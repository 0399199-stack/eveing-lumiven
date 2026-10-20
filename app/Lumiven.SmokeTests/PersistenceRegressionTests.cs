using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumiven.Models;
using Lumiven.Services;

internal static class PersistenceRegressionTests
{
    private static readonly byte[] Mp4 = [0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 109, 112, 52, 49];
    private const string VideoId = "1234567890123456789";
    private const string XhsId = "1234567890abcdef12345678";

    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "lumiven-persistence-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await VerifyLibraryRollbackAsync(Path.Combine(root, "library"));
        await VerifyScanRollbackAsync(Path.Combine(root, "scan"));
        foreach (string mode in new[] { "cache-failure", "partial-response", "xhs-fallback", "xhs-all-failed" })
            await VerifyDownloadAsync(Path.Combine(root, mode), mode);
        Console.WriteLine("PASS library rollback, terminal task persistence/recovery, partial-response rejection and XHS fallback completion");
    }

    private static async Task VerifyLibraryRollbackAsync(string root)
    {
        Directory.CreateDirectory(root);
        string originalPath = Path.Combine(root, "original.mp4"), addedPath = Path.Combine(root, "new.mp4");
        string removedDirectory = Path.Combine(root, "removed");
        Directory.CreateDirectory(removedDirectory);
        string removedPath = Path.Combine(removedDirectory, "removed.mp4");
        foreach (string path in new[] { originalPath, addedPath, removedPath }) await File.WriteAllBytesAsync(path, Mp4);
        var library = new LibraryService(root);
        var original = Completed("original", originalPath, "original title");
        await library.RecordAsync(original);
        await library.RecordAsync(Completed("removed", removedPath, "removed"));
        await library.RemoveAsync((await library.GetAllAsync()).Single(item => item.Id == "removed"), false);
        string index = Path.Combine(root, "library.json");
        string before = await File.ReadAllTextAsync(index);
        Directory.CreateDirectory(index + ".tmp");
        var updated = Completed("original", originalPath, "changed title");
        var revived = Completed("new-download", removedPath, "new download");
        revived.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        bool failed = false;
        try { await library.RecordManyAsync([updated, Completed("added", addedPath, "new"), revived]); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Check(failed && (await library.GetAllAsync()).Single().Title == "original title" && await File.ReadAllTextAsync(index) == before,
            "A failed batch record must roll back new records, replacements and removal markers");
        Directory.Delete(index + ".tmp");
        Check((await library.ScanAsync(removedDirectory)).Count == 0,
            "A failed batch must not revive a removed record during a later scan");
        await library.RecordManyAsync([updated, Completed("added", addedPath, "new"), revived]);
        Check((await new LibraryService(root).GetAllAsync()).Count == 3,
            "A failed batch must remain retryable after storage recovers");
    }

    private static async Task VerifyScanRollbackAsync(string root)
    {
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "original.mp4"), added = Path.Combine(root, "new.mp4");
        await File.WriteAllBytesAsync(original, Mp4);
        await File.WriteAllBytesAsync(added, Mp4);
        var library = new LibraryService(root);
        await library.RecordAsync(Completed("original", original, "metadata to keep"));
        string index = Path.Combine(root, "library.json");
        string before = await File.ReadAllTextAsync(index);
        Directory.CreateDirectory(index + ".tmp");
        bool failed = false;
        try { await library.ScanAsync(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Check(failed && (await library.GetAllAsync()).Single().Title == "metadata to keep" && await File.ReadAllTextAsync(index) == before,
            "A failed scan must preserve both the original index and visible collection");
        Directory.Delete(index + ".tmp");
        await library.ScanAsync(root);
        Check((await new LibraryService(root).GetAllAsync()).Count == 2,
            "An import can be retried without duplicates after a failed index write");
    }

    private static async Task VerifyDownloadAsync(string root, string mode)
    {
        Directory.CreateDirectory(root);
        var library = new LibraryService(root);
        var settings = new AppSettings { DownloadDirectory = Path.Combine(root, "videos") };
        var fixture = new Fixture(mode);
        var service = new LinkDownloadService(settings, library, root)
        {
            ClientFactory = () => new HttpClient(fixture) { Timeout = Timeout.InfiniteTimeSpan }
        };
        var statuses = new List<string>();
        service.TaskUpdated += (_, task) =>
        {
            statuses.Add(task.Status);
            if (mode == "cache-failure" && task.Status == "saving")
                Directory.CreateDirectory(Path.Combine(root, "link-tasks.json.tmp"));
            if (task.Status == "completed")
            {
                var cached = JsonSerializer.Deserialize<List<DownloadTask>>(File.ReadAllText(Path.Combine(root, "link-tasks.json")), AppServices.JsonOptions)!;
                Check(cached.Single(item => item.Id == task.Id).Status == "completed",
                    "Completion must be persisted before the completion event is emitted");
            }
        };
        bool xhs = mode.StartsWith("xhs-", StringComparison.Ordinal);
        service.Start(xhs ? "https://www.xiaohongshu.com/explore/" + XhsId : "https://v.douyin.com/Test123/", xhs);
        await service.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var task = service.GetTasks().Single();
        if (mode == "cache-failure")
        {
            Check(task.Status == "failed" && !statuses.Contains("completed") && task.Error.Contains("任务记录保存失败"),
                "A failed terminal cache write must report its failure rather than publish completion");
            Check((await library.GetAllAsync()).Single().Id == task.Id && (await File.ReadAllBytesAsync(task.FilePath)).SequenceEqual(Mp4),
                "A task-cache failure must preserve the completed library entry and video");
            Directory.Delete(Path.Combine(root, "link-tasks.json.tmp"));
            var restarted = new LinkDownloadService(settings, new LibraryService(root), root);
            await restarted.InitializeAsync();
            var recovered = restarted.GetTasks().Single();
            Check(recovered.Status == "completed" && recovered.Progress == 100 && recovered.Error == "" && recovered.FilePath == task.FilePath,
                "A durable library entry must recover stale task-cache state after restart");
            // A persisted failure from another writer is reconciled by the same authoritative library record.
            await File.WriteAllTextAsync(Path.Combine(root, "link-tasks.json"), JsonSerializer.Serialize(new[] { task }, AppServices.JsonOptions));
            restarted = new LinkDownloadService(settings, new LibraryService(root), root);
            await restarted.InitializeAsync();
            Check(restarted.GetTasks().Single().Status == "completed", "A saved cache-error state must also recover against the completed library record");
        }
        else if (mode is "partial-response" or "xhs-all-failed")
        {
            Check(task.Status == "failed" && !statuses.Contains("completed") && (await library.GetAllAsync()).Count == 0,
                "A partial HTTP206 response or failed CDN candidates must never produce a completed media record");
            Check(!Directory.EnumerateFiles(settings.DownloadDirectory, "*.mp4").Any(), "A rejected HTTP fragment must not be saved as a video");
            var restarted = new LinkDownloadService(settings, new LibraryService(root), root);
            await restarted.InitializeAsync();
            Check(restarted.GetTasks().Single().Status == "failed", "A failed download must stay failed after restart");
            if (xhs) Check(fixture.MediaRequests == 2, "All failed XHS candidates must be attempted within the limit");
        }
        else
        {
            var media = (await new LibraryService(root).GetAllAsync()).Single();
            Check(task.Status == "completed" && task.Quality == "720×1280" && media.Quality == "720×1280" && media.Id == task.Id &&
                fixture.MediaRequests == 2 && (await File.ReadAllBytesAsync(task.FilePath)).SequenceEqual(Mp4),
                "XHS must save the successful fallback stream with its actual quality and persistent library record");
        }
        Check(!Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories).Any(), "Every outcome must clean temporary download files");
    }

    private static DownloadTask Completed(string id, string path, string title) => new()
    {
        Id = id, Status = "completed", FilePath = path, Title = title, UpdatedAt = DateTimeOffset.UtcNow, BytesTotal = Mp4.Length
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(string mode) : HttpMessageHandler
    {
        public int MediaRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string host = request.RequestUri!.Host;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (host == "v.douyin.com")
            {
                response.StatusCode = HttpStatusCode.Found;
                response.Headers.Location = new Uri("https://www.iesdouyin.com/share/video/" + VideoId + "/");
            }
            else if (host == "www.iesdouyin.com")
                response.Content = new StringContent("window._ROUTER_DATA=" + JsonSerializer.Serialize(new
                {
                    loaderData = new Dictionary<string, object> { ["video_(id)/page"] = new { videoInfoRes = new { item_list = new[] {
                        new { aweme_id = VideoId, desc = "fixture", author = new { nickname = "fixture" },
                            video = new { width = 720, height = 1280, play_addr = new { url_list = new[] { "https://v3.douyinvod.com/video.mp4" } } } }
                    } } } }
                }));
            else if (host == "www.xiaohongshu.com")
                response.Content = new StringContent("window.__INITIAL_STATE__=" + JsonSerializer.Serialize(new
                {
                    note = new { noteDetailMap = new Dictionary<string, object> { [XhsId] = new { note = new
                    {
                        noteId = XhsId, type = "video", title = "fixture", user = new { nickname = "fixture" },
                        video = new { consumer = new { originVideoKey = "bad.mp4" }, media = new { stream = new { h264 = new[] {
                            new { width = 720, height = 1280, masterUrl = "https://sns-video-v6.xhscdn.com/good.mp4" }
                        } } } }
                    } } } }
                }));
            else
            {
                MediaRequests++;
                if (host == "sns-video-bd.xhscdn.com" || mode == "xhs-all-failed") response.StatusCode = HttpStatusCode.Forbidden;
                else
                {
                    response.Content = new ByteArrayContent(Mp4);
                    if (mode == "partial-response")
                    {
                        response.StatusCode = HttpStatusCode.PartialContent;
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, Mp4.Length - 1, Mp4.Length * 2);
                    }
                }
            }
            return Task.FromResult(response);
        }
    }
}
