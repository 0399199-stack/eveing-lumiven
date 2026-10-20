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
    private readonly Dictionary<string, DateTimeOffset> _removed = [];
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
                using var document = await JsonDocument.ParseAsync(stream);
                // Older versions stored just the item array.
                var index = document.RootElement.ValueKind == JsonValueKind.Array
                    ? new LibraryIndex { Items = document.RootElement.Deserialize<List<MediaItem>>(AppServices.JsonOptions) ?? [] }
                    : document.RootElement.Deserialize<LibraryIndex>(AppServices.JsonOptions) ?? throw new JsonException("Invalid library index");
                foreach (var item in index.Items) _items[item.FilePath] = item;
                foreach (var entry in index.Removed) _removed[entry.Key] = entry.Value;
            }
            catch (JsonException ex) { throw new IOException(Localizer.T("无法读取媒体库，原索引已保留。"), ex); }
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
            if (IsRemoved(item.FilePath, task.UpdatedAt)) return;
            if (_items.TryGetValue(item.FilePath, out var previous)) item.Thumbnail = previous.Thumbnail;
            var key = PathKey(item.FilePath);
            var hadRemoval = _removed.TryGetValue(key, out var removedAt);
            _items[item.FilePath] = item;
            _removed.Remove(key);
            try { await SaveLockedAsync(); }
            catch
            {
                if (previous is null) _items.Remove(item.FilePath);
                else _items[item.FilePath] = previous;
                if (hadRemoval) _removed[key] = removedAt;
                throw;
            }
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
                if (IsRemoved(path, task.UpdatedAt)) continue;
                _items.TryGetValue(path, out var previous);
                _items[path] = new MediaItem
                {
                    Id = task.Id, Title = task.Title, Author = task.Author, Quality = task.Quality, FilePath = path,
                    SizeBytes = task.BytesTotal > 0 ? task.BytesTotal : task.BytesReceived, CompletedAt = task.UpdatedAt,
                    Thumbnail = previous?.Thumbnail ?? ""
                };
                _removed.Remove(PathKey(path));
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
            foreach (var item in discovered)
                if (!_removed.ContainsKey(PathKey(item.FilePath)) && !_items.ContainsKey(item.FilePath)) _items[item.FilePath] = item;
            await SaveLockedAsync();
            return discovered.Where(x => _items.ContainsKey(x.FilePath)).Select(x => _items[x.FilePath]).OrderByDescending(x => x.CompletedAt).ToList();
        }
        finally { _gate.Release(); }
    }

    private sealed class LibraryIndex
    {
        public List<MediaItem> Items { get; set; } = [];
        public Dictionary<string, DateTimeOffset> Removed { get; set; } = [];
    }

    private static string PathKey(string path) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));

    private bool IsRemoved(string path, DateTimeOffset completedAt) =>
        _removed.TryGetValue(PathKey(path), out var removedAt) && completedAt <= removedAt;

    private Task SaveLockedAsync() => AtomicJson.WriteAsync(IndexPath,
        new LibraryIndex { Items = _items.Values.ToList(), Removed = _removed }, AppServices.JsonOptions);

    public async Task RemoveAsync(MediaItem item, bool deleteFile, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadLockedAsync();
            if (!_items.TryGetValue(item.FilePath, out var stored)) return;
            if (stored.Id != item.Id || stored.CompletedAt != item.CompletedAt)
                throw new IOException(Localizer.T("记录已更新，请刷新后重试。"));
            cancellationToken.ThrowIfCancellationRequested();
            if (deleteFile) await Task.Run(() => DeleteRecordedFile(stored.FilePath), cancellationToken);

            var key = PathKey(stored.FilePath);
            var removed = new Dictionary<string, DateTimeOffset>(_removed)
            {
                [key] = stored.CompletedAt > DateTimeOffset.UtcNow ? stored.CompletedAt : DateTimeOffset.UtcNow
            };
            // Persist before changing the visible collection. A failed file deletion keeps the record.
            await AtomicJson.WriteAsync(IndexPath, new LibraryIndex
            {
                Items = _items.Values.Where(x => !ReferenceEquals(x, stored)).ToList(), Removed = removed
            }, AppServices.JsonOptions);
            _items.Remove(stored.FilePath);
            _removed[key] = removed[key];
        }
        finally { _gate.Release(); }
    }

    public async Task<(IReadOnlyList<MediaItem> Removed, IReadOnlyList<MediaItem> Failed)> RemoveManyAsync(
        IReadOnlyList<MediaItem> items, bool deleteFiles, CancellationToken cancellationToken = default)
    {
        var removed = new List<MediaItem>();
        var failed = new List<MediaItem>();
        foreach (var item in items.DistinctBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await RemoveAsync(item, deleteFiles, cancellationToken); removed.Add(item); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(item); }
        }
        return (removed, failed);
    }

    private static void DeleteRecordedFile(string path)
    {
        // Only this recorded file is removed, never a directory or a redirected path.
        for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
        {
            if (parent.Exists && (parent.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new IOException(Localizer.T("文件路径已重定向，请在资源管理器中核对后删除。"));
        }
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint)) != 0)
                throw new IOException(Localizer.T("文件路径已改变，不能删除目录或链接。"));
            File.Delete(path);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

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
        if (!File.Exists(item.FilePath)) throw new FileNotFoundException(Localizer.T("视频文件已移动或删除"), item.FilePath);
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
            if (directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException(Localizer.T("原保存文件夹已不存在"));
            start.ArgumentList.Add(directory);
        }
        Process.Start(start);
    }
}
