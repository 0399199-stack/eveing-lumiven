using System.Text.Json;
using Lumiven.Models;
using Lumiven.Services;

if (args.Contains("--xhs-regression-only"))
{
    await XiaohongshuRegressionTests.RunAsync();
    return;
}
if (args.Length == 3 && args[0] is "--douyin-live" or "--xhs-live")
{
    await LinkDownloadTests.LiveAsync(args[1], args[2], args[0] == "--xhs-live");
    return;
}
if (args.Contains("--check-updates-live"))
{
    using var updates = new UpdateService(Path.GetTempPath());
    var update = await updates.CheckAsync(new Version(1, 1, 0), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { githubReachable = true, availableVersion = update?.Version.ToString(), downloaded = false }));
    return;
}
await LibraryRefreshTests.RunAsync();
if (args.Contains("--library-refresh-only")) return;
await ShellFileLocatorTests.RunAsync();
if (args.Contains("--shell-locator-only")) return;
await LocalizationTests.RunAsync();
if (args.Contains("--localization-only")) return;
await EngineRepairTests.RunAsync();
await UpdateTests.RunAsync();
await XiaohongshuRegressionTests.RunAsync();
await PersistenceRegressionTests.RunAsync();
await LinkDownloadTests.RunAsync();

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
var certificateFailure = new InvalidOperationException("Windows 设置失败: Import-Certificate -FilePath 'C:\\Users\\15168\\secret.cer': 此操作中不允许使用 UI。", new Exception("exit status 1"));
var userError = AppServices.UserError(certificateFailure);
Check(userError.Contains("证书") && !userError.Contains("Import-Certificate") && !userError.Contains("C:\\Users") && !userError.Contains('\n'),
    "Certificate failure must be actionable without exposing PowerShell output in the UI");
var diagnostic = AppServices.RedactDiagnostic("Import-Certificate -FilePath 'C:\\Users\\15168\\secret.cer' https://example.com/watch?key=private secret_token: 0123456789abcdef0123456789abcdef");
Check(diagnostic.Contains("Import-Certificate") && !diagnostic.Contains("15168") && !diagnostic.Contains("private") && !diagnostic.Contains("0123456789abcdef"),
    "Local diagnostics must retain the failure command but redact paths and credentials");

// Deletion uses synthetic files only and must survive scans and stale engine events.
var removalRoot = Path.Combine(root, "removal");
Directory.CreateDirectory(removalRoot);
var retainedFile = Path.Combine(removalRoot, "保留本地文件.mp4");
await File.WriteAllBytesAsync(retainedFile, [1, 2, 3]);
var recordedTask = new DownloadTask { Id = "remove-record", Status = "completed", FilePath = retainedFile,
    Title = "删除前标题", UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
var oldItem = new MediaItem { Id = recordedTask.Id, FilePath = retainedFile,
    Title = recordedTask.Title, CompletedAt = recordedTask.UpdatedAt };
await File.WriteAllTextAsync(Path.Combine(removalRoot, "library.json"),
    JsonSerializer.Serialize(new[] { oldItem }, AppServices.JsonOptions));
var removals = new LibraryService(removalRoot);
Check((await removals.GetAllAsync()).Single().Title == oldItem.Title, "Legacy array indexes must still load");
await removals.RemoveAsync(oldItem, false);
Check(File.Exists(retainedFile) && (await removals.GetAllAsync()).Count == 0, "Record-only deletion must keep the video");
Check(!(await File.ReadAllTextAsync(Path.Combine(removalRoot, "library.json"))).Contains("remove-record"),
    "Removed item metadata must be discarded");
removals = new LibraryService(removalRoot);
Check((await removals.ScanAsync(removalRoot)).Count == 0, "A restart and automatic scan must not restore a deleted record");
await removals.RecordAsync(recordedTask);
await removals.RecordManyAsync([recordedTask]);
Check((await removals.GetAllAsync()).Count == 0, "Stale snapshot and socket events must not restore deleted records");
recordedTask.Id = "new-download";
recordedTask.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1);
await removals.RecordAsync(recordedTask);
var freshItem = (await removals.GetAllAsync()).Single();
Check(freshItem.Id == "new-download", "A new download to the same path must be visible");
try { await removals.RemoveAsync(oldItem, true); throw new Exception("Stale card deleted a new download"); }
catch (IOException) { }
Check(File.Exists(retainedFile), "A stale UI card must not delete a newer file");
using (File.Open(retainedFile, FileMode.Open, FileAccess.Read, FileShare.None))
{
    try { await removals.RemoveAsync(freshItem, true); throw new Exception("Locked file deletion succeeded"); }
    catch (IOException) { }
}
Check((await removals.GetAllAsync()).Count == 1 && File.Exists(retainedFile), "A file deletion failure must preserve the record");
using (File.Open(Path.Combine(removalRoot, "library.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
{
    try { await removals.RemoveAsync(freshItem, false); throw new Exception("Locked index replacement succeeded"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
}
Check((await removals.GetAllAsync()).Count == 1, "A failed index write must preserve the in-memory record");
await removals.RemoveAsync(freshItem, true);
Check(!File.Exists(retainedFile) && (await new LibraryService(removalRoot).GetAllAsync()).Count == 0,
    "Combined deletion must remove only the selected file and persist the record removal");
Check(File.Exists(importedFile), "Deleting one video must not remove neighboring files");

var missingTask = new DownloadTask { Id = "missing", Status = "completed", FilePath = Path.Combine(removalRoot, "已移走.mp4"), UpdatedAt = DateTimeOffset.UtcNow };
await removals.RecordAsync(missingTask);
await removals.RemoveAsync((await removals.GetAllAsync()).Single(), true);
Check((await removals.GetAllAsync()).Count == 0, "Missing files must still allow record deletion");
var directoryPath = Path.Combine(removalRoot, "替换为目录.mp4");
Directory.CreateDirectory(directoryPath);
await removals.RecordAsync(new DownloadTask { Id = "directory", Status = "completed", FilePath = directoryPath, UpdatedAt = DateTimeOffset.UtcNow });
var directoryItem = (await removals.GetAllAsync()).Single();
try { await removals.RemoveAsync(directoryItem, true); throw new Exception("Directory deletion accepted"); }
catch (IOException) { }
Check(Directory.Exists(directoryPath) && (await removals.GetAllAsync()).Count == 1, "Directories must never be deleted");
await removals.RemoveAsync(directoryItem, false);
await removals.RemoveAsync(new MediaItem { Id = "unregistered", FilePath = importedFile }, true);
Check(File.Exists(importedFile), "An unregistered file must not be deleted");

var batchRoot = Path.Combine(root, "batch");
Directory.CreateDirectory(batchRoot);
var batchLibrary = new LibraryService(batchRoot);
for (int i = 0; i < 3; i++) await File.WriteAllBytesAsync(Path.Combine(batchRoot, $"批量{i}.mp4"), [1, 2, 3]);
var batchItems = (await batchLibrary.ScanAsync(batchRoot)).OrderBy(x => x.FilePath).ToList();
var onlyRecords = await batchLibrary.RemoveManyAsync([batchItems[0], batchItems[0]], false);
Check(onlyRecords.Removed.Count == 1 && onlyRecords.Failed.Count == 0 && File.Exists(batchItems[0].FilePath),
    "Batch record-only deletion must preserve files and deduplicate the selection");
using (File.Open(batchItems[1].FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
{
    var partialBatch = await batchLibrary.RemoveManyAsync([batchItems[1], batchItems[2]], true);
    Check(partialBatch.Removed.Single() == batchItems[2] && partialBatch.Failed.Single() == batchItems[1],
        "One locked file must not prevent deletion of other selected files");
}
Check(File.Exists(batchItems[0].FilePath) && File.Exists(batchItems[1].FilePath) && !File.Exists(batchItems[2].FilePath),
    "Batch deletion must affect only selected unlocked files");
var batchRestart = new LibraryService(batchRoot);
Check((await batchRestart.ScanAsync(batchRoot)).Single().FilePath == batchItems[1].FilePath,
    "Partial batch success must survive restart while retaining the failed record");
await batchRestart.RemoveManyAsync(await batchRestart.GetAllAsync(), true);
Check((await new LibraryService(batchRoot).GetAllAsync()).Count == 0, "Failed batch entries must be retryable");

Console.WriteLine(JsonSerializer.Serialize(new { passed = true, persistedLibrary = true, importDoesNotMoveFiles = true,
    idempotentImport = true, longChinesePath = true, externalMissing = true, interruptedCache = true,
    falseCompletionPrevented = true, defaultLight = true, safeCertificateError = true, redactedDiagnostics = true,
    recordOnlyDeletion = true, fileDeletion = true, deletionSurvivesRestartAndScan = true, staleEventsIgnored = true,
    newDownloadsVisible = true, deletionFailuresPreserveRecord = true, missingFileDeletion = true,
    directoriesAndUnregisteredFilesProtected = true, legacyIndexMigration = true,
    batchRecordDeletion = true, batchPartialFailure = true, batchRetry = true, systemChanges = false }));

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
