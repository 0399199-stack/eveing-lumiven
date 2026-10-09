using System.Text.Json;
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
            File.AppendAllText(Path.Combine(DataDirectory, "app.log"), $"{DateTimeOffset.Now:O} {context}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
