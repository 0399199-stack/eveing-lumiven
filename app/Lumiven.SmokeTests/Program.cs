using System.Text.Json;
using Lumiven.Models;
using Lumiven.Services;

var root = Path.Combine(Path.GetTempPath(), "lumiven-library-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var video = Path.Combine(root, "原始中文视频.mp4");
await File.WriteAllBytesAsync(video, [0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109]);
var library = new LibraryService(root);
var completed = new DownloadTask
{
    Id = "completed", Title = "湖畔的一束光", Author = "光影记录", Quality = "1080p",
    Status = "completed", FilePath = video, BytesTotal = 12, UpdatedAt = DateTimeOffset.Now
};
await library.RecordAsync(completed);
await library.RecordAsync(new DownloadTask { Id = "partial", Status = "downloading", FilePath = video });
Check((await library.GetAllAsync()).Count == 1, "An unfinished task must not enter the library");

var restoredLibrary = new LibraryService(root);
var restored = await restoredLibrary.GetAllAsync();
Check(restored.Count == 1 && restored[0].Title == completed.Title && restored[0].FilePath == video,
    "Completed media path and title must survive restart");

// Import indexes original files without moving or replacing their contents.
var legacy = Path.Combine(root, "旧目录", new string('长', 80), new string('路', 80));
Directory.CreateDirectory(legacy);
var importedFile = Path.Combine(legacy, "导入旧视频.mp4");
File.Copy(video, importedFile);
await restoredLibrary.ScanAsync(root);
var imported = await restoredLibrary.GetAllAsync();
Check(imported.Count == 2 && File.Exists(importedFile), "Import must preserve the original file");
Check(imported.Single(x => x.FilePath == video).Author == "光影记录", "Import must preserve download metadata");
await restoredLibrary.ScanAsync(root);
Check((await restoredLibrary.GetAllAsync()).Count == 2, "Repeated import must not duplicate media");

File.Move(video, Path.Combine(root, "移走的视频.mp4"));
await restoredLibrary.RefreshAsync(imported);
var missing = imported.Single(x => x.FilePath == video);
Check(missing.IsMissing && !imported.Single(x => x.FilePath == importedFile).IsMissing,
    "External removal must mark only the missing item");
Check(await restoredLibrary.GetThumbnailAsync(missing) is null, "Missing media must not generate a thumbnail");
try { await restoredLibrary.ScanAsync(Path.Combine(root, "不存在")); throw new Exception("Missing directory accepted"); }
catch (DirectoryNotFoundException) { }

var partial = new DownloadTask { Id = "partial", Status = "processing", BytesReceived = 12, BytesTotal = 12 };
partial.NormalizeProgress();
Check(partial.Progress < 100, "Processing must not look completed");
await File.WriteAllTextAsync(Path.Combine(root, "task-cache.json"), JsonSerializer.Serialize(new[] { partial, completed }, AppServices.JsonOptions));
var client = new EngineClient(new AppSettings(), restoredLibrary, root);
await client.InitializeAsync();
var tasks = await client.GetTasksAsync();
Check(tasks.Single(x => x.Id == "partial").Status == "interrupted", "Unfinished cache must become interrupted after restart");
Check(tasks.Single(x => x.Id == "completed").Status == "completed", "Completed cache must stay completed");
Check(new AppSettings().Theme == "Light", "New installations must use the approved light ice-blue theme");
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, persistedLibrary = true, importDoesNotMoveFiles = true,
    idempotentImport = true, longChinesePath = true, externalMissing = true, interruptedCache = true,
    falseCompletionPrevented = true, defaultLight = true, systemChanges = false }));

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
