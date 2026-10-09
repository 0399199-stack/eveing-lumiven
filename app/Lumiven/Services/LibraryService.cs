using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumiven.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Lumiven.Services;

public sealed class LibraryService(string dataDirectory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _thumbnails = new(2, 2);
    private readonly Dictionary<string, MediaItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private string IndexPath => Path.Combine(dataDirectory, "library.json");
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi" };

    private async Task LoadLockedAsync()
    {
        if (_loaded) return;
        if (File.Exists(IndexPath))
        {
            try
            {
                await using var stream = File.OpenRead(IndexPath);
                var items = await JsonSerializer.DeserializeAsync<List<MediaItem>>(stream, AppServices.JsonOptions);
                foreach (var item in items ?? []) _items[item.FilePath] = item;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppServices.Log("读取媒体库", ex); }
        }
        _loaded = true;
    }

    public async Task<IReadOnlyList<MediaItem>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await LoadLockedAsync();
            return _items.Values.OrderByDescending(x => x.CompletedAt).ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task RecordAsync(DownloadTask task)
    {
        if (task.Status != "completed" || string.IsNullOrWhiteSpace(task.FilePath)) return;
        var item = new MediaItem
        {
            Id = task.Id, Title = task.Title, Author = task.Author, Quality = task.Quality,
            FilePath = Path.GetFullPath(task.FilePath), SizeBytes = task.BytesTotal > 0 ? task.BytesTotal : task.BytesReceived,
            CompletedAt = task.UpdatedAt == default ? DateTimeOffset.Now : task.UpdatedAt
        };
        item.IsMissing = !await Task.Run(() => File.Exists(item.FilePath));
        await _gate.WaitAsync();
        try
        {
            await LoadLockedAsync();
            if (_items.TryGetValue(item.FilePath, out var previous)) item.Thumbnail = previous.Thumbnail;
            _items[item.FilePath] = item;
            await SaveLockedAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task RecordManyAsync(IEnumerable<DownloadTask> tasks)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadLockedAsync();
            foreach (var task in tasks)
            {
                if (task.Status != "completed" || string.IsNullOrWhiteSpace(task.FilePath)) continue;
                var path = Path.GetFullPath(task.FilePath);
                _items.TryGetValue(path, out var previous);
                _items[path] = new MediaItem
                {
                    Id = task.Id, Title = task.Title, Author = task.Author, Quality = task.Quality, FilePath = path,
                    SizeBytes = task.BytesTotal > 0 ? task.BytesTotal : task.BytesReceived, CompletedAt = task.UpdatedAt,
                    Thumbnail = previous?.Thumbnail ?? ""
                };
            }
            await SaveLockedAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<MediaItem>> ScanAsync(string directory, CancellationToken cancellationToken = default)
    {
        directory = Path.GetFullPath(directory);
        var discovered = await Task.Run(() =>
        {
            var items = new List<MediaItem>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = System.IO.FileAttributes.ReparsePoint | System.IO.FileAttributes.System };
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!VideoExtensions.Contains(Path.GetExtension(file))) continue;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length == 0) continue;
                    items.Add(new MediaItem
                    {
                        Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(info.FullName))).ToLowerInvariant(),
                        FilePath = info.FullName, Title = Path.GetFileNameWithoutExtension(file),
                        Author = string.Equals(info.DirectoryName, directory, StringComparison.OrdinalIgnoreCase) ? "" : info.Directory?.Name ?? "",
                        SizeBytes = info.Length, CompletedAt = info.LastWriteTimeUtc
                    });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return items;
        }, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadLockedAsync();
            foreach (var item in discovered) if (!_items.ContainsKey(item.FilePath)) _items[item.FilePath] = item;
            await SaveLockedAsync();
            return discovered.Select(x => _items[x.FilePath]).OrderByDescending(x => x.CompletedAt).ToList();
        }
        finally { _gate.Release(); }
    }

    private Task SaveLockedAsync() => AtomicJson.WriteAsync(IndexPath, _items.Values.ToList(), AppServices.JsonOptions);

    public async Task RefreshAsync(IEnumerable<MediaItem> items)
    {
        var snapshots = items.ToList();
        var missing = await Task.Run(() => snapshots.Select(x => !File.Exists(x.FilePath)).ToArray());
        for (var i = 0; i < snapshots.Count; i++) snapshots[i].IsMissing = missing[i];
    }

    public async Task<string?> GetThumbnailAsync(MediaItem item)
    {
        if (item.IsMissing) return null;
        await _thumbnails.WaitAsync();
        try
        {
            var metadata = await Task.Run(() =>
            {
                var file = new FileInfo(item.FilePath);
                return file.Exists ? (Exists: true, Length: file.Length, Modified: file.LastWriteTimeUtc.Ticks) : (Exists: false, Length: 0L, Modified: 0L);
            });
            if (!metadata.Exists) return null;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{item.FilePath}|{metadata.Length}|{metadata.Modified}")));
            var cacheDirectory = Path.Combine(dataDirectory, "thumbnails");
            Directory.CreateDirectory(cacheDirectory);
            var cacheFile = Path.Combine(cacheDirectory, key + ".jpg");
            if (!File.Exists(cacheFile))
            {
                var source = await StorageFile.GetFileFromPathAsync(item.FilePath);
                using var thumbnail = await source.GetThumbnailAsync(ThumbnailMode.VideosView, 320, ThumbnailOptions.UseCurrentScale);
                if (thumbnail is null || thumbnail.Size == 0) return null;
                await using var output = new FileStream(cacheFile, FileMode.Create, FileAccess.Write, FileShare.Read, 8192, FileOptions.Asynchronous);
                await thumbnail.AsStreamForRead().CopyToAsync(output);
            }
            item.Thumbnail = cacheFile;
            return cacheFile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            AppServices.Log("生成视频封面", ex);
            return null;
        }
        finally { _thumbnails.Release(); }
    }

    public void OpenFile(MediaItem item)
    {
        if (!File.Exists(item.FilePath)) throw new FileNotFoundException("视频文件已移动或删除", item.FilePath);
        Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
    }

    public void OpenFolder(MediaItem item)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        if (File.Exists(item.FilePath))
        {
            start.Arguments = "/select,\"" + item.FilePath + "\"";
        }
        else
        {
            var directory = Path.GetDirectoryName(item.FilePath);
            if (directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException("原保存文件夹已不存在");
            start.ArgumentList.Add(directory);
        }
        Process.Start(start);
    }
}
