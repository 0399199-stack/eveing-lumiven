using Lumiven.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Globalization;

namespace Lumiven;

public sealed partial class MainWindow
{
    private string? _pendingLanguage;
    private bool _settingLanguage;

    private void InitializeLanguageControls()
    {
        _settingLanguage = true;
        LanguageCombo.SelectedIndex = Localizer.CurrentLanguage == "en-US" ? 1 : 0;
        _settingLanguage = false;
        Root.Language = Localizer.CurrentLanguage;
        ApplicationLanguages.PrimaryLanguageOverride = Localizer.CurrentLanguage;
        LanguageCombo.DropDownClosed += (_, _) => DispatcherQueue.TryEnqueue(async () => await ApplyPendingLanguageAsync());
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _settingLanguage || LanguageCombo.SelectedItem is not ComboBoxItem item) return;
        _pendingLanguage = (string)item.Tag;
        if (!LanguageCombo.IsDropDownOpen) DispatcherQueue.TryEnqueue(async () => await ApplyPendingLanguageAsync());
    }

    private async Task ApplyPendingLanguageAsync()
    {
        if (_pendingLanguage is not { } language || LanguageCombo.IsDropDownOpen) return;
        _pendingLanguage = null;
        if (language == Localizer.CurrentLanguage) return;
        AppServices.Settings.Language = language;
        ApplicationLanguages.PrimaryLanguageOverride = language;
        Root.Language = language;
        Localizer.SetLanguage(language);
        RefreshLanguage();
        if (!_performanceTest) await GuardAsync(AppServices.SaveSettingsAsync);
    }
}
