using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Lumiven.Controls;

public static class Motion
{
    private static readonly UISettings Preferences = new();
    public static bool Suspended { get; set; }
    public static bool Allowed => !Suspended && Preferences.AnimationsEnabled;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEnabledChanged));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element || args.NewValue is not true) return;
        element.PointerEntered += (_, _) => Scale(element, 1.025f, 150);
        element.PointerExited += (_, _) => Scale(element, 1f, 180);
        element.PointerPressed += (_, _) => Scale(element, .96f, 90);
        element.PointerReleased += (_, _) => Scale(element, 1f, 180);
        element.PointerCanceled += (_, _) => Scale(element, 1f, 120);
        element.SizeChanged += (_, _) => ElementCompositionPreview.GetElementVisual(element).CenterPoint =
            new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
    }

    private static void Scale(FrameworkElement element, float value, int duration)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Scale");
        if (!Allowed) { visual.Scale = Vector3.One; return; }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1, new Vector3(value, value, 1),
            visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), new Vector2(.2f, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(duration);
        visual.StartAnimation("Scale", animation);
    }

    public static void Enter(FrameworkElement element, int duration = 240, float distance = 14)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        if (!Allowed) { visual.Opacity = 1; visual.Properties.InsertVector3("Translation", Vector3.Zero); return; }
        var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1); fade.Duration = TimeSpan.FromMilliseconds(duration);
        var move = visual.Compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0, new Vector3(0, distance, 0)); move.InsertKeyFrame(1, Vector3.Zero, easing);
        move.Duration = TimeSpan.FromMilliseconds(duration);
        visual.StartAnimation("Opacity", fade); visual.StartAnimation("Translation", move);
    }

    public static void Flash(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Allowed) return;
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        animation.InsertKeyFrame(0, new Vector3(.8f, .8f, 1));
        animation.InsertKeyFrame(.65f, new Vector3(1.06f, 1.06f, 1));
        animation.InsertKeyFrame(1, Vector3.One);
        animation.Duration = TimeSpan.FromMilliseconds(400);
        visual.StartAnimation("Scale", animation);
    }

    public static void Reset(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Scale"); visual.StopAnimation("Opacity"); visual.StopAnimation("Translation");
        visual.Scale = Vector3.One; visual.Opacity = 1;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }
}
