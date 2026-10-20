using System.Diagnostics;
using System.Text.Json;
using Lumiven.Models;

namespace Lumiven.Services;

public sealed class LinkDownloadService(AppSettings settings, LibraryService library, string dataDirectory)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DownloadTask> _tasks = [];
    private readonly Dictionary<string, (CancellationTokenSource Cancellation, Task Work)> _active = [];
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private bool _stopping;
    internal Func<HttpClient> ClientFactory { get; init; } = VideoLinkHttp.CreateClient;
    private string CachePath => Path.Combine(dataDirectory, "link-tasks.json");
    public event EventHandler<DownloadTask>? TaskUpdated;
    public bool HasActiveTasks { get { lock (_gate) return _active.Count != 0; } }
    public IReadOnlyList<DownloadTask> GetTasks() { lock (_gate) return _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList(); }
    internal Task WhenIdleAsync() { lock (_gate) return Task.WhenAll(_active.Values.Select(x => x.Work).ToArray()); }
    private static DownloadTask Copy(DownloadTask task) =>
        JsonSerializer.Deserialize<DownloadTask>(JsonSerializer.Serialize(task, AppServices.JsonOptions), AppServices.JsonOptions)!;

    public async Task InitializeAsync()
    {
        if (!File.Exists(CachePath)) return;
        List<DownloadTask> tasks;
        await using (var file = File.OpenRead(CachePath))
            tasks = await JsonSerializer.DeserializeAsync<List<DownloadTask>>(file, AppServices.JsonOptions) ?? [];
        lock (_gate)
        {
            foreach (var task in tasks)
            {
                if (task.Status is not ("completed" or "failed" or "interrupted" or "cancelled"))
                {
                    task.Status = "interrupted";
                    task.Error = "上次下载已中断，请重新粘贴分享链接。";
                }
                _tasks[task.Id] = task;
            }
        }
        await SaveAsync();
    }

    public static bool IsLinkTask(string id) => id.StartsWith("douyin-", StringComparison.Ordinal) || id.StartsWith("xhs-", StringComparison.Ordinal);

    public void Start(string text, bool xiaohongshu = false)
    {
        var url = xiaohongshu ? XiaohongshuLink.Extract(text) : DouyinShortLink.Extract(text);
        var platform = xiaohongshu ? "小红书" : "抖音";
        var task = new DownloadTask { Id = (xiaohongshu ? "xhs-" : "douyin-") + Guid.NewGuid().ToString("N"), Title = platform + " · 正在解析分享链接",
            Status = "queued", CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now };
        lock (_gate)
        {
            if (_stopping) throw new InvalidOperationException("软件正在退出，请稍后重试。");
            if (_active.Count >= 5) throw new InvalidDataException("已有 5 个链接任务，请等待下载完成后再添加。");
            var cancellation = new CancellationTokenSource();
            _tasks[task.Id] = Copy(task);
            _active[task.Id] = (cancellation, Task.Run(() => RunAsync(task, url, cancellation, xiaohongshu)));
        }
    }

    public void Cancel(string id)
    {
        lock (_gate)
            if (_active.TryGetValue(id, out var operation)) operation.Cancellation.Cancel();
    }

    public async Task StopAsync()
    {
        Task[] running;
        lock (_gate)
        {
            _stopping = true;
            foreach (var operation in _active.Values) operation.Cancellation.Cancel();
            running = _active.Values.Select(x => x.Work).ToArray();
        }
        try { await Task.WhenAll(running); }
        finally { lock (_gate) _stopping = false; }
    }

    private void Publish(DownloadTask task)
    {
        task.UpdatedAt = DateTimeOffset.Now;
        task.NormalizeProgress();
        lock (_gate) _tasks[task.Id] = Copy(task);
        TaskUpdated?.Invoke(this, Copy(task));
    }

    private async Task SaveAsync()
    {
        await _cacheGate.WaitAsync();
        try { await AtomicJson.WriteAsync(CachePath, GetTasks(), AppServices.JsonOptions); }
        finally { _cacheGate.Release(); }
    }

    private async Task RunAsync(DownloadTask task, Uri shortLink, CancellationTokenSource cancellation, bool xiaohongshu)
    {
        var token = cancellation.Token;
        string? partial = null;
        bool entered = false;
        try
        {
            Publish(task);
            await SaveAsync();
            await _downloadGate.WaitAsync(token);
            entered = true;
            task.Status = "parsing"; Publish(task);
            using var http = ClientFactory();
            using var parseTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            parseTimeout.CancelAfter(TimeSpan.FromSeconds(50));
            var video = xiaohongshu ? await XiaohongshuLink.ResolveAsync(http, shortLink, parseTimeout.Token) : await DouyinShortLink.ResolveAsync(http, shortLink, parseTimeout.Token);
            task.VideoId = video.Id; task.Title = video.Title; task.Author = video.Author; task.Quality = video.Quality;
            var directory = Path.GetFullPath(settings.DownloadDirectory);
            Directory.CreateDirectory(directory);
            var title = string.Concat(video.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            title = title[..Math.Min(title.Length, 60)].Trim().TrimEnd('.');
            if (title.Length == 0) title = "视频";
            var final = Path.Combine(directory, $"{title}_{video.Id}_{task.Id[^8..]}.mp4");
            partial = Path.Combine(dataDirectory, "link-partials", task.Id + ".part");
            Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
            task.Status = "downloading"; Publish(task);
            await SaveAsync();
            using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            downloadTimeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var response = await VideoLinkHttp.GetAsync(http, video.Url, true, downloadTimeout.Token, xiaohongshu);
            task.BytesTotal = response.Content.Headers.ContentLength ?? 0;
            await using (var source = await response.Content.ReadAsStreamAsync(downloadTimeout.Token))
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536];
                var timer = Stopwatch.StartNew();
                long previous = 0;
                int count;
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(downloadTimeout.Token);
                    idle.CancelAfter(TimeSpan.FromSeconds(30));
                    count = await source.ReadAsync(buffer, idle.Token);
                    if (count == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, count), downloadTimeout.Token);
                    task.BytesReceived += count;
                    if (timer.ElapsedMilliseconds < 250) continue;
                    task.Speed = (long)((task.BytesReceived - previous) / timer.Elapsed.TotalSeconds);
                    previous = task.BytesReceived; timer.Restart(); Publish(task);
                }
                await file.FlushAsync(downloadTimeout.Token);
            }
            await ValidateFileAsync(partial, task.BytesTotal, token);
            task.Status = "saving"; Publish(task);
            token.ThrowIfCancellationRequested();
            // Cross-volume copies still use an excluded temporary extension in the final directory.
            var staging = final + ".part";
            try
            {
                await using (var input = File.OpenRead(partial))
                await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    await input.CopyToAsync(output, token);
                token.ThrowIfCancellationRequested();
                File.Move(staging, final);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            task.FilePath = final;
            var completed = Copy(task);
            completed.Status = "completed";
            completed.UpdatedAt = DateTimeOffset.Now;
            await library.RecordAsync(completed);
            task.Status = "completed";
        }
        catch (OperationCanceledException)
        {
            task.Status = token.IsCancellationRequested ? "cancelled" : "failed";
            task.Error = token.IsCancellationRequested ? "已取消。重新下载请再次粘贴链接。" : "连接视频平台超时，请稍后重试。";
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.Error = ex is InvalidDataException ? ex.Message :
                ex is HttpRequestException ? "视频平台暂未提供可用的视频响应，请稍后重试。" :
                "文件保存或记录写入失败，请检查保存目录、磁盘空间及权限。";
            AppServices.Log("视频链接下载", ex);
        }
        finally
        {
            try { if (partial is not null && File.Exists(partial)) File.Delete(partial); }
            catch (IOException ex) { AppServices.Log("清理链接临时文件", ex); }
            catch (UnauthorizedAccessException ex) { AppServices.Log("清理链接临时文件", ex); }
            try { Publish(task); await SaveAsync(); }
            catch (Exception ex) { AppServices.Log("保存链接任务", ex); }
            if (entered) _downloadGate.Release();
            lock (_gate) { _active.Remove(task.Id); cancellation.Dispose(); }
        }
    }

    internal static async Task ValidateFileAsync(string path, long expectedBytes, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        if (file.Length < 12 || (expectedBytes > 0 && file.Length != expectedBytes))
            throw new InvalidDataException("视频文件未下载完整，请重试。");
        byte[] header = new byte[12];
        await file.ReadExactlyAsync(header, token);
        if (!header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            throw new InvalidDataException("平台返回的内容不是 MP4 视频，未计为下载成功。");
    }
}
