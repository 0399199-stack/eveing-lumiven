using System.Text.Json;
using Lumiven.Models;
using Lumiven.Services;

internal static class EngineRepairTests
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "lumiven-repair-test-" + Guid.NewGuid().ToString("N"));
        var engine = new EngineClient(new AppSettings(), new LibraryService(root), root);
        Check(!engine.HasPendingRecovery, "A new client must not invent a recovery requirement");
        Check(await engine.RepairAsync() == EngineRepairResult.NoActionNeeded,
            "An already-clean client must report no pending recovery instead of claiming a repair");
        Check(!Directory.Exists(root) && !engine.IsRunning && engine.StatusState == "disabled",
            "A clean diagnostic must not create state, start an engine or enable interception");

        // Only a synthetic file is used: a pending lease must never be sent to the real recovery executable here.
        string core = Path.Combine(root, "core");
        Directory.CreateDirectory(core);
        string lease = Path.Combine(core, "system-lease.json");
        await File.WriteAllTextAsync(lease, "synthetic lease; do not recover");
        await engine.InitializeAsync();
        Check(engine.HasPendingRecovery && engine.StatusState == "recovery_required",
            "An owned lease must be visible to diagnostics even without a live engine");
        File.Delete(lease);
        Check(await engine.RepairAsync() == EngineRepairResult.NoActionNeeded && !engine.HasPendingRecovery,
            "A stale recovery indicator must clear without claiming work after the lease is already gone");
        Check(!File.Exists(Path.Combine(root, "task-cache.json")),
            "A diagnostic with no pending recovery must not rewrite download records");

        var invalid = new EngineClient(new AppSettings(), new LibraryService(root), root + '\0');
        bool rejected = false;
        try { await invalid.RepairAsync(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "An invalid lease path must fail rather than masquerade as no recovery required");
        Check(!invalid.IsRunning, "A failed diagnostic must not enable the engine");
        await VerifyInterruptedTasksAsync(Path.Combine(root, "task-state"));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            engineRepairDiagnostics = true, noActionResult = true, pendingLeaseDetection = true,
            staleIndicatorCleared = true, invalidPathRejected = true, interruptedTasksPersisted = true,
            cancelledSessionUpdatesRejected = true, failedPersistenceRetried = true, systemChanges = false
        }));
    }

    private static async Task VerifyInterruptedTasksAsync(string root)
    {
        Directory.CreateDirectory(root);
        string video = Path.Combine(root, "retained-video.mp4");
        await File.WriteAllBytesAsync(video, [1, 2, 3]);
        var library = new LibraryService(root);
        var engine = new EngineClient(new AppSettings(), library, root);
        using var oldSession = new CancellationTokenSource();
        string[] unfinished = ["queued", "pending", "downloading", "uploading", "processing", "saving"];
        foreach (string status in unfinished)
            engine.ApplyTaskUpdate(new DownloadTask { Id = status, Status = status, Speed = 99, BytesReceived = 20, BytesTotal = 40 }, oldSession.Token);
        foreach (string status in new[] { "completed", "failed", "cancelled", "interrupted" })
            engine.ApplyTaskUpdate(new DownloadTask { Id = status, Status = status, Error = "original", FilePath = status == "completed" ? video : "" }, oldSession.Token);
        await library.RecordAsync((await engine.GetTasksAsync()).Single(task => task.Id == "completed"));
        var events = new List<(string Id, string Status)>();
        engine.TaskUpdated += (_, task) => events.Add((task.Id, task.Status));
        oldSession.Cancel();
        Check(await engine.RepairAsync() == EngineRepairResult.NoActionNeeded,
            "Reconciling stale tasks must not claim proxy recovery when the engine and lease are already gone");
        var tasks = (await engine.GetTasksAsync()).ToDictionary(task => task.Id);
        Check(unfinished.All(id => tasks[id].Status == "interrupted" && tasks[id].Speed == 0 && tasks[id].Progress < 100 && tasks[id].Error.Length > 0),
            "Every unfinished task must stop showing active progress after confirmed engine exit");
        Check(new[] { "completed", "failed", "cancelled", "interrupted" }.All(id => tasks[id].Status == id && tasks[id].Error == "original"),
            "Recovery must preserve terminal results and diagnostics");
        Check(events.Count == unfinished.Length && events.All(item => item.Status == "interrupted"),
            "Each interrupted task must publish its terminal update exactly once");
        Check(!engine.ApplyTaskUpdate(new DownloadTask { Id = "downloading", Status = "downloading" }, oldSession.Token) &&
            !engine.ApplyTaskUpdate(new DownloadTask { Id = "processing", Status = "processing" }, oldSession.Token, skipUnchanged: true),
            "Late socket and snapshot updates from a cancelled session must be rejected");
        Check(events.Count == unfinished.Length && (await engine.GetTasksAsync()).Single(task => task.Id == "downloading").Status == "interrupted",
            "A late update must neither revive a task nor emit a misleading event");
        var cached = JsonSerializer.Deserialize<List<DownloadTask>>(await File.ReadAllTextAsync(Path.Combine(root, "task-cache.json")), AppServices.JsonOptions)!;
        Check(unfinished.All(id => cached.Single(task => task.Id == id).Status == "interrupted"),
            "Interrupted task state must be persisted before repair completes");
        Check((await File.ReadAllBytesAsync(video)).SequenceEqual(new byte[] { 1, 2, 3 }) && (await library.GetAllAsync()).Count == 1,
            "Recovery must preserve completed videos and library records");
        Check(engine.ApplyTaskUpdate(new DownloadTask { Id = "downloading", Status = "failed", Error = "fresh session" }, CancellationToken.None),
            "A fresh session must still be able to reconcile a previously interrupted task");

        string retryRoot = Path.Combine(root, "retry");
        string blockedWrite = Path.Combine(retryRoot, "task-cache.json.tmp");
        Directory.CreateDirectory(blockedWrite);
        var retry = new EngineClient(new AppSettings(), new LibraryService(retryRoot), retryRoot);
        retry.ApplyTaskUpdate(new DownloadTask { Id = "retry", Status = "downloading" }, CancellationToken.None);
        bool failed = false;
        try { await retry.RepairAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Check(failed, "A failed task-cache write must not report successful repair completion");
        Directory.Delete(blockedWrite);
        await retry.RepairAsync();
        Check((await File.ReadAllTextAsync(Path.Combine(retryRoot, "task-cache.json"))).Contains("interrupted"),
            "A subsequent repair must retry the failed interrupted-state write");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
