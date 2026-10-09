namespace Lumiven.Models;

public sealed class MediaItem : ObservableObject
{
    private string _thumbnail = "";
    private bool _isMissing;
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Quality { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public bool IsMissing { get => _isMissing; set => Set(ref _isMissing, value); }
    public string Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
}
