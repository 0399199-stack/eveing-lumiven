namespace Lumiven.Models;

public sealed class AppSettings : ObservableObject
{
    private string _downloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EVEING Lumiven");
    private string _theme = "Light";
    private int _concurrency = 2;
    public string DownloadDirectory { get => _downloadDirectory; set => Set(ref _downloadDirectory, value); }
    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public int Concurrency { get => _concurrency; set => Set(ref _concurrency, Math.Clamp(value, 1, 5)); }
    public bool AutomaticUpdates { get; set; } = true;
    public List<string> ImportedDirectories { get; set; } = [];
}
