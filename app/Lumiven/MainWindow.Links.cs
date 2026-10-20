using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace Lumiven;

public sealed partial class MainWindow
{
    private bool _xiaohongshu;

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
            AutomationProperties.SetHelpText(button, active ? "已选择" : "点击选择此平台");
        }
        LinkInputTitle.Text = _xiaohongshu ? "小红书视频分享链接" : "抖音分享短链接";
        LinkInput.PlaceholderText = _xiaohongshu ? "粘贴小红书视频链接或整段分享文案" : "粘贴 https://v.douyin.com/… 或整段分享文案";
    }

    private async void PasteLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text)) LinkInput.Text = await content.GetTextAsync();
            else { LinkInputError.Text = "剪贴板中没有文字，请先复制视频分享链接。"; LinkInputError.Visibility = Visibility.Visible; }
        }
        catch { LinkInputError.Text = "暂时无法读取剪贴板，请在输入框中手动粘贴。"; LinkInputError.Visibility = Visibility.Visible; }
    }

    private void LinkDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_exiting || _performanceTest) return;
        try
        {
            AppServices.Links.Start(LinkInput.Text, _xiaohongshu);
            LinkInputError.Visibility = Visibility.Collapsed;
            LinkInput.Text = "";
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            LinkInputError.Text = ex.Message;
            LinkInputError.Visibility = Visibility.Visible;
        }
    }

    private void CancelLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) AppServices.Links.Cancel(id);
    }
}
