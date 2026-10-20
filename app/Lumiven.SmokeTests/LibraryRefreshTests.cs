using System.Text.Json;
using Lumiven.Services;

internal static class LibraryRefreshTests
{
    public static async Task RunAsync()
    {
        var started = Enumerable.Range(0, 3).Select(_ => Signal()).ToArray();
        var release = Enumerable.Range(0, 3).Select(_ => Signal()).ToArray();
        int sourceVersion = 1, visibleVersion = 0, scans = 0, active = 0, maxActive = 0;
        var scheduler = new LibraryRefreshScheduler(async () =>
        {
            int scan = Interlocked.Increment(ref scans) - 1;
            int current = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, current);
            int snapshot = Volatile.Read(ref sourceVersion);
            started[scan].SetResult();
            await release[scan].Task;
            Volatile.Write(ref visibleVersion, snapshot);
            Interlocked.Decrement(ref active);
        });

        var first = scheduler.RequestAsync();
        await started[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A download completes after the existing scan has already read its snapshot.
        Volatile.Write(ref sourceVersion, 2);
        var requests = Enumerable.Range(0, 100).Select(_ => scheduler.RequestAsync()).ToArray();
        Check(requests.All(task => ReferenceEquals(task, first)), "Refresh bursts must share one operation");
        release[0].SetResult();
        await started[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(scans == 2 && !first.IsCompleted, "A completed download must trigger one follow-up scan before refresh completes");

        // Another completion while the follow-up scan is running must also be retained.
        Volatile.Write(ref sourceVersion, 3);
        var latest = scheduler.RequestAsync();
        release[1].SetResult();
        await started[2].Task.WaitAsync(TimeSpan.FromSeconds(5));
        release[2].SetResult();
        await Task.WhenAll(requests.Append(first).Append(latest)).WaitAsync(TimeSpan.FromSeconds(5));
        Check(scans == 3 && maxActive == 1 && visibleVersion == 3,
            "Only serial scans may run, and the newest completion must reach the visible library");

        // A refresh that waits behind media deletion must not silently disappear.
        using var libraryGate = new SemaphoreSlim(0, 1);
        int blockedScans = 0;
        var blocked = new LibraryRefreshScheduler(async () =>
        {
            await libraryGate.WaitAsync();
            try { blockedScans++; }
            finally { libraryGate.Release(); }
        });
        var waiting = blocked.RequestAsync();
        Check(!waiting.IsCompleted, "Refresh must wait for an in-progress media operation");
        libraryGate.Release();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Check(blockedScans == 1, "Refresh must run after media deletion releases the library");

        int attempts = 0;
        var failureGate = Signal();
        var retryable = new LibraryRefreshScheduler(async () =>
        {
            if (++attempts == 1) { await failureGate.Task; throw new IOException("fixture scan failure"); }
        });
        var failed = retryable.RequestAsync();
        var sharedFailure = retryable.RequestAsync();
        failureGate.SetResult();
        foreach (var task in new[] { failed, sharedFailure })
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("A failed scan was reported as successful"); }
            catch (IOException ex) when (ex.Message == "fixture scan failure") { }
        }
        await retryable.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(attempts == 2, "A failed scan must permit a later retry");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int cancelledAttempts = 0;
        var cancellable = new LibraryRefreshScheduler(() => ++cancelledAttempts == 1
            ? Task.FromCanceled(cancellation.Token) : Task.CompletedTask);
        var cancelled = cancellable.RequestAsync();
        try { await cancelled; throw new Exception("Cancelled scan was reported as successful"); }
        catch (OperationCanceledException) { }
        Check(cancelled.IsCanceled, "Cancellation must stay cancellation");
        await cancellable.RequestAsync();
        await cancellable.RequestAsync();
        Check(cancelledAttempts == 3, "Cancellation and synchronous completion must not leave the scheduler stuck");

        Console.WriteLine(JsonSerializer.Serialize(new { libraryRefresh = true, completionDuringScan = true,
            burstCoalesced = true, completionDuringFollowUp = true, deletionWaitPreserved = true,
            failurePropagatedAndRetryable = true, cancellationPreserved = true, systemChanges = false }));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
