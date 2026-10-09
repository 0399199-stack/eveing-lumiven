using Lumiven.Controls;
using Lumiven.Models;
using Lumiven.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Hosting;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace Lumiven;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<DownloadTask> Tasks { get; } = [];
    public ObservableCollection<MediaItem> FilteredMedia { get; private set; } = [];
    private readonly List<MediaItem> _media = [];
    private readonly Dictionary<string, DownloadTask> _pendingTasks = [];
    private readonly HashSet<string> _completedIds = [];
    private readonly DispatcherTimer _taskTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _performanceTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly PerfMonitor _performance = new();
    private readonly bool _performanceTest = Environment.GetCommandLineArgs().Contains("--performance-test");
    private readonly UISettings _uiSettings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _libraryLock = new(1, 1);
    private readonly IntPtr _hwnd;
    private readonly AppWindow _window;
    private FrameworkElement _page = null!;
    private string _pageName = "downloads";
    private bool _initialized;
    private bool _exiting;
    private bool _busy;
    private MediaItem? _playing;
    private Image? _sourceCover;
    private MediaPlayer? _player;
    private int _performanceStep;
    private bool? _effectsEnabled;
    private string? _pendingTheme;
    private bool _windowSuspended;
    private bool _searchPending;

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _window = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        double scale = GetDpiForWindow(_hwnd) / 96d;
        var workArea = DisplayArea.GetFromWindowId(_window.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(1260 * scale), workArea.Width - 48);
        int height = Math.Min((int)(850 * scale), workArea.Height - 48);
        _window.MoveAndResize(new RectInt32(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Lumiven.ico");
        if (File.Exists(icon)) _window.SetIcon(icon);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        _window.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        _window.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        _window.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        _window.Closing += Window_Closing;
        _window.Changed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            try { UpdateWindowActivity(); }
            catch (Exception ex) { AppServices.Log("窗口状态变化", ex); }
        });
        _page = DownloadsPage;
        _taskTimer.Tick += (_, _) => FlushTaskUpdates();
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); _searchPending = false; FilterLibrary(); };
        Root.KeyDown += Root_KeyDown;
        Root.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateEffects);
        ThemeCombo.DropDownClosed += (_, _) => DispatcherQueue.TryEnqueue(async () => await ApplyPendingThemeAsync());
        _uiSettings.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateEffects);
        AppServices.Engine.StatusChanged += (_, status) => DispatcherQueue.TryEnqueue(() => SetStatus(status));
        AppServices.Engine.TaskUpdated += (_, task) => DispatcherQueue.TryEnqueue(() => _pendingTasks[task.Id] = task);
        AppServices.Tray.Initialize(_hwnd, () => DispatcherQueue.TryEnqueue(ShowWindow), () => DispatcherQueue.TryEnqueue(async () => await ExitAsync()));
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        ApplyTheme(AppServices.Settings.Theme);
        DownloadDirectoryBox.Text = AppServices.Settings.DownloadDirectory;
        ConcurrencySlider.Value = AppServices.Settings.Concurrency;
        ConcurrencyText.Text = $"{AppServices.Settings.Concurrency} 个";
        ThemeCombo.SelectedIndex = AppServices.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        SelectNavigation();
        UpdateEffects();
        _taskTimer.Start();
        if (_performanceTest) { StartPerformanceTest(); return; }
        await GuardAsync(async () =>
        {
            await AppServices.Engine.InitializeAsync();
            SetStatus(AppServices.Engine.StatusText);
            foreach (var task in await AppServices.Engine.GetTasksAsync())
            {
                if (task.Status != "completed") Tasks.Add(task);
                else _completedIds.Add(task.Id);
            }
            UpdateQueueCount();
            await ReloadLibraryAsync();
        });
        Motion.Enter(DownloadsPage);
    }

    private void UpdateEffects()
    {
        bool effects = _uiSettings.AdvancedEffectsEnabled;
        AmbientLayer.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
        Root.Background = ThemeBrush(effects ? "CanvasBrush" : "CanvasSolidBrush");
        if (_effectsEnabled != effects)
        {
            SystemBackdrop = effects && DesktopAcrylicControllerSupported() ? new DesktopAcrylicBackdrop() : null;
            _effectsEnabled = effects;
        }
        foreach (var border in Descendants<Border>(Root))
        {
            if (ReferenceEquals(border.Style, Application.Current.Resources["GlassPanel"]))
                border.Background = ThemeBrush(effects ? "GlassBrush" : "GlassSolidBrush");
        }
        var foreground = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        _window.TitleBar.ButtonForegroundColor = foreground;
        _window.TitleBar.ButtonInactiveForegroundColor = Root.ActualTheme == ElementTheme.Dark ? Colors.Gray : Colors.DarkGray;
        SelectNavigation();
    }

    private static bool DesktopAcrylicControllerSupported() => Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();

    private Brush ThemeBrush(string key)
    {
        var dictionary = Application.Current.Resources.MergedDictionaries.Last();
        var colors = (ResourceDictionary)dictionary.ThemeDictionaries[Root.ActualTheme == ElementTheme.Light ? "Light" : "Default"];
        return (Brush)colors[key];
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        string next = (string)((Button)sender).Tag;
        if (_pageName == next) return;
        foreach (var progress in Descendants<SmoothProgressBar>(_page)) progress.Suspend();
        _page.Visibility = Visibility.Collapsed;
        _pageName = next;
        _page = next switch { "library" => LibraryPage, "settings" => SettingsPage, "about" => AboutPage, _ => DownloadsPage };
        _page.Visibility = Visibility.Visible;
        SelectNavigation();
        foreach (var progress in Descendants<SmoothProgressBar>(_page)) progress.Resume();
        Motion.Enter(_page);
    }

    private void SelectNavigation()
    {
        foreach (var (button, indicator) in new[]
        {
            (DownloadsNav, DownloadsIndicator), (LibraryNav, LibraryIndicator),
            (SettingsNav, SettingsIndicator), (AboutNav, AboutIndicator)
        })
        {
            var active = (string)button.Tag == _pageName;
            button.Background = active ? ThemeBrush("SelectionBrush") : new SolidColorBrush(Colors.Transparent);
            button.BorderBrush = active ? ThemeBrush("SelectionEdgeBrush") : new SolidColorBrush(Colors.Transparent);
            button.BorderThickness = active ? new Thickness(1) : new Thickness(0);
            button.Foreground = ThemeBrush("PrimaryBrush");
            button.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            indicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (_performanceTest) return;
        if (_busy) return;
        var consent = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "启用微信下载", PrimaryButtonText = "同意并启用", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = "为微信视频号注入下载按钮，需要安装本软件根证书，并临时将当前用户系统代理切换到本机服务。\n\n使用期间，其他遵循系统代理的应用流量也会经过本机。停止接入或退出软件时会恢复原代理；本软件不安装网络驱动。\n\n接入仅作用于当前 Windows 用户，无需管理员权限。如 Windows 提示证书安装确认，请确认后继续。启用后请重新打开视频号页面。",
                TextWrapping = TextWrapping.Wrap, LineHeight = 23, MaxWidth = 430
            }
        };
        if (await consent.ShowAsync() != ContentDialogResult.Primary) return;
        _busy = true; EnableButton.IsEnabled = false;
        await GuardAsync(async () => { await AppServices.Engine.EnableAsync(); SetStatus(AppServices.Engine.StatusText); });
        _busy = false; SetStatus(AppServices.Engine.StatusText);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_performanceTest) return;
        if (_busy) return;
        _busy = true; StopButton.IsEnabled = false;
        await GuardAsync(async () => { await AppServices.Engine.StopAsync(); SetStatus(AppServices.Engine.StatusText); });
        _busy = false;
        SetStatus(AppServices.Engine.StatusText);
    }

    private void SetStatus(string status)
    {
        EngineStatusText.Text = status;
        SettingsStatusText.Text = status;
        bool running = AppServices.Engine.IsRunning;
        bool needsRecovery = AppServices.Engine.StatusState == "recovery_required";
        ConnectionLabel.Text = needsRecovery ? "需要恢复接入" : running ? "微信下载已启用" : "尚未启用";
        ConnectionDot.Fill = ThemeBrush(running ? "AccentBrush" : "MutedBrush");
        EnableButton.IsEnabled = !_busy && !running && !needsRecovery;
        StopButton.IsEnabled = !_busy && (running || needsRecovery);
    }

    private void FlushTaskUpdates()
    {
        if (_pendingTasks.Count == 0) return;
        bool completed = false;
        foreach (var update in _pendingTasks.Values)
        {
            var existing = Tasks.FirstOrDefault(t => t.Id == update.Id);
            if (update.Status == "completed")
            {
                if (existing is not null) Tasks.Remove(existing);
                completed |= _completedIds.Add(update.Id);
                continue;
            }
            if (existing is null) Tasks.Insert(0, update);
            else if (!ReferenceEquals(existing, update))
            {
                existing.Title = update.Title; existing.Author = update.Author; existing.Quality = update.Quality;
                existing.Status = update.Status; existing.Progress = update.Progress;
                existing.BytesReceived = update.BytesReceived; existing.BytesTotal = update.BytesTotal;
                existing.Error = update.Error; existing.FilePath = update.FilePath; existing.UpdatedAt = update.UpdatedAt;
            }
        }
        _pendingTasks.Clear();
        UpdateQueueCount();
        if (completed)
        {
            ShowNotice("下载完成", "视频已保存，可在“已下载”中播放或查看。", InfoBarSeverity.Success);
            Motion.Flash(ConnectionDot);
            if (!_performanceTest) _ = GuardAsync(ReloadLibraryAsync);
        }
    }

    private void UpdateQueueCount()
    {
        QueueCount.Text = $"下载任务 · {Tasks.Count}";
        QueueEmpty.Visibility = Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_performanceTest) { FilterLibrary(); return; }
        foreach (var task in await AppServices.Engine.GetTasksAsync()) _pendingTasks[task.Id] = task;
        FlushTaskUpdates();
        await ReloadLibraryAsync();
    });

    private async Task ReloadLibraryAsync()
    {
        if (!await _libraryLock.WaitAsync(0)) return;
        try
        {
            var records = await AppServices.Engine.GetDownloadedAsync();
            var byPath = records.ToDictionary(item => Path.GetFullPath(item.FilePath), StringComparer.OrdinalIgnoreCase);
            var directories = AppServices.Settings.ImportedDirectories.Prepend(AppServices.Settings.DownloadDirectory).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                if (!await Task.Run(() => Directory.Exists(directory))) continue;
                var scanned = await AppServices.Library.ScanAsync(directory, _lifetime.Token);
                foreach (var item in scanned) byPath.TryAdd(Path.GetFullPath(item.FilePath), item);
            }
            await AppServices.Library.RefreshAsync(byPath.Values);
            _media.Clear();
            _media.AddRange(byPath.Values.OrderByDescending(item => item.CompletedAt));
            FilterLibrary();
        }
        finally { _libraryLock.Release(); }
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchPending = true;
        _searchTimer.Stop(); _searchTimer.Start();
    }

    private void FilterLibrary()
    {
        string query = LibrarySearch.Text.Trim();
        var results = _media.Where(item => query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Author.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        // One ItemsSource reset lets the virtualized panel realize only visible cards.
        // Adding 1,000 entries individually fires 1,000 UI collection notifications.
        FilteredMedia = new ObservableCollection<MediaItem>(results);
        MediaGrid.ItemsSource = FilteredMedia;
        LibrarySubtitle.Text = $"{_media.Count} 个视频 · 本地保存，随时回看";
        LibraryEmptyTitle.Text = query.Length > 0 ? "没有找到匹配的视频" : "你的灵感收藏，从这里开始";
        LibraryEmpty.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Import_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_performanceTest) return;
        var folder = await PickFolderAsync();
        if (folder is null) return;
        if (!AppServices.Settings.ImportedDirectories.Contains(folder, StringComparer.OrdinalIgnoreCase)) AppServices.Settings.ImportedDirectories.Add(folder);
        await AppServices.SaveSettingsAsync();
        await ReloadLibraryAsync();
        ShowNotice("目录已导入", "原文件保留在原目录，Lumiven 只记录位置。", InfoBarSeverity.Success);
    });

    private async void ChooseDirectory_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_performanceTest) return;
        if (AppServices.Engine.IsRunning) { ShowNotice("请先停止接入", "更改保存目录前，请停止微信下载。", InfoBarSeverity.Informational); return; }
        var folder = await PickFolderAsync();
        if (folder is null) return;
        string old = AppServices.Settings.DownloadDirectory;
        if (!AppServices.Settings.ImportedDirectories.Contains(old, StringComparer.OrdinalIgnoreCase)) AppServices.Settings.ImportedDirectories.Add(old);
        AppServices.Settings.DownloadDirectory = folder;
        DownloadDirectoryBox.Text = folder;
        await AppServices.SaveSettingsAsync();
        await ReloadLibraryAsync();
    });

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ThemeCombo.SelectedItem is not ComboBoxItem item) return;
        _pendingTheme = (string)item.Tag;
        if (!ThemeCombo.IsDropDownOpen) DispatcherQueue.TryEnqueue(async () => await ApplyPendingThemeAsync());
    }

    private async Task ApplyPendingThemeAsync()
    {
        if (_pendingTheme is not { } theme || ThemeCombo.IsDropDownOpen) return;
        _pendingTheme = null;
        AppServices.Settings.Theme = theme;
        // Do not mutate a live popup tree or replace native backdrop controllers during theme changes.
        ApplyTheme(theme);
        if (!_performanceTest) await GuardAsync(AppServices.SaveSettingsAsync);
    }

    private void ApplyTheme(string theme) => Root.RequestedTheme = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };

    private async void Concurrency_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_initialized) return;
        int concurrency = (int)e.NewValue;
        ConcurrencyText.Text = $"{concurrency} 个";
        AppServices.Settings.Concurrency = concurrency;
        if (!_performanceTest) await GuardAsync(AppServices.SaveSettingsAsync);
    }

    private void TaskList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Phase == 0 && args.ItemContainer.ContentTemplateRoot is FrameworkElement content) Motion.Enter(content, 220, 8);
    }

    private async void MediaGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Phase != 0 || args.Item is not MediaItem item) return;
        if (args.ItemContainer.ContentTemplateRoot is FrameworkElement content) Motion.Enter(content, 220, 8);
        if (!_performanceTest && string.IsNullOrWhiteSpace(item.Thumbnail) && !item.IsMissing)
        {
            // Fast scrolling can recycle hundreds of containers; only request the thumbnail
            // if this item is still realized after a short settle period.
            try { await Task.Delay(120, _lifetime.Token); }
            catch (OperationCanceledException) { return; }
            if (LibraryPage.Visibility != Visibility.Visible || MediaGrid.ContainerFromItem(item) != args.ItemContainer) return;
            await GuardAsync(async () => { var thumbnail = await AppServices.Library.GetThumbnailAsync(item); if (thumbnail is not null) item.Thumbnail = thumbnail; });
        }
    }

    private void MediaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MediaGrid.ItemsPanelRoot is not ItemsWrapGrid panel || e.NewSize.Width <= 0) return;
        int columns = e.NewSize.Width >= 690 ? 3 : e.NewSize.Width >= 440 ? 2 : 1;
        panel.MaximumRowsOrColumns = columns;
        panel.ItemWidth = Math.Floor(e.NewSize.Width / columns);
    }

    private void CoverFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var radius = new System.Numerics.Vector2(13);
        visual.Clip = visual.Compositor.CreateRectangleClip(0, 0, (float)e.NewSize.Width, (float)e.NewSize.Height, radius, radius, radius, radius);
    }

    private async void Media_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MediaItem item) return;
        if (_performanceTest) { ShowNotice("性能测试数据", "此场景只测试界面响应，测试卡片不对应真实视频。", InfoBarSeverity.Informational); return; }
        if (item.IsMissing || !File.Exists(item.FilePath)) { ShowNotice("文件已移走", "请检查文件是否仍在原位置，或重新导入目录。", InfoBarSeverity.Warning); return; }
        await GuardAsync(() => OpenPlayerAsync(item));
    }

    private Task OpenPlayerAsync(MediaItem item)
    {
        _playing = item;
        var container = MediaGrid.ContainerFromItem(item) as GridViewItem;
        _sourceCover = container is null ? null : Descendants<Image>(container).FirstOrDefault();
        if (Motion.Allowed && _sourceCover is not null && _sourceCover.Source is not null)
        {
            ConnectedAnimationService.GetForCurrentView().DefaultDuration = TimeSpan.FromMilliseconds(320);
            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("openVideo", _sourceCover);
            animation.Configuration = new DirectConnectedAnimationConfiguration();
        }
        PlayerTitle.Text = item.Title; PlayerAuthor.Text = item.Author; PlayerError.Text = "";
        PlayerCover.Source = _sourceCover?.Source;
        PlayerCover.Visibility = PlayerCover.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        PlayerOverlay.Visibility = Visibility.Visible;
        Motion.Enter(PlayerOverlay, 240, 0);
        var connected = ConnectedAnimationService.GetForCurrentView().GetAnimation("openVideo");
        connected?.TryStart(PlayerCover);
        _player?.Dispose();
        _player = new MediaPlayer { AutoPlay = true };
        _player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => PlayerCover.Visibility = Visibility.Collapsed);
        _player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() => { PlayerCover.Visibility = Visibility.Collapsed; PlayerError.Text = "Windows 暂不支持此视频的编码。请使用右侧按钮在系统播放器中打开。"; });
        VideoPlayer.SetMediaPlayer(_player);
        _player.Source = MediaSource.CreateFromUri(new Uri(item.FilePath));
        return Task.CompletedTask;
    }

    private void ClosePlayer_Click(object sender, RoutedEventArgs e) => ClosePlayer();

    private void ClosePlayer()
    {
        if (PlayerOverlay.Visibility != Visibility.Visible) return;
        SetFullscreen(false);
        _player?.Pause(); VideoPlayer.SetMediaPlayer(null); _player?.Dispose(); _player = null;
        PlayerOverlay.Visibility = Visibility.Collapsed;
        _playing = null;
        if (_sourceCover is not null) Motion.Enter(_sourceCover, 220, 0);
        _sourceCover = null;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => SetFullscreen(_window.Presenter.Kind != AppWindowPresenterKind.FullScreen);

    private void SetFullscreen(bool fullscreen)
    {
        _window.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        TitleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        PlayerOverlay.Margin = fullscreen ? new Thickness(0) : new Thickness(0, 58, 0, 0);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (_window.Presenter.Kind == AppWindowPresenterKind.FullScreen) SetFullscreen(false);
        else ClosePlayer();
        e.Handled = true;
    }

    private async void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is MediaItem item) await GuardAsync(() => { AppServices.Library.OpenFolder(item); return Task.CompletedTask; });
    }

    private async void PlayerFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_playing is not null) await GuardAsync(() => { AppServices.Library.OpenFolder(_playing); return Task.CompletedTask; });
    }

    private async void ExternalPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_playing is not null) await GuardAsync(() => { AppServices.Library.OpenFile(_playing); return Task.CompletedTask; });
    }

    private void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        if (_performanceTest) { _ = ExitAsync(); return; }
        _player?.Pause();
        _window.Hide();
        _taskTimer.Stop();
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate(); UpdateWindowActivity(); FlushTaskUpdates();
    }

    private void UpdateWindowActivity()
    {
        if (!_initialized || _exiting) return;
        bool suspended = !_window.IsVisible || _window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        if (_windowSuspended == suspended) return;
        _windowSuspended = suspended; Motion.Suspended = suspended;
        if (suspended)
        {
            _taskTimer.Stop(); _searchTimer.Stop(); _performanceTimer.Stop();
            foreach (var element in Descendants<FrameworkElement>(Root))
                if (Motion.GetEnabled(element)) Motion.Reset(element);
            Motion.Reset(_page); Motion.Reset(Notice); Motion.Reset(ConnectionDot);
            foreach (var progress in Descendants<SmoothProgressBar>(Root)) progress.Suspend();
        }
        else
        {
            _taskTimer.Start();
            if (_searchPending) _searchTimer.Start();
            if (_performanceTest && _performanceStep < 240) _performanceTimer.Start();
            foreach (var progress in Descendants<SmoothProgressBar>(Root)) progress.Resume();
            FlushTaskUpdates();
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (!_performanceTest)
        {
            ShowWindow();
            EnableButton.IsEnabled = false; StopButton.IsEnabled = false;
            EngineStatusText.Text = "正在停止接入并恢复代理…";
            try { await AppServices.Engine.StopAsync(); }
            catch (Exception ex)
            {
                _exiting = false;
                ShowNotice("暂时无法退出", $"停止接入失败：{ex.Message}。请先在设置中停止接入后重试。", InfoBarSeverity.Error);
                SetStatus(AppServices.Engine.StatusText);
                return;
            }
        }
        _lifetime.Cancel(); _taskTimer.Stop(); _searchTimer.Stop();
        _performanceTimer.Stop();
        _player?.Dispose(); AppServices.Tray.Dispose();
        Close();
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { AppServices.Log("界面操作", ex); ShowNotice("操作未完成", ex.Message, InfoBarSeverity.Error); }
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        Notice.Title = title; Notice.Message = message; Notice.Severity = severity; Notice.IsOpen = true;
        Motion.Enter(Notice, 220, 12);
    }

    private void StartPerformanceTest()
    {
        Title = "EVEING Lumiven · 性能测试数据";
        _window.Title = Title;
        EnableButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        EngineStatusText.Text = "性能测试数据 · 1000 条历史 + 5 个并行进度任务 · 不下载文件，不修改媒体库";
        for (int i = 0; i < 1000; i++)
            _media.Add(new MediaItem
            {
                Id = $"performance-media-{i}", Title = $"性能测试数据 · 灵感片段 {i + 1:0000}", Author = $"测试作者 {i % 32 + 1}",
                Quality = "1080p", FilePath = Path.Combine(Path.GetTempPath(), $"lumiven-performance-{i}.mp4"),
                SizeBytes = 1024 * 1024 * (20L + i % 80), CompletedAt = DateTimeOffset.Now.AddMinutes(-i)
            });
        FilterLibrary();
        _performance.Start(DispatcherQueue, "Synthetic UI: 1000 media cards, 5 progress tasks, navigation and virtualized scrolling. No network workload.");
        _performanceTimer.Tick += PerformanceTick;
        _performanceTimer.Start();
        ShowNotice("性能测试数据", "60 秒界面压力测试正在运行，结果会保存到本地 performance 目录。", InfoBarSeverity.Informational);
    }

    private async void PerformanceTick(object? sender, object e)
    {
        _performanceStep++;
        for (int i = 0; i < 5; i++)
            _pendingTasks[$"performance-task-{i}"] = new DownloadTask
            {
                Id = $"performance-task-{i}", Title = $"性能测试数据 · 并行下载 {i + 1}", Author = "测试作者", Quality = "1080p",
                Status = _performanceStep >= 220 ? "completed" : "downloading", Progress = Math.Min(100, _performanceStep * 100d / 220),
                BytesTotal = 104857600, BytesReceived = (long)Math.Min(104857600, _performanceStep * 104857600d / 220), UpdatedAt = DateTimeOffset.Now
            };
        if (_performanceStep % 24 == 0)
        {
            var button = ((_performanceStep / 24) % 4) switch { 0 => DownloadsNav, 1 => LibraryNav, 2 => SettingsNav, _ => AboutNav };
            Navigate_Click(button, new RoutedEventArgs());
        }
        if (_pageName == "library" && _performanceStep % 4 == 0)
        {
            var scroll = Descendants<ScrollViewer>(MediaGrid).FirstOrDefault();
            if (scroll is not null && scroll.ScrollableHeight > 0)
                scroll.ChangeView(null, (_performanceStep * 60) % scroll.ScrollableHeight, null, false);
        }
        if (_performanceStep < 240) return;
        _performanceTimer.Stop();
        await GuardAsync(async () =>
        {
            var path = await _performance.StopAsync();
            ShowNotice("性能测试完成", $"测试记录：{path}", InfoBarSeverity.Success);
        });
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
