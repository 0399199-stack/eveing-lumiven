using System.Text.Json;
using System.Text.RegularExpressions;
using Lumiven.Models;

namespace Lumiven.Services;

public static class AppServices
{
    private static readonly SemaphoreSlim SettingsGate = new(1, 1);
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EVEING", "Lumiven");
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static AppSettings Settings { get; } = LoadSettings();
    public static LibraryService Library { get; } = new(DataDirectory);
    public static EngineClient Engine { get; } = new(Settings, Library, DataDirectory);
    public static LinkDownloadService Links { get; } = new(Settings, Library, DataDirectory);
    public static TrayService Tray { get; } = new();

    private static AppSettings LoadSettings()
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            var path = Path.Combine(DataDirectory, "settings.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log("读取设置", ex);
            return new();
        }
    }

    public static async Task SaveSettingsAsync()
    {
        await SettingsGate.WaitAsync();
        try { await AtomicJson.WriteAsync(Path.Combine(DataDirectory, "settings.json"), Settings, JsonOptions); }
        finally { SettingsGate.Release(); }
    }

    public static void Log(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(Path.Combine(DataDirectory, "app.log"), $"{DateTimeOffset.Now:O} {context}: {RedactDiagnostic(exception.ToString())}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string UserError(Exception exception)
    {
        var detail = exception.ToString();
        if (detail.Contains("Import-Certificate", StringComparison.OrdinalIgnoreCase))
            return "本机根证书安装失败。Windows 可能需要安全确认；如弹出系统提示，请确认后重试。未出现提示时请查看本地日志。";
        if (detail.Contains("Windows 设置失败", StringComparison.OrdinalIgnoreCase))
            return "Windows 网络设置失败，微信下载未启用。请查看本地日志后重试。";
        if (detail.Contains("找不到可用的本地端口", StringComparison.Ordinal))
            return "本地端口被占用，请关闭旧下载器后重试。";
        return exception switch
        {
            FileNotFoundException => "所需文件不存在，请检查软件安装或视频文件位置。",
            DirectoryNotFoundException => "文件夹不存在或已移动，请重新选择。",
            UnauthorizedAccessException => "访问被拒绝，请检查文件夹权限后重试。",
            IOException => "文件访问失败，请检查磁盘空间与文件占用情况。",
            _ => "操作失败，请查看本地日志后重试。"
        };
    }

    public static string RedactDiagnostic(string text)
    {
        text = Regex.Replace(text, @"(?i)\b(?:https?|wss?)://[^\s'""<>]+", "<url>");
        text = Regex.Replace(text, @"(?i)\b(?:secret_token|x-local-auth|access_token|api_key)\s*[:=]\s*[^\s,;]+", "<credential>");
        text = Regex.Replace(text, @"(?i)(?:[a-z]:\\|\\\\)[^\r\n'""<>|]+", "<path>");
        return Regex.Replace(text, @"\b[0-9a-fA-F]{32,}\b", "<credential>");
    }
}

internal static class AtomicJson
{
    public static async Task WriteAsync<T>(string path, T value, JsonSerializerOptions options)
    {
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            await JsonSerializer.SerializeAsync(stream, value, options);
        File.Move(temp, path, true);
    }
}
