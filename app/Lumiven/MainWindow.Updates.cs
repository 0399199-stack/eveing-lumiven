using Lumiven.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lumiven;

public sealed partial class MainWindow
{
    private readonly UpdateService _updates = new(AppServices.DataDirectory);
    private CancellationTokenSource? _updateCancellation;
    private AppUpdate? _availableUpdate;
    private string? _updateInstaller;
    private bool _updateBusy;
    private bool _updateChecked;

    private async Task CheckUpdatesAtStartupAsync()
    {
        try
        {
            await Task.Delay(2500, _lifetime.Token);
            if (!_updateChecked && AppServices.Settings.AutomaticUpdates && !_exiting)
                await CheckForUpdatesAsync(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async void AutomaticUpdates_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _performanceTest || AppServices.Settings.AutomaticUpdates == AutomaticUpdatesToggle.IsOn) return;
        AppServices.Settings.AutomaticUpdates = AutomaticUpdatesToggle.IsOn;
        if (!AutomaticUpdatesToggle.IsOn) _updateCancellation?.Cancel();
        await GuardAsync(AppServices.SaveSettingsAsync);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async void OpenUpdates_Click(object sender, RoutedEventArgs e)
    {
        Navigate_Click(SettingsNav, e);
        await CheckForUpdatesAsync(true);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy || _exiting || _performanceTest) return;
        _updateChecked = true;
        SetUpdateBusy(true);
        _updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _availableUpdate = null;
        _updateInstaller = null;
        UpdateStatusText.Text = "正在连接 GitHub，检查更新…";
        UpdateProgress.Value = 0;
        UpdateProgress.IsIndeterminate = true;
        UpdateReleaseLink.Visibility = Visibility.Collapsed;
        try
        {
            _availableUpdate = await _updates.CheckAsync(UpdateService.CurrentVersion, _updateCancellation.Token);
            if (_availableUpdate is null)
            {
                UpdateStatusText.Text = "当前没有比 V" + UpdateService.CurrentVersion + " 更新的正式版本。";
                if (manual) ShowNotice("检查完成", UpdateStatusText.Text, InfoBarSeverity.Success);
            }
            else
            {
                UpdateReleaseLink.NavigateUri = _availableUpdate.ReleaseUrl;
                UpdateReleaseLink.Visibility = Visibility.Visible;
                UpdateStatusText.Text = $"发现 V{_availableUpdate.Version} · {_availableUpdate.Size / 1024d / 1024d:F1} MB";
                if (AppServices.Settings.AutomaticUpdates) await DownloadUpdateCoreAsync(_updateCancellation.Token);
                else ShowNotice("发现新版本", "前往设置 → 软件更新下载并安装。", InfoBarSeverity.Informational);
            }
        }
        catch (Exception ex) { ReportUpdateFailure(ex, manual); }
        finally { FinishUpdateOperation(); }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _availableUpdate is null || _exiting) return;
        SetUpdateBusy(true);
        _updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try { await DownloadUpdateCoreAsync(_updateCancellation.Token); }
        catch (Exception ex) { ReportUpdateFailure(ex, true); }
        finally { FinishUpdateOperation(); }
    }

    private async Task DownloadUpdateCoreAsync(CancellationToken token)
    {
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        UpdateStatusText.Text = $"正在下载 V{_availableUpdate!.Version}…";
        var progress = new Progress<double>(value =>
        {
            if (_exiting || !_updateBusy || token.IsCancellationRequested) return;
            UpdateProgress.Value = value;
            UpdateStatusText.Text = $"正在下载 V{_availableUpdate.Version} · {value:F0}%";
        });
        _updateInstaller = await _updates.DownloadAsync(_availableUpdate, progress, token);
        UpdateStatusText.Text = $"V{_availableUpdate.Version} 已下载并通过校验，可以安装。";
        ShowNotice("更新已准备好", "前往设置 → 软件更新，点击“安装更新”。", InfoBarSeverity.Success);
    }

    private void CancelUpdate_Click(object sender, RoutedEventArgs e) => _updateCancellation?.Cancel();

    private void SetUpdateBusy(bool busy)
    {
        _updateBusy = busy;
        CheckUpdatesButton.IsEnabled = !busy && !_exiting;
        DownloadUpdateButton.Visibility = !busy && _availableUpdate is not null && _updateInstaller is null ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Visibility = !busy && _updateInstaller is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelUpdateButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FinishUpdateOperation()
    {
        _updateCancellation?.Dispose();
        _updateCancellation = null;
        if (!_exiting) SetUpdateBusy(false);
    }

    private void ReportUpdateFailure(Exception exception, bool notify)
    {
        if (_lifetime.IsCancellationRequested) return;
        UpdateStatusText.Text = exception switch
        {
            OperationCanceledException when _updateCancellation?.IsCancellationRequested == true => "已取消更新，可随时重试。",
            OperationCanceledException => "连接 GitHub 超时，请稍后重试。",
            HttpRequestException => "暂时无法连接 GitHub，可能是网络不可达或请求受限，请稍后重试。",
            InvalidDataException => exception.Message,
            _ => "更新未完成，请检查网络、磁盘空间与文件权限后重试。"
        };
        AppServices.Log("软件更新", exception);
        if (notify) ShowNotice("更新未完成", UpdateStatusText.Text, InfoBarSeverity.Warning);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _updateInstaller is null || _availableUpdate is null || _exiting || _busy || _stopping) return;
        _updateBusy = true;
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = $"安装 V{_availableUpdate.Version}",
                Content = "Lumiven 将先停止微信接入并恢复代理，然后退出并打开安装向导。下载视频、媒体库和设置会保留。请完成安装后重新打开软件。",
                PrimaryButtonText = "继续安装", CloseButtonText = "稍后", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var tasks = await AppServices.Engine.GetTasksAsync();
            if (tasks.Any(t => t.Status is "queued" or "pending" or "downloading" or "processing" or "saving"))
            {
                ShowNotice("请稍后安装", "仍有视频任务未完成，请等待下载结束后再安装更新。", InfoBarSeverity.Warning);
                return;
            }
            await _updates.VerifyInstallerAsync(_availableUpdate, _updateInstaller, _lifetime.Token);
            await ExitAsync(_updateInstaller);
        }
        catch (Exception ex) { ReportUpdateFailure(ex, true); }
        finally
        {
            _updateBusy = false;
            if (!_exiting) { SetUpdateBusy(false); InstallUpdateButton.IsEnabled = true; }
        }
    }
}
