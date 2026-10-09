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
    private readonly Dictionary<string, List<double>> _uiCallbacks = [];
    private readonly List<SlowCallback> _slowCallbacks = [];
    private DispatcherQueueTimer? _timer;
    private double _lastFrame, _lastDispatch;
    private string _scenario = "";
    private bool _active;

    public void Start(DispatcherQueue queue, string scenario)
    {
        _scenario = scenario;
        _active = true;
        _clock.Restart();
        _lastFrame = _lastDispatch = 0;
        _renderIntervals.Clear(); _dispatchIntervals.Clear();
        _uiCallbacks.Clear(); _slowCallbacks.Clear();
        CompositionTarget.Rendering += OnRendering;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnRendering(object? sender, object args)
    {
        if (!_clock.IsRunning) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_lastFrame > 2000) _renderIntervals.Add(now - _lastFrame);
        _lastFrame = now;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_clock.IsRunning) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_lastDispatch > 2000) _dispatchIntervals.Add(now - _lastDispatch);
        _lastDispatch = now;
    }

    public void Pause()
    {
        if (!_active || !_clock.IsRunning) return;
        _timer?.Stop();
        _clock.Stop();
        _lastFrame = _lastDispatch = 0;
    }

    public void Resume()
    {
        if (!_active || _clock.IsRunning) return;
        _lastFrame = _lastDispatch = 0;
        _clock.Start();
        _timer?.Start();
    }

    public UiCallbackScope Measure(string name) => new(this, name, _clock.IsRunning ? Stopwatch.GetTimestamp() : 0);

    public readonly struct UiCallbackScope(PerfMonitor monitor, string name, long started) : IDisposable
    {
        public void Dispose()
        {
            if (started != 0) monitor.Record(name, started);
        }
    }

    private void Record(string name, long started)
    {
        double duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!_uiCallbacks.TryGetValue(name, out var samples)) _uiCallbacks[name] = samples = [];
        samples.Add(duration);
        if (duration > 100) _slowCallbacks.Add(new SlowCallback(name, _clock.Elapsed.TotalMilliseconds, duration));
    }

    private sealed record SlowCallback(string Name, double AtMs, double DurationMs);

    public async Task<string> StopAsync()
    {
        _active = false;
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
            renderCallbackIntervals = Summarize(_renderIntervals), dispatcherTimerIntervals = Summarize(_dispatchIntervals),
            uiCallbackDurations = _uiCallbacks.ToDictionary(x => x.Key, x => Summarize(x.Value)), slowUiCallbacks = _slowCallbacks,
            note = "Callback durations cover only instrumented UI work. Rendering and dispatcher timer intervals include scheduling delay. Minimized time is excluded; nested callback durations overlap. None measure GPU presented frames or input-to-feedback latency."
        }, AppServices.JsonOptions);
        return path;
    }
}
