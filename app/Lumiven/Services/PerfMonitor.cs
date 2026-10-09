using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Lumiven.Services;

public sealed class PerfMonitor
{
    private readonly Stopwatch _clock = new();
    private readonly List<double> _renderIntervals = [];
    private readonly List<double> _dispatchIntervals = [];
    private DispatcherQueueTimer? _timer;
    private double _lastFrame, _lastDispatch;
    private string _scenario = "";

    public void Start(DispatcherQueue queue, string scenario)
    {
        _scenario = scenario;
        _clock.Restart();
        _lastFrame = _lastDispatch = 0;
        _renderIntervals.Clear(); _dispatchIntervals.Clear();
        CompositionTarget.Rendering += OnRendering;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnRendering(object? sender, object args)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_lastFrame > 2000) _renderIntervals.Add(now - _lastFrame);
        _lastFrame = now;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_lastDispatch > 2000) _dispatchIntervals.Add(now - _lastDispatch);
        _lastDispatch = now;
    }

    public async Task<string> StopAsync()
    {
        CompositionTarget.Rendering -= OnRendering;
        _timer?.Stop();
        _clock.Stop();
        static object Summarize(List<double> samples)
        {
            var sorted = samples.Order().ToArray();
            return new
            {
                samples = sorted.Length,
                medianMs = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2],
                p95Ms = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .95))],
                maximumMs = sorted.Length == 0 ? 0 : sorted[^1],
                stallsOver100Ms = sorted.Count(x => x > 100)
            };
        }
        var directory = Path.Combine(AppServices.DataDirectory, "performance");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"perf-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await AtomicJson.WriteAsync(path, new
        {
            scenario = _scenario, recordedAt = DateTimeOffset.Now, durationSeconds = _clock.Elapsed.TotalSeconds,
            os = Environment.OSVersion.VersionString, processors = Environment.ProcessorCount,
            renderCallbackIntervals = Summarize(_renderIntervals), uiDispatcherIntervals = Summarize(_dispatchIntervals),
            note = "Render callbacks and UI dispatcher intervals; these are responsiveness measurements, not GPU presented-frame measurements."
        }, AppServices.JsonOptions);
        return path;
    }
}
