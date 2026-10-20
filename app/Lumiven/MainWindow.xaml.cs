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
    public string DisplayVersion => "V" + UpdateService.CurrentVersion.ToString(UpdateService.CurrentVersion.Build == 0 ? 2 : 3) + "  ·  免费开源";
    public ObservableCollection<DownloadTask> Tasks { get; } = [];
    public ObservableCollection<DownloadTask> LinkTasks { get; } = [];
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
    private bool _stopping;
    private MediaItem? _playing;
    private Image? _sourceCover;
    private MediaPlayer? _player;
    private int _performanceStep;
    private bool? _effectsEnabled;
    private string? _pendingTheme;
    private bool _windowSuspended;
    private bool _searchPending;
    private bool _deletingMedia;
    private bool _selectingMedia;

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
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBar);
            _window.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            _window.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _window.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
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
        AppServices.Links.TaskUpdated += (_, task) => DispatcherQueue.TryEnqueue(() => _pendingTasks[task.Id] = task);
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
        AutomaticUpdatesToggle.IsOn = AppServices.Settings.AutomaticUpdates;
        SelectNavigation();
        UpdateEffects();
        _taskTimer.Start();
        if (_performanceTest) { StartPerformanceTest(); return; }
        await GuardAsync(async () =>
        {
            await AppServices.Engine.InitializeAsync();
            await AppServices.Links.InitializeAsync();
            SetStatus(AppServices.Engine.StatusText);
            foreach (var task in (await AppServices.Engine.GetTasksAsync()).Concat(AppServices.Links.GetTasks()).OrderByDescending(t => t.CreatedAt))
            {
                if (task.Status != "completed") (LinkDownloadService.IsLinkTask(task.Id) ? LinkTasks : Tasks).Add(task);
                else _completedIds.Add(task.Id);
            }
            UpdateQueueCount();
            await ReloadLibraryAsync();
        });
        Motion.Enter(DownloadsPage);
        _ = CheckUpdatesAtStartupAsync();
    }

    private void UpdateEffects()
    {
        bool effects = _uiSettings.AdvancedEffectsEnabled && DesktopAcrylicControllerSupported();
        AmbientLayer.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
        Root.Background = ThemeBrush(effects ? "CanvasBrush" : "CanvasSolidBrush");
        if (_effectsEnabled != effects)
        {
            SystemBackdrop = effects ? new DesktopAcrylicBackdrop() : null;
            _effectsEnabled = effects;
        }
        foreach (var border in Descendants<Border>(Root))
        {
            if (ReferenceEquals(border.Style, Application.Current.Resources["GlassPanel"]))
                border.Background = ThemeBrush(effects ? "GlassBrush" : "GlassSolidBrush");
        }
        var foreground = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _window.TitleBar.ButtonForegroundColor = foreground;
            _window.TitleBar.ButtonInactiveForegroundColor = Root.ActualTheme == ElementTheme.Dark ? Colors.Gray : Colors.DarkGray;
        }
        SelectNavigation();
        UpdatePlatformSelection();
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
        using var measurement = _performance.Measure("Navigate");
        string next = (string)((Button)sender).Tag;
        if (_pageName == next) return;
        foreach (var progress in Descendants<SmoothProgressBar>(_page)) progress.Suspend();
        _page.Visibility = Visibility.Collapsed;
        _pageName = next;
        _page = next switch { "parsing" => ParsingPage, "library" => LibraryPage, "settings" => SettingsPage, "about" => AboutPage, _ => DownloadsPage };
        _page.Visibility = Visibility.Visible;
        SelectNavigation();
        foreach (var progress in Descendants<SmoothProgressBar>(_page)) progress.Resume();
        Motion.Enter(_page);
    }

    private void SelectNavigation()
    {
        foreach (var (button, indicator) in new[]
        {
            (DownloadsNav, DownloadsIndicator), (ParsingNav, ParsingIndicator), (LibraryNav, LibraryIndicator),
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
        if (_stopping) return;
        _stopping = true; StopButton.IsEnabled = false;
        await GuardAsync(async () => { await AppServices.Engine.StopAsync(); SetStatus(AppServices.Engine.StatusText); });
        _stopping = false;
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
        EnableButton.IsEnabled = !_exiting && !_busy && !_stopping && !running && !needsRecovery;
        StopButton.IsEnabled = !_exiting && !_stopping && (running || needsRecovery || AppServices.Engine.StatusState == "starting");
    }

    private void FlushTaskUpdates()
    {
        using var measurement = _performance.Measure("FlushTaskUpdates");
        if (_pendingTasks.Count == 0) return;
        bool completed = false;
        foreach (var update in _pendingTasks.Values)
        {
            var targetTasks = LinkDownloadService.IsLinkTask(update.Id) ? LinkTasks : Tasks;
            var existing = targetTasks.FirstOrDefault(t => t.Id == update.Id);
            if (update.Status == "completed")
            {
                if (existing is not null) targetTasks.Remove(existing);
                completed |= _completedIds.Add(update.Id);
                continue;
            }
            if (existing is null) targetTasks.Insert(0, update);
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
        LinkQueueCount.Text = $"解析下载任务 · {LinkTasks.Count}";
        LinkQueueEmpty.Visibility = LinkTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_performanceTest) { FilterLibrary(); return; }
        foreach (var task in await AppServices.Engine.GetTasksAsync()) _pendingTasks[task.Id] = task;
        foreach (var task in AppServices.Links.GetTasks()) _pendingTasks[task.Id] = task;
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
        using var measurement = _performance.Measure("FilterLibrary");
        string query = LibrarySearch.Text.Trim();
        var results = _media.Where(item => query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Author.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        // One ItemsSource reset lets the virtualized panel realize only visible cards.
        // Adding 1,000 entries individually fires 1,000 UI collection notifications.
        foreach (var item in _media) item.IsSelected = false;
        FilteredMedia = new ObservableCollection<MediaItem>(results);
        MediaGrid.ItemsSource = FilteredMedia;
        LibrarySubtitle.Text = $"{_media.Count} 个视频 · 本地保存，随时回看";
        LibraryEmptyTitle.Text = query.Length > 0 ? "没有找到匹配的视频" : "你的灵感收藏，从这里开始";
        LibraryEmpty.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateMediaSelection();
    }

    private void BatchSelect_Click(object sender, RoutedEventArgs e)
    {
        if (_selectingMedia) MediaGrid.SelectedItems.Clear();
        _selectingMedia = !_selectingMedia;
        MediaGrid.SelectionMode = _selectingMedia ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        MediaGrid.IsItemClickEnabled = !_selectingMedia;
        BatchSelectionPanel.Visibility = _selectingMedia ? Visibility.Visible : Visibility.Collapsed;
        BatchSelectText.Text = _selectingMedia ? "退出选择" : "批量选择";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName((DependencyObject)sender, BatchSelectText.Text);
        UpdateMediaSelection();
    }

    private void Media_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.RemovedItems.OfType<MediaItem>()) item.IsSelected = false;
        foreach (var item in e.AddedItems.OfType<MediaItem>()) item.IsSelected = true;
        UpdateMediaSelection();
    }

    private void UpdateMediaSelection()
    {
        if (BatchSelectionCount is null) return;
        int count = MediaGrid.SelectedItems.Count;
        BatchSelectionCount.Text = $"已选 {count} / {FilteredMedia.Count} 项";
        DeleteSelectedButton.IsEnabled = count > 0;
        SelectAllButton.IsEnabled = FilteredMedia.Count > 0;
        SelectAllButton.Content = count > 0 && count == FilteredMedia.Count ? "取消全选" : "全选当前结果";
    }

    private void SelectAllMedia_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItems.Count == FilteredMedia.Count) MediaGrid.SelectedItems.Clear();
        else MediaGrid.SelectAll();
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_performanceTest) return;
        var items = MediaGrid.SelectedItems.Cast<MediaItem>().ToList();
        await RemoveMediaAsync(items, null);
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
        using var measurement = _performance.Measure("TaskCardRealize");
        if (!args.InRecycleQueue && args.Phase == 0 && args.ItemContainer.ContentTemplateRoot is FrameworkElement content) Motion.Enter(content, 220, 8);
    }

    private async void MediaGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Phase != 0 || args.Item is not MediaItem item) return;
        using (_performance.Measure("MediaCardRealize"))
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
        using var measurement = _performance.Measure("MediaGridSizeChanged");
        if (MediaGrid.ItemsPanelRoot is not ItemsWrapGrid panel || e.NewSize.Width <= 0) return;
        int columns = e.NewSize.Width >= 690 ? 3 : e.NewSize.Width >= 440 ? 2 : 1;
        panel.MaximumRowsOrColumns = columns;
        panel.ItemWidth = Math.Floor(e.NewSize.Width / columns);
    }

    private void CoverFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        using var measurement = _performance.Measure("CoverFrameSizeChanged");
        if (sender is not FrameworkElement element || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var radius = new System.Numerics.Vector2(13);
        visual.Clip = visual.Compositor.CreateRectangleClip(0, 0, (float)e.NewSize.Width, (float)e.NewSize.Height, radius, radius, radius, radius);
    }

    private async void Media_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_selectingMedia || e.ClickedItem is not MediaItem item) return;
        if (_performanceTest) { ShowNotice("性能测试数据", "此场景只测试界面响应，测试卡片不对应真实视频。", InfoBarSeverity.Informational); return; }
        if (item.IsMissing || !File.Exists(item.FilePath)) { ShowNotice("文件已移走", "请检查文件是否仍在原位置，或重新导入目录。", InfoBarSeverity.Warning); return; }
        await GuardAsync(() => OpenPlayerAsync(item));
    }

    private void MediaMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaItem item } button || _performanceTest) return;
        var menu = new MenuFlyout();
        var remove = new MenuFlyoutItem { Text = "仅删除记录", Icon = new SymbolIcon(Symbol.Remove) };
        var delete = new MenuFlyoutItem { Text = "删除记录并删除本地文件", Icon = new SymbolIcon(Symbol.Delete) };
        remove.Click += async (_, _) => await RemoveMediaAsync([item], false);
        delete.Click += async (_, _) => await RemoveMediaAsync([item], true);
        menu.Items.Add(remove);
        menu.Items.Add(delete);
        menu.ShowAt(button);
    }

    private async Task RemoveMediaAsync(IReadOnlyList<MediaItem> items, bool? deleteFileChoice)
    {
        if (_deletingMedia || items.Count == 0) return;
        _deletingMedia = true;
        try
        {
            bool deleteFile = deleteFileChoice == true;
            string summary = items.Count == 1 ? $"{items[0].Title}\n\n{items[0].FilePath}"
                : $"已选择 {items.Count} 项：\n" + string.Join("\n", items.Take(5).Select(x => x.Title))
                    + (items.Count > 5 ? $"\n……另 {items.Count - 5} 项" : "");
            var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
            var deleteFilesCheckBox = new CheckBox
            {
                Content = "同时删除本地文件", IsChecked = false,
                Visibility = deleteFileChoice is null ? Visibility.Visible : Visibility.Collapsed
            };
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
                Content = new StackPanel
                {
                    Spacing = 16,
                    Children = { new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 }, deleteFilesCheckBox, explanation }
                }
            };
            void UpdateConfirmation()
            {
                dialog.Title = deleteFile ? $"删除 {items.Count} 条记录和本地文件？" : $"仅删除这 {items.Count} 条记录？";
                dialog.PrimaryButtonText = deleteFile ? "删除记录和文件" : "删除记录";
                explanation.Text = deleteFile
                    ? "将永久删除所选视频文件，不经过回收站。文件已不存在时，仅删除记录。未选择的文件不会被删除。"
                    : "本地视频文件会保留。刷新或重启后，所选记录不会再次显示。";
            }
            deleteFilesCheckBox.Checked += (_, _) => { deleteFile = true; UpdateConfirmation(); };
            deleteFilesCheckBox.Unchecked += (_, _) => { deleteFile = false; UpdateConfirmation(); };
            UpdateConfirmation();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            LibraryPage.IsEnabled = false;
            await _libraryLock.WaitAsync(_lifetime.Token);
            try
            {
                if (deleteFile && items.Any(x => x.FilePath == _playing?.FilePath)) ClosePlayer();
                var result = await AppServices.Library.RemoveManyAsync(items, deleteFile, _lifetime.Token);
                var removedPaths = result.Removed.Select(x => x.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _media.RemoveAll(x => removedPaths.Contains(x.FilePath));
                FilterLibrary();
                if (_selectingMedia)
                    foreach (var failed in result.Failed.Where(FilteredMedia.Contains)) MediaGrid.SelectedItems.Add(failed);
                ShowNotice(result.Failed.Count > 0 ? (result.Removed.Count == 0 ? "删除未完成" : "部分记录未删除") : "记录已删除",
                    $"已删除 {result.Removed.Count} 条记录。" + (result.Failed.Count > 0
                        ? $"{result.Failed.Count} 项未完成，记录已保留；请核对文件占用、权限和磁盘空间后重试。"
                        : deleteFile ? "所选本地文件已删除或已不存在。" : "本地视频文件已保留。"),
                    result.Failed.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
            finally { _libraryLock.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppServices.Log("删除媒体记录", ex);
            ShowNotice("删除未完成", AppServices.UserError(ex) + " 请刷新后核对记录和文件状态。", InfoBarSeverity.Error);
        }
        finally { LibraryPage.IsEnabled = true; _deletingMedia = false; }
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
            _performance.Pause();
            foreach (var element in Descendants<FrameworkElement>(Root))
                if (Motion.GetEnabled(element)) Motion.Reset(element);
            Motion.Reset(_page); Motion.Reset(Notice); Motion.Reset(ConnectionDot);
            foreach (var progress in Descendants<SmoothProgressBar>(Root)) progress.Suspend();
        }
        else
        {
            _performance.Resume();
            _taskTimer.Start();
            if (_searchPending) _searchTimer.Start();
            if (_performanceTest && _performanceStep < 240) _performanceTimer.Start();
            foreach (var progress in Descendants<SmoothProgressBar>(Root)) progress.Resume();
            FlushTaskUpdates();
        }
    }

    private async Task ExitAsync(string? updateInstaller = null)
    {
        if (_exiting) return;
        _exiting = true;
        LinkDownloadButton.IsEnabled = false;
        if (!_performanceTest)
        {
            ShowWindow();
            EnableButton.IsEnabled = false; StopButton.IsEnabled = false;
            EngineStatusText.Text = "正在停止接入并恢复代理…";
            try { await AppServices.Engine.StopAsync(); await AppServices.Links.StopAsync(); }
            catch (Exception ex)
            {
                _exiting = false;
                LinkDownloadButton.IsEnabled = true;
                AppServices.Log("退出时停止接入", ex);
                ShowNotice("暂时无法退出", "停止接入或恢复原代理尚未完成。请在设置中重试停止接入，并查看本地日志。", InfoBarSeverity.Error);
                SetStatus(AppServices.Engine.StatusText);
                return;
            }
        }
        if (updateInstaller is not null)
        {
            try
            {
                await _updates.VerifyInstallerAsync(_availableUpdate!, updateInstaller, _lifetime.Token);
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updateInstaller)
                    { UseShellExecute = true, Arguments = "/NORESTART" });
                if (process is null) throw new IOException("无法启动更新安装程序。");
                process.Dispose();
            }
            catch (Exception ex)
            {
                _exiting = false;
                LinkDownloadButton.IsEnabled = true;
                AppServices.Log("启动更新安装", ex);
                ShowNotice("无法安装更新", "接入已停止。请重新下载更新后再试。", InfoBarSeverity.Error);
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
        catch (Exception ex)
        {
            AppServices.Log("界面操作", ex);
            var message = AppServices.Engine.StatusState == "recovery_required"
                ? "接入恢复尚未确认。请点击停止接入并查看本地日志。"
                : AppServices.UserError(ex);
            ShowNotice("操作未完成", message, InfoBarSeverity.Error);
        }
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
        _performance.Start(DispatcherQueue, "Synthetic UI: 1000 media cards, 5 progress tasks, navigation and virtualized scrolling. No network workload.");
        using (_performance.Measure("Prepare1000Items"))
        {
            for (int i = 0; i < 1000; i++)
                _media.Add(new MediaItem
                {
                    Id = $"performance-media-{i}", Title = $"性能测试数据 · 灵感片段 {i + 1:0000}", Author = $"测试作者 {i % 32 + 1}",
                    Quality = "1080p", FilePath = Path.Combine(Path.GetTempPath(), $"lumiven-performance-{i}.mp4"),
                    SizeBytes = 1024 * 1024 * (20L + i % 80), CompletedAt = DateTimeOffset.Now.AddMinutes(-i)
                });
            FilterLibrary();
        }
        _performanceTimer.Tick += PerformanceTick;
        _performanceTimer.Start();
        ShowNotice("性能测试数据", "60 秒界面压力测试正在运行，结果会保存到本地 performance 目录。", InfoBarSeverity.Informational);
    }

    private async void PerformanceTick(object? sender, object e)
    {
        using (_performance.Measure("PerformanceTick"))
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
        }
        await GuardAsync(async () =>
        {
            var path = await _performance.StopAsync();
            ShowNotice("性能测试完成", $"测试记录：{path}", InfoBarSeverity.Success);
        });
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
