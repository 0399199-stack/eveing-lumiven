using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lumiven;

public sealed partial class MainWindow
{
    private bool _repairing;
    private string _repairStatusSource = "";
    private object[] _repairStatusArguments = [];

    private void UpdateRepairButton()
    {
        if (RepairButton is null) return;
        RepairButton.IsEnabled = _initialized && !_performanceTest && !_exiting && !_busy && !_stopping && !_repairing && !_updateBusy;
    }

    private void SetRepairStatus(string source, params object[] arguments)
    {
        _repairStatusSource = source;
        _repairStatusArguments = arguments;
        RefreshRepairLocalization();
    }

    private void RefreshRepairLocalization()
    {
        if (RepairStatusText is null) return;
        RepairStatusText.Text = Localizer.TranslateExisting(Localizer.T(_repairStatusSource, _repairStatusArguments));
        RepairStatusText.Visibility = _repairStatusSource.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _performanceTest || _exiting || _busy || _stopping || _repairing || _updateBusy) return;
        if (AppServices.Links.HasActiveTasks)
        {
            SetRepairStatus("视频解析中仍有任务正在进行。请等待完成，或在视频解析中手动取消后再修复，避免代理恢复影响这些下载。");
            ShowNotice(Localizer.T("请先处理正在进行的下载"), RepairStatusText.Text, InfoBarSeverity.Warning);
            return;
        }

        _repairing = true;
        LinkDownloadButton.IsEnabled = false;
        UpdateRepairButton();
        bool checkedSuccessfully = false;
        bool stoppingForRepair = false;
        try
        {
            var tasks = await AppServices.Engine.GetTasksAsync();
            int activeCount = tasks.Count(task => task.Status is "queued" or "pending" or "downloading" or "uploading" or "processing" or "saving");
            var content = new StackPanel { Spacing = 12, MaxWidth = 440 };
            content.Children.Add(new TextBlock
            {
                Text = Localizer.T("此操作会停止 Lumiven 的微信接入，恢复本软件接入前记录的代理设置，并清理本软件的临时证书。下载完成的视频和媒体库会保留。"),
                TextWrapping = TextWrapping.Wrap
            });
            if (activeCount > 0) content.Children.Add(new TextBlock
            {
                Text = Localizer.T("检测到 {0} 个视频号任务尚未结束。继续修复会中断这些任务，之后需要重新下载；也可选择等待下载完成。", activeCount),
                TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text = Localizer.T("修复期间请勿开始新的视频号下载。若 Windows 弹出证书删除提示，请核对为 EVEING Lumiven 的证书后确认。"),
                TextWrapping = TextWrapping.Wrap
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Language = Localizer.CurrentLanguage,
                Title = Localizer.T("修复视频号接入"), Content = content,
                PrimaryButtonText = Localizer.T(activeCount > 0 ? "停止下载并修复" : "停止接入并修复"),
                CloseButtonText = Localizer.T(activeCount > 0 ? "等待下载完成" : "取消"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _exiting) return;
            _stopping = true;
            stoppingForRepair = true;
            SetStatus(AppServices.Engine.StatusText);
            RepairProgress.Visibility = Visibility.Visible;
            RepairProgress.IsActive = true;
            SetRepairStatus("正在检查并恢复 Lumiven 接入状态…若出现 Windows 证书确认，请核对后亲自处理。");
            var result = await AppServices.Engine.RepairAsync();
            foreach (var task in await AppServices.Engine.GetTasksAsync()) _pendingTasks[task.Id] = task;
            FlushTaskUpdates();
            checkedSuccessfully = true;
            if (result == EngineRepairResult.Recovered)
            {
                SetRepairStatus("接入已停止，本软件的代理和证书恢复已确认。请关闭视频号窗口后重新打开；如需下载，请重新启用微信下载。");
                ShowNotice(Localizer.T("Lumiven 接入已恢复"), RepairStatusText.Text, InfoBarSeverity.Success);
            }
            else
            {
                SetRepairStatus("未发现 Lumiven 待恢复的接入状态。如果视频号仍异常，请先检查微信或网络状态。");
                ShowNotice(Localizer.T("无需恢复 Lumiven 接入"), RepairStatusText.Text, InfoBarSeverity.Informational);
            }
        }
        catch (Exception ex)
        {
            AppServices.Log("视频号异常修复", ex);
            SetRepairStatus("修复未完成，未确认恢复成功。请查看当前接入状态后重试。 {0}", AppServices.UserError(ex));
            ShowNotice(Localizer.T("视频号修复未完成"), RepairStatusText.Text, InfoBarSeverity.Error);
        }
        finally
        {
            if (stoppingForRepair) _stopping = false;
            _repairing = false;
            LinkDownloadButton.IsEnabled = !_exiting;
            RepairProgress.IsActive = false;
            RepairProgress.Visibility = Visibility.Collapsed;
            SetStatus(AppServices.Engine.StatusText);
            UpdateRepairButton();
        }
        if (checkedSuccessfully && !_exiting) await GuardAsync(ContinueStartupAsync);
    }
}
