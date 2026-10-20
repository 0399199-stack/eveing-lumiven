using Microsoft.UI.Xaml;
using Lumiven.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Lumiven;

public partial class App : Application
{
    public MainWindow? MainWindow { get; private set; }
    private readonly Mutex _instance;

    public App()
    {
        _instance = new Mutex(false, $"Local\\EVEING.Lumiven.{WindowsIdentity.GetCurrent().User?.Value}");
        try
        {
            if (!_instance.WaitOne(0))
            {
                ActivateExistingWindow();
                Environment.Exit(0);
            }
        }
        catch (AbandonedMutexException) { }
        Localizer.SetLanguage(AppServices.Settings.Language);
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = Localizer.CurrentLanguage;
        InitializeComponent();
        UnhandledException += (_, args) => AppServices.Log("未处理异常", args.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Closed += (_, _) => { _instance.ReleaseMutex(); _instance.Dispose(); };
        MainWindow.Activate();
    }

    private static void ActivateExistingWindow()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("EVEING.Lumiven"))
        {
            using (process)
            {
                if (process.Id == current.Id || process.SessionId != current.SessionId) continue;
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero) continue;
                ShowWindow(window, 9);
                SetForegroundWindow(window);
                break;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
