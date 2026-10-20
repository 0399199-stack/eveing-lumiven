using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace Lumiven;

public sealed partial class MainWindow
{
    private bool _xiaohongshu;
    private string _linkErrorSource = "";
    private bool _clearingLinkRecords;
    private readonly HashSet<string> _clearedLinkTaskIds = [];

    private void Platform_Click(object sender, RoutedEventArgs e)
    {
        _xiaohongshu = ((Button)sender).Tag as string == "xhs";
        LinkInputError.Visibility = Visibility.Collapsed;
        UpdatePlatformSelection();
    }

    private void UpdatePlatformSelection()
    {
        foreach (var (button, active) in new[] { (DouyinPlatformButton, !_xiaohongshu), (XhsPlatformButton, _xiaohongshu) })
        {
            button.Background = ThemeBrush(active ? "SelectionBrush" : "GlassBrush");
            button.BorderBrush = ThemeBrush(active ? "AccentBrush" : "GlassEdgeBrush");
            AutomationProperties.SetHelpText(button, Localizer.T(active ? "已选择" : "点击选择此平台"));
        }
        LinkInputTitle.Text = Localizer.T(_xiaohongshu ? "小红书视频分享链接" : "抖音分享短链接");
        LinkInput.PlaceholderText = Localizer.T(_xiaohongshu ? "粘贴小红书视频链接或整段分享文案" : "粘贴 https://v.douyin.com/… 或整段分享文案");
    }

    private void SetLinkError(string message)
    {
        _linkErrorSource = message;
        LinkInputError.Text = Localizer.TranslateExisting(message);
        LinkInputError.Visibility = Visibility.Visible;
    }

    private void RefreshLinkLocalization()
    {
        UpdatePlatformSelection();
        LinkInputError.Text = Localizer.TranslateExisting(_linkErrorSource);
    }

    private async void PasteLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text)) LinkInput.Text = await content.GetTextAsync();
            else SetLinkError("剪贴板中没有文字，请先复制视频分享链接。");
        }
        catch { SetLinkError("暂时无法读取剪贴板，请在输入框中手动粘贴。"); }
    }

    private void LinkDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_exiting || _performanceTest || _repairing) return;
        try
        {
            AppServices.Links.Start(LinkInput.Text, _xiaohongshu);
            LinkInputError.Visibility = Visibility.Collapsed;
            LinkInput.Text = "";
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            SetLinkError(ex.Message);
        }
    }

    private void CancelLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) AppServices.Links.Cancel(id);
    }

    private void UpdateClearLinkRecordsButton()
    {
        if (ClearLinkRecordsButton is null) return;
        ClearLinkRecordsButton.IsEnabled = _initialized && !_exiting && !_performanceTest && !_clearingLinkRecords &&
            AppServices.Links.GetTasks().Any(task => task.Status is "completed" or "failed" or "cancelled" or "interrupted");
    }

    private async void ClearLinkRecords_Click(object sender, RoutedEventArgs e)
    {
        if (_exiting || _performanceTest || _clearingLinkRecords) return;
        _clearingLinkRecords = true;
        UpdateClearLinkRecordsButton();
        bool recordsCleared = false;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Language = Localizer.CurrentLanguage,
                Title = Localizer.T("清除已结束的解析记录？"),
                Content = new TextBlock
                {
                    Text = Localizer.T("仅移除视频解析中的已完成、失败、已取消和中断的任务记录。下载的视频文件和“已下载”媒体库会保留，正在进行的解析与下载不受影响。"),
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 440
                },
                PrimaryButtonText = Localizer.T("清除记录"), CloseButtonText = Localizer.T("取消"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var clearedIds = await AppServices.Links.ClearFinishedTasksAsync();
            if (clearedIds.Count == 0)
            {
                ShowNotice(Localizer.T("暂无可清除的记录"), Localizer.T("当前没有可清除的已结束任务。若任务刚刚结束，请稍后重试。"), InfoBarSeverity.Informational);
                return;
            }
            foreach (string id in clearedIds)
            {
                _clearedLinkTaskIds.Add(id);
                _pendingTasks.Remove(id);
                _completedIds.Remove(id);
            }
            var cleared = clearedIds.ToHashSet(StringComparer.Ordinal);
            foreach (var task in LinkTasks.Where(task => cleared.Contains(task.Id)).ToArray()) LinkTasks.Remove(task);
            UpdateQueueCount();
            recordsCleared = true;
            ShowNotice(Localizer.T("解析记录已清除"), Localizer.T("已清除 {0} 条记录。下载的视频文件和媒体库已保留。", clearedIds.Count), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppServices.Log("清除解析记录", ex);
            ShowNotice(Localizer.T("未能清除记录"), Localizer.T("记录未作更改，请稍后重试。 {0}", AppServices.UserError(ex)), InfoBarSeverity.Error);
        }
        finally
        {
            _clearingLinkRecords = false;
            UpdateClearLinkRecordsButton();
        }
        if (recordsCleared) await GuardAsync(ReloadLibraryAsync);
    }
}
