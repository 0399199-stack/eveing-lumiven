namespace Lumiven.Services;

// Keep one scan in progress and coalesce requests made during it into a follow-up scan.
internal sealed class LibraryRefreshScheduler(Func<Task> refresh)
{
    private readonly object _gate = new();
    private TaskCompletionSource? _running;
    private bool _pending;

    public Task RequestAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            _pending = true;
            if (_running is not null) return _running.Task;
            completion = _running = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _ = RunAsync(completion);
        return completion.Task;
    }

    private async Task RunAsync(TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                lock (_gate) _pending = false;
                await refresh();
                lock (_gate)
                {
                    if (_pending) continue;
                    _running = null;
                }
                completion.SetResult();
                return;
            }
        }
        catch (OperationCanceledException ex)
        {
            lock (_gate) _running = null;
            completion.SetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            lock (_gate) _running = null;
            completion.SetException(ex);
        }
    }
}
