using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;

namespace Lumiven.Controls;

public sealed class SmoothProgressBar : ProgressBar
{
    private Storyboard? _animation;
    public static readonly DependencyProperty TargetValueProperty = DependencyProperty.Register(
        nameof(TargetValue), typeof(double), typeof(SmoothProgressBar), new PropertyMetadata(0d, OnTargetChanged));
    public double TargetValue { get => (double)GetValue(TargetValueProperty); set => SetValue(TargetValueProperty, value); }
    public static readonly DependencyProperty TotalBytesProperty = DependencyProperty.Register(
        nameof(TotalBytes), typeof(long), typeof(SmoothProgressBar), new PropertyMetadata(0L, OnStageChanged));
    public static readonly DependencyProperty TaskStatusProperty = DependencyProperty.Register(
        nameof(TaskStatus), typeof(string), typeof(SmoothProgressBar), new PropertyMetadata("queued", OnStageChanged));
    public long TotalBytes { get => (long)GetValue(TotalBytesProperty); set => SetValue(TotalBytesProperty, value); }
    public string TaskStatus { get => (string)GetValue(TaskStatusProperty); set => SetValue(TaskStatusProperty, value); }

    public SmoothProgressBar()
    {
        Loaded += (_, _) => Resume();
        Unloaded += (_, _) => Suspend();
    }

    public void Suspend()
    {
        _animation?.Stop(); _animation = null;
        Value = TargetValue; IsIndeterminate = false;
    }

    public void Resume()
    {
        _animation?.Stop(); _animation = null;
        Value = TargetValue; UpdateStage();
    }

    private bool IsShown()
    {
        for (DependencyObject? ancestor = this; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private void UpdateStage() => IsIndeterminate = IsLoaded && Motion.Allowed && IsShown() && TotalBytes <= 0 && TaskStatus is "downloading" or "uploading" or "processing" or "saving";

    private static void OnStageChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((SmoothProgressBar)sender).UpdateStage();
    }

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var bar = (SmoothProgressBar)sender;
        var from = bar.Value;
        bar._animation?.Stop();
        double target = Math.Clamp((double)args.NewValue, bar.Minimum, bar.Maximum);
        bar.Value = target;
        bar.UpdateStage();
        if (!Motion.Allowed || !bar.IsLoaded || !bar.IsShown()) return;
        var change = new DoubleAnimation
        {
            From = from, To = target, Duration = TimeSpan.FromMilliseconds(220), EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(change, bar); Storyboard.SetTargetProperty(change, "Value");
        bar._animation = new Storyboard(); bar._animation.Children.Add(change); bar._animation.Begin();
    }
}
