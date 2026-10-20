using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lumiven;

public sealed partial class MainWindow
{
    private bool _startupReady;
    private bool _startupContinuing;
    private bool _startupComplete;

    private async Task ContinueStartupAsync()
    {
        if (!_startupReady || _startupComplete || _startupContinuing || _performanceTest || _exiting ||
            _busy || _stopping || AppServices.Engine.StatusState == "recovery_required") return;
        _startupContinuing = true;
        try
        {
            // Recovery may finish while a help dialog or menu is still open.
            while (VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).FirstOrDefault(popup => popup.IsOpen) is { } popup)
            {
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnClosed(object? sender, object args) => closed.TrySetResult();
                popup.Closed += OnClosed;
                try { if (popup.IsOpen) await closed.Task.WaitAsync(_lifetime.Token); }
                finally { popup.Closed -= OnClosed; }
            }
            if (_exiting || _busy || _stopping || AppServices.Engine.StatusState == "recovery_required") return;
            if (!AppServices.Settings.SuppressStartupNotice)
            {
                var remember = new CheckBox { Content = Localizer.T("不再显示此提示") };
                var paragraphs = new StackPanel { Spacing = 12, MaxWidth = 460 };
                foreach (string text in new[]
                {
                    "请仅下载您拥有版权或已获授权的视频，遵守法律法规及平台规则，尊重著作权与个人隐私。",
                    "本软件由 EVEING 开发维护，免费提供、源代码公开。如您通过任何渠道付费购买，请立即向销售方或交易平台申请退款。",
                    "请通过官方 GitHub 免费获取软件及更新。使用中如遇问题，请联系 0399199@gmail.com，附上软件版本、问题说明或截图。"
                }) paragraphs.Children.Add(new TextBlock
                {
                    Text = Localizer.T(text), FontSize = 13, LineHeight = 21,
                    TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
                });
                paragraphs.Children.Add(new HyperlinkButton
                {
                    Content = Localizer.T("官方 GitHub"),
                    NavigateUri = new Uri("https://github.com/0399199-stack/eveing-lumiven"),
                    HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 4, 0, 4)
                });
                var content = new StackPanel { Spacing = 18 };
                content.Children.Add(new ScrollViewer
                {
                    Content = paragraphs, MaxHeight = Math.Max(140, Math.Min(430, Root.ActualHeight - 280)),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                });
                content.Children.Add(remember);
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Language = Localizer.CurrentLanguage,
                    Title = new TextBlock { Text = Localizer.T("使用须知与免费声明"), FontSize = 20, TextWrapping = TextWrapping.Wrap },
                    Content = content, PrimaryButtonText = Localizer.T("我已阅读，开始使用"),
                    CloseButtonText = Localizer.T("退出软件"), DefaultButton = ContentDialogButton.Primary
                };
                var result = await dialog.ShowAsync();
                if (_exiting || _lifetime.IsCancellationRequested) return;
                if (result != ContentDialogResult.Primary)
                {
                    await ExitAsync();
                    return;
                }
                if (remember.IsChecked == true)
                {
                    await GuardAsync(async () =>
                    {
                        AppServices.Settings.SuppressStartupNotice = true;
                        try { await AppServices.SaveSettingsAsync(); }
                        catch { AppServices.Settings.SuppressStartupNotice = false; throw; }
                    });
                }
            }
            if (_exiting || _lifetime.IsCancellationRequested) return;
            _startupComplete = true;
            _ = CheckUpdatesAtStartupAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _startupContinuing = false; }
    }
}
