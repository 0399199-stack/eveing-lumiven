using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lumiven.Controls;

public sealed class LocalizedMediaTransportControls : MediaTransportControls
{
    // Keep the Windows App SDK template and behavior. Only localize its text properties.
    private static readonly string[] PartNames =
    [
        "ProgressSlider", "TimeElapsedElement", "TimeRemainingElement", "PlayPauseButton", "PlayPauseButtonOnLeft",
        "VolumeMuteButton", "AudioMuteButton", "VolumeSlider", "ZoomButton", "CastButton", "StopButton",
        "SkipBackwardButton", "PreviousTrackButton", "RewindButton", "FastForwardButton", "NextTrackButton",
        "SkipForwardButton", "PlaybackRateButton", "RepeatButton", "CCSelectionButton", "AudioTracksSelectionButton"
    ];
    private readonly List<FrameworkElement> _parts = [];
    private readonly List<(DependencyObject Target, DependencyProperty Property, long Token)> _callbacks = [];
    private bool _translating;
    private bool _listening;

    public LocalizedMediaTransportControls()
    {
        DefaultStyleKey = typeof(MediaTransportControls);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    protected override void OnApplyTemplate()
    {
        Detach();
        base.OnApplyTemplate();
        Attach();
    }

    private void Attach()
    {
        if (_listening) return;
        foreach (string name in PartNames)
        {
            if (GetTemplateChild(name) is not FrameworkElement part) continue;
            _parts.Add(part);
            Watch(part, AutomationProperties.NameProperty, part);
            Watch(part, AutomationProperties.HelpTextProperty, part);
            Watch(part, ToolTipService.ToolTipProperty, part);
            if (part is AppBarButton) Watch(part, AppBarButton.LabelProperty, part);
            if (part is AppBarToggleButton) Watch(part, AppBarToggleButton.LabelProperty, part);
        }
        Localizer.LanguageChanged += LanguageChanged;
        _listening = true;
        TranslateParts();
    }

    private void Detach()
    {
        Localizer.LanguageChanged -= LanguageChanged;
        foreach (var (target, property, token) in _callbacks) target.UnregisterPropertyChangedCallback(property, token);
        _callbacks.Clear();
        _parts.Clear();
        _listening = false;
    }

    private void Watch(DependencyObject target, DependencyProperty property, FrameworkElement part)
    {
        if (_callbacks.Any(item => ReferenceEquals(item.Target, target) && item.Property == property)) return;
        long token = target.RegisterPropertyChangedCallback(property, (_, _) => TranslatePart(part));
        _callbacks.Add((target, property, token));
    }

    private void LanguageChanged(object? sender, EventArgs args) => TranslateParts();
    private void TranslateParts()
    {
        Language = Localizer.CurrentLanguage;
        foreach (var part in _parts) TranslatePart(part);
    }

    private void TranslatePart(FrameworkElement part)
    {
        if (_translating) return;
        _translating = true;
        try
        {
            part.Language = Localizer.CurrentLanguage;
            TranslateProperty(part, AutomationProperties.NameProperty);
            TranslateProperty(part, AutomationProperties.HelpTextProperty);
            if (part is AppBarButton) TranslateProperty(part, AppBarButton.LabelProperty);
            if (part is AppBarToggleButton) TranslateProperty(part, AppBarToggleButton.LabelProperty);
            if (ToolTipService.GetToolTip(part) is ToolTip tip)
            {
                Watch(tip, ContentControl.ContentProperty, part);
                TranslateProperty(tip, ContentControl.ContentProperty);
            }
            else TranslateProperty(part, ToolTipService.ToolTipProperty);
        }
        finally { _translating = false; }
    }

    private static void TranslateProperty(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string text || string.IsNullOrEmpty(text)) return;
        string localized = Localizer.TranslateExisting(text);
        if (localized != text) target.SetValue(property, localized);
    }
}
