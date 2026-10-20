using System.Text.Json;
using Lumiven.Services;

internal static class ShellFileLocatorTests
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "lumiven-locator-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "视频 # 中文, 文件夹");
        string file = Path.Combine(folder, "抖音 # AI 100% & [片段] + 🐈.mp4");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
            Check(ShellFileLocator.GetExistingFilePath(file) == file,
                "Shell paths must preserve Chinese, spaces, hash, comma, percent and other literal filename characters");
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, file);
            Check(ShellFileLocator.GetExistingFilePath(relative) == file,
                "A relative file path must become an absolute filesystem path without URI escaping");
            foreach (string invalid in new[] { Path.Combine(folder, "missing.mp4"), folder })
            {
                try { ShellFileLocator.ShowFile(invalid); throw new Exception("A missing file or directory reached the Shell selection API"); }
                catch (FileNotFoundException ex) { Check(ex.FileName == invalid, "Missing-file errors must identify the requested path"); }
            }
            foreach (string invalid in new[] { "", "invalid\0.mp4" })
            {
                try { ShellFileLocator.ShowFile(invalid); throw new Exception("An invalid path reached the Shell selection API"); }
                catch (ArgumentException) { }
            }
            Check(File.ReadAllBytes(file).SequenceEqual(new byte[] { 1, 2, 3 }), "Path validation must not change the video");
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(folder);
            Directory.Delete(root);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { shellFileLocator = true, literalSpecialCharacters = true,
            absoluteFilesystemPath = true, invalidPathsRejectedBeforeShell = true, shellWindowsOpened = false, systemChanges = false }));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
