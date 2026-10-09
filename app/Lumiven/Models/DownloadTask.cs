using System.Text.Json.Serialization;

namespace Lumiven.Models;

public sealed class DownloadTask : ObservableObject
{
    private string _id = "", _videoId = "", _title = "", _author = "", _quality = "", _status = "queued", _filePath = "", _error = "";
    private long _bytesReceived, _bytesTotal, _speed;
    private double _progress;
    private DateTimeOffset _createdAt, _updatedAt;
    public string Id { get => _id; set => Set(ref _id, value); }
    public string VideoId { get => _videoId; set => Set(ref _videoId, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Author { get => _author; set => Set(ref _author, value); }
    public string Quality { get => _quality; set => Set(ref _quality, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    [JsonPropertyName("downloadedBytes")]
    public long BytesReceived { get => _bytesReceived; set => Set(ref _bytesReceived, value); }
    [JsonPropertyName("totalBytes")]
    public long BytesTotal { get => _bytesTotal; set => Set(ref _bytesTotal, value); }
    public long Speed { get => _speed; set => Set(ref _speed, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }
    public string RelativePath { get; set; } = "";
    public string Error { get => _error; set => Set(ref _error, value); }
    public DateTimeOffset CreatedAt { get => _createdAt; set => Set(ref _createdAt, value); }
    public DateTimeOffset UpdatedAt { get => _updatedAt; set => Set(ref _updatedAt, value); }

    public void NormalizeProgress() => Progress = Status == "completed" ? 100 :
        BytesTotal > 0 ? Math.Clamp(100d * BytesReceived / BytesTotal, 0, 99.9) : 0;
}
