using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumiven.Models;

namespace Lumiven.Services;

public enum EngineRepairResult { NoActionNeeded, Recovered }

public sealed class EngineClient(AppSettings settings, LibraryService library, string dataDirectory)
{
    private readonly ConcurrentDictionary<string, DownloadTask> _tasks = new();
    private readonly object _taskUpdatesGate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? _activation;
    private CancellationTokenSource? _session;
    private Task? _pollTask, _listenTask;
    private ClientWebSocket? _socket;
    private Process? _process;
    private Process? _recoveryProcess;
    private Uri? _baseUri;
    private string _token = "";
    private bool _stopRequested;
    private bool _interruptedCachePending;
    private string CoreDirectory => Path.Combine(dataDirectory, "core");
    private string SystemLeasePath => Path.Combine(CoreDirectory, "system-lease.json");
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<DownloadTask>? TaskUpdated;
    public bool IsRunning { get; private set; }
    private string _statusText = "微信下载未启用";
    public string StatusText => Localizer.TranslateExisting(_statusText);
    public string StatusState { get; private set; } = "disabled";
    public bool HasPendingRecovery => IsRunning || !HasEngineExited() || _recoveryProcess is not null ||
        StatusState == "recovery_required" || SystemLeaseExists();

    public async Task InitializeAsync()
    {
        if (SystemLeaseExists())
            SetStatus("recovery_required", "上次接入的恢复尚未确认，请先点击停止接入并恢复原代理。");
        var path = Path.Combine(dataDirectory, "task-cache.json");
        if (!File.Exists(path)) return;
        try
        {
            await using var stream = File.OpenRead(path);
            var cached = await JsonSerializer.DeserializeAsync<List<DownloadTask>>(stream, AppServices.JsonOptions);
            foreach (var task in cached ?? [])
            {
                if (task.Status is not ("completed" or "failed" or "interrupted"))
                {
                    task.Status = "interrupted";
                    task.Error = "上次下载已中断，请在微信中重新点击下载";
                }
                task.NormalizeProgress();
                _tasks[task.Id] = task;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { AppServices.Log("恢复任务", ex); }
    }

    public async Task EnableAsync()
    {
        await _lifecycle.WaitAsync();
        CancellationTokenSource? activation = null;
        try
        {
            if (IsRunning && StatusState == "ready" && !_stopRequested) return;
            // Keep the old process and token until its exit and proxy restoration are confirmed.
            if (_process is not null || SystemLeaseExists()) await StopCoreAsync();
            var enginePath = FindEnginePath();
            if (enginePath is null) throw new FileNotFoundException(Localizer.T("找不到下载内核，请重新安装 EVEING Lumiven。"));
            var coreDirectory = CoreDirectory;
            Directory.CreateDirectory(coreDirectory);
            Directory.CreateDirectory(settings.DownloadDirectory);
            var port = FindPortPair();
            _baseUri = new Uri($"http://127.0.0.1:{port + 1}/");
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _http.DefaultRequestHeaders.Remove("X-Local-Auth");
            _http.DefaultRequestHeaders.Add("X-Local-Auth", _token);
            var configPath = Path.Combine(coreDirectory, "config.yaml");
            var yaml = $"port: {port}\ndownload_dir: {JsonSerializer.Serialize(settings.DownloadDirectory)}\ndownload_concurrency: {settings.Concurrency}\ncloud_enabled: false\nradar_enabled: false\nauto_open_channels: false\nlog_file: {JsonSerializer.Serialize(Path.Combine(coreDirectory, "core.log"))}\n";
            await File.WriteAllTextAsync(configPath, yaml, new UTF8Encoding(false));
            var args = new[] { "--desktop-managed", "--desktop-token", _token, "--desktop-parent", Environment.ProcessId.ToString(), "--desktop-data-dir", coreDirectory, "--config", configPath, "--port", port.ToString() };
            var start = new ProcessStartInfo(enginePath)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = coreDirectory
            };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            activation = new CancellationTokenSource();
            _activation = activation;
            SetStatus("starting", "正在等待 Windows 安全确认；如出现系统提示，请确认。可点击停止接入取消。");
            _stopRequested = false;
            activation.Token.ThrowIfCancellationRequested();
            _process = Process.Start(start);
            if (_process is null) throw new InvalidOperationException(Localizer.T("Windows 未返回下载内核进程，无法确认接入状态。"));
            _session = new CancellationTokenSource();
            var startupTimeout = Stopwatch.StartNew();
            while (startupTimeout.Elapsed < TimeSpan.FromSeconds(150))
            {
                try
                {
                    var status = await ReadStatusAsync(activation.Token);
                    if (status.State == "failed") throw new InvalidOperationException(status.Error ?? Localizer.T("微信下载接入失败"));
                    if (status.State == "ready")
                    {
                        activation.Token.ThrowIfCancellationRequested();
                        IsRunning = true;
                        SetStatus("ready", "服务已启用 · 请打开或刷新微信视频号");
                        await FetchSnapshotAsync(activation.Token);
                        activation.Token.ThrowIfCancellationRequested();
                        StartMonitoring();
                        return;
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!activation.IsCancellationRequested) { }
                if (HasEngineExited()) break;
                await Task.Delay(500, activation.Token);
            }
            throw new InvalidOperationException(Localizer.T("下载服务未能启动，请检查权限与日志后重试。"));
        }
        catch (OperationCanceledException) when (activation?.IsCancellationRequested == true) { }
        catch (Exception ex)
        {
            AppServices.Log("启用下载", ex);
            if (StatusState == "recovery_required") throw;
            try
            {
                await StopCoreAsync();
                SetStatus("failed", $"启用失败，接入已停止：{AppServices.UserError(ex)}");
            }
            catch (Exception cleanupError)
            {
                if (!HasEngineExited()) StartMonitoring();
                AppServices.Log("启用失败后的恢复", cleanupError);
                throw new InvalidOperationException(Localizer.T("启用失败且接入恢复尚未确认，请点击停止接入并查看本地日志。"), ex);
            }
            throw;
        }
        finally
        {
            _activation = null;
            activation?.Dispose();
            _lifecycle.Release();
        }
    }

    public Task StopAsync() => StopAndSaveAsync(onlyIfNeeded: false);

    public Task<EngineRepairResult> RepairAsync() => StopAndSaveAsync(onlyIfNeeded: true);

    private async Task<EngineRepairResult> StopAndSaveAsync(bool onlyIfNeeded)
    {
        try { _activation?.Cancel(); } catch (ObjectDisposedException) { }
        await _lifecycle.WaitAsync();
        try
        {
            bool recovered = await StopCoreAsync();
            if (recovered || !onlyIfNeeded) await SaveCacheAsync();
            return recovered ? EngineRepairResult.Recovered : EngineRepairResult.NoActionNeeded;
        }
        catch
        {
            if (!HasEngineExited()) StartMonitoring();
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    private async Task<bool> StopCoreAsync()
    {
        // Inspect the owned lease even if no process is running. An unreadable lease is not a clean state.
        bool hasLease = SystemLeaseExists();
        bool hadPendingRecovery = hasLease || _recoveryProcess is not null || !HasEngineExited();
        _stopRequested = true;
        Exception? shutdownError = null;
        if (!HasEngineExited())
        {
            IsRunning = true;
            SetStatus("stopping", "正在停止接入并恢复原代理；如出现 Windows 安全确认，请确认…");
            try { await RequestShutdownAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                shutdownError = ex;
                AppServices.Log("请求停止内核", ex);
            }
            var timeout = Stopwatch.StartNew();
            while (!HasEngineExited() && timeout.Elapsed < TimeSpan.FromSeconds(150)) await Task.Delay(100);
            if (!HasEngineExited())
            {
                var message = "下载内核仍在运行，停止尚未确认。请重试停止接入；恢复完成前不能重新启用或退出。";
                IsRunning = true;
                SetStatus("recovery_required", message);
                throw new InvalidOperationException(Localizer.T(message), shutdownError);
            }
        }
        // A native process exit alone does not prove that its deferred system cleanup succeeded.
        CancelSession();
        IsRunning = false;
        try
        {
            await RecoverOwnedSystemAsync();
        }
        catch (Exception ex)
        {
            AppServices.Log("停止接入后恢复", ex);
            SetStatus("recovery_required", "内核已退出，但代理或证书恢复尚未确认。请重试停止接入并查看本地日志。");
            throw;
        }
        _process?.Dispose(); _process = null;
        _baseUri = null; _token = "";
        SetStatus("disabled", hadPendingRecovery ? "微信下载已停止，原代理已恢复" : "微信下载未启用");
        await InterruptUnfinishedTasksAsync();
        return hadPendingRecovery;
    }

    private async Task RequestShutdownAsync()
    {
        if (_baseUri is null) throw new InvalidOperationException(Localizer.T("缺少原内核管理地址，无法请求停止。"));
        using var response = await _http.PostAsync(new Uri(_baseUri, "api/desktop/shutdown"), null);
        response.EnsureSuccessStatusCode();
    }

    private void CancelSession()
    {
        _session?.Cancel();
        try { _socket?.Abort(); } catch (ObjectDisposedException) { }
        _socket = null;
        _pollTask = null; _listenTask = null;
    }

    private void StartMonitoring()
    {
        if (_session is null || _session.IsCancellationRequested) return;
        if (_pollTask is null || _pollTask.IsCompleted) _pollTask = PollAsync(_session.Token);
        if (_listenTask is null || _listenTask.IsCompleted) _listenTask = ListenAsync(_session.Token);
    }

    private async Task RecoverOwnedSystemAsync()
    {
        if (_recoveryProcess is null && !SystemLeaseExists()) return;
        if (_recoveryProcess is null)
        {
            string executable = FindEnginePath() ?? throw new FileNotFoundException(Localizer.T("缺少用于恢复原代理的下载内核，请重新安装软件后重试。"));
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = CoreDirectory
            };
            start.ArgumentList.Add("--desktop-recover");
            start.ArgumentList.Add("--desktop-data-dir");
            start.ArgumentList.Add(CoreDirectory);
            _recoveryProcess = Process.Start(start) ?? throw new InvalidOperationException(Localizer.T("无法启动代理恢复工具。"));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        try { await _recoveryProcess.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new InvalidOperationException(Localizer.T("代理恢复工具仍在运行，请稍后重试。")); }
        int exitCode = _recoveryProcess.ExitCode;
        _recoveryProcess.Dispose(); _recoveryProcess = null;
        if (exitCode != 0 || SystemLeaseExists())
            throw new InvalidOperationException(Localizer.T("代理恢复工具未确认完成，请检查接入日志。"));
    }

    private bool SystemLeaseExists()
    {
        try { _ = File.GetAttributes(SystemLeasePath); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public Task<IReadOnlyList<DownloadTask>> GetTasksAsync() => Task.FromResult<IReadOnlyList<DownloadTask>>(_tasks.Values.OrderByDescending(x => x.CreatedAt).ToList());

    public async Task<IReadOnlyList<MediaItem>> GetDownloadedAsync()
    {
        var items = await library.GetAllAsync();
        await library.RefreshAsync(items);
        return items;
    }

    private async Task FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _http.GetFromJsonAsync<TaskSnapshot>(new Uri(_baseUri!, "api/desktop/tasks"), AppServices.JsonOptions, cancellationToken);
        var completed = new List<DownloadTask>();
        foreach (var task in snapshot?.Tasks ?? [])
        {
            if (!ApplyTaskUpdate(task, cancellationToken, skipUnchanged: true)) continue;
            if (task.Status == "completed") completed.Add(task);
        }
        if (completed.Count != 0) await library.RecordManyAsync(completed);
        await SaveCacheAsync();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(4000, cancellationToken);
                var status = await ReadStatusAsync(cancellationToken);
                if (status.State == "failed") throw new InvalidOperationException(status.Error ?? Localizer.T("内核报告接入失败"));
                await FetchSnapshotAsync(cancellationToken);
                failures = 0;
                if (status.State == "ready" && !_stopRequested && StatusState != "ready")
                {
                    IsRunning = true;
                    SetStatus("ready", "服务已重新连接 · 请打开或刷新微信视频号");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                if (cancellationToken.IsCancellationRequested) break;
                bool exited = HasEngineExited();
                // Keep watching after a timed-out stop so a later exit can restore the proxy.
                if (_stopRequested && !exited) continue;
                if (++failures < 3 && !exited) continue;
                if (!exited)
                {
                    IsRunning = true;
                    SetStatus("recovery_required", "下载服务已断开，但内核仍在运行。请先停止接入并确认原代理恢复，再重新启用。");
                    if (failures == 3) AppServices.Log("下载服务连接", ex);
                    continue;
                }
                bool locked = false;
                try
                {
                    await _lifecycle.WaitAsync(cancellationToken); locked = true;
                    if (_session?.Token != cancellationToken) break;
                    IsRunning = false;
                    await RecoverOwnedSystemAsync();
                    CancelSession();
                    await InterruptUnfinishedTasksAsync();
                    _process?.Dispose(); _process = null;
                    _baseUri = null; _token = "";
                    if (_stopRequested) SetStatus("disabled", "微信下载已停止，原代理已恢复");
                    else
                    {
                        SetStatus("failed", "下载内核已退出，原代理已恢复。可重新启用微信下载。");
                        AppServices.Log("下载内核退出", ex);
                    }
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception recoveryError)
                {
                    SetStatus("recovery_required", "内核已退出，但原代理恢复尚未确认。请重试停止接入并查看本地日志。");
                    AppServices.Log("内核退出后恢复", recoveryError);
                    // Keep monitoring/retrying the recovery rather than hiding an incomplete cleanup.
                }
                finally { if (locked) _lifecycle.Release(); }
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            _socket = socket;
            socket.Options.SetRequestHeader("X-Local-Auth", _token);
            try
            {
                var uri = new UriBuilder(new Uri(_baseUri!, "ws/desktop")) { Scheme = "ws" }.Uri;
                await socket.ConnectAsync(uri, cancellationToken);
                await FetchSnapshotAsync(cancellationToken);
                var buffer = new byte[16384];
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    ValueWebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        message.Write(buffer, 0, result.Count);
                        if (message.Length > 1024 * 1024) throw new JsonException("任务消息过大");
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Position = 0;
                    var notification = await JsonSerializer.DeserializeAsync<TaskNotification>(message, AppServices.JsonOptions, cancellationToken);
                    if (notification?.Task is not { } task) continue;
                    if (task.Status == "completed") await library.RecordAsync(task);
                    ApplyTaskUpdate(task, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or JsonException or IOException)
            {
                AppServices.Log("下载事件连接", ex);
            }
            try { await Task.Delay(1500, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<EngineStatus> ReadStatusAsync(CancellationToken token) =>
        await _http.GetFromJsonAsync<EngineStatus>(new Uri(_baseUri!, "api/desktop/status"), AppServices.JsonOptions, token) ?? new();

    internal bool ApplyTaskUpdate(DownloadTask task, CancellationToken cancellationToken, bool skipUnchanged = false)
    {
        lock (_taskUpdatesGate)
        {
            // A socket or snapshot from a cancelled session must never revive a stopped task.
            if (cancellationToken.IsCancellationRequested) return false;
            if (skipUnchanged && _tasks.TryGetValue(task.Id, out var previous) && previous.UpdatedAt == task.UpdatedAt &&
                previous.Status == task.Status && previous.BytesReceived == task.BytesReceived) return false;
            task.NormalizeProgress();
            _tasks[task.Id] = task;
            TaskUpdated?.Invoke(this, task);
            return true;
        }
    }

    private async Task InterruptUnfinishedTasksAsync()
    {
        lock (_taskUpdatesGate)
        {
            foreach (var task in _tasks.Values)
            {
                if (task.Status is "completed" or "failed" or "cancelled" or "interrupted") continue;
                task.Status = "interrupted";
                task.Error = "上次下载已中断，请在微信中重新点击下载";
                task.Speed = 0;
                task.UpdatedAt = DateTimeOffset.Now;
                task.NormalizeProgress();
                _interruptedCachePending = true;
                TaskUpdated?.Invoke(this, task);
            }
        }
        if (!_interruptedCachePending) return;
        // Keep the pending flag on failure so a later repair retries persistence.
        await SaveCacheAsync();
        _interruptedCachePending = false;
    }

    private async Task SaveCacheAsync()
    {
        await _cacheGate.WaitAsync();
        try { await AtomicJson.WriteAsync(Path.Combine(dataDirectory, "task-cache.json"), _tasks.Values.ToList(), AppServices.JsonOptions); }
        finally { _cacheGate.Release(); }
    }

    private void SetStatus(string state, string text)
    {
        StatusState = state;
        _statusText = text;
        StatusChanged?.Invoke(this, StatusText);
    }

    private bool HasEngineExited()
    {
        if (_process is null) return true;
        try { return _process.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return false; }
    }

    private static string? FindEnginePath()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "lumiven-core.exe");
        if (File.Exists(installed)) return installed;
        var direct = Path.Combine(AppContext.BaseDirectory, "core", "lumiven-core.exe");
        if (File.Exists(direct)) return direct;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "artifacts", "core", "lumiven-core.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static int FindPortPair()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var port = RandomNumberGenerator.GetInt32(20000, 50000);
            TcpListener? first = null, second = null;
            try
            {
                first = new TcpListener(IPAddress.Loopback, port); first.Start();
                second = new TcpListener(IPAddress.Loopback, port + 1); second.Start();
                return port;
            }
            catch (SocketException) { }
            finally { first?.Stop(); second?.Stop(); }
        }
        throw new InvalidOperationException(Localizer.T("找不到可用的本地端口，请关闭旧下载器后重试。"));
    }

    private sealed class TaskSnapshot { public List<DownloadTask> Tasks { get; set; } = []; }
    private sealed class TaskNotification { public string Type { get; set; } = ""; public DownloadTask? Task { get; set; } }
    private sealed class EngineStatus { public string State { get; set; } = "starting"; public string? Error { get; set; } }
}
