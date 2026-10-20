using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lumiven.Models;
using Lumiven.Services;

internal static class LocalizationTests
{
    public static async Task RunAsync()
    {
        string originalLanguage = Localizer.CurrentLanguage;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            var catalog = VerifyCatalogs();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            const string legacyJson = """
                {"downloadDirectory":"C:\\Videos\\中文资料","theme":"Dark","concurrency":4,
                 "automaticUpdates":false,"importedDirectories":["D:\\旧版视频"]}
                """;
            var settings = JsonSerializer.Deserialize<AppSettings>(legacyJson, options)!;
            Check(settings.Language == "zh-CN", "Legacy settings must default to Chinese without migration");
            Check(!new AppSettings().SuppressStartupNotice && !settings.SuppressStartupNotice,
                "New and legacy settings must show the startup notice by default");
            settings.Language = "en-US";
            settings.SuppressStartupNotice = true;
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, options), options)!;
            Check(restored.Language == "en-US" && restored.Theme == "Dark" && restored.Concurrency == 4 &&
                !restored.AutomaticUpdates && restored.SuppressStartupNotice && restored.DownloadDirectory == @"C:\Videos\中文资料" &&
                restored.ImportedDirectories.SequenceEqual([@"D:\旧版视频"]),
                "Saving preferences must preserve startup notice choice, paths, theme, concurrency and update settings");
            restored.Language = "unsupported";
            Check(restored.Language == "zh-CN", "Unsupported stored languages must fall back to Chinese");

            // Construction and status reads never enable interception, start a process or touch system settings.
            string root = Path.Combine(Path.GetTempPath(), "lumiven-localization-test-" + Guid.NewGuid().ToString("N"));
            var engine = new EngineClient(settings, new LibraryService(root), root);
            Localizer.SetLanguage("zh-CN");
            Check(engine.StatusText == "微信下载未启用", "Engine status must initially render in Chinese");
            int changes = 0;
            EventHandler onChange = (_, _) => changes++;
            Localizer.LanguageChanged += onChange;
            try
            {
                Localizer.SetLanguage("en-US");
                Check(engine.StatusText == "WeChat downloads are not enabled", "Existing EngineClient status must switch to English");
                Localizer.SetLanguage("en-US");
                Check(changes == 1, "Reapplying the same language must not emit another change");
                Check(Localizer.T("发现 V{0} · {1:F1} MB", new Version(1, 2, 0), 12.5) == "V1.2.0 is available · 12.5 MB",
                    "Translated formatting must retain version and numeric precision");
                Check(Localizer.T("已选择") == "Selected", "Catalogs embedded in the executable must be loaded");
                string englishError = AppServices.UserError(new IOException("synthetic, no file operation"));
                Check(englishError == "The file could not be accessed. Check available disk space and whether the file is in use.",
                    "Service error handling must render in English");
                const string chineseError = "视频平台请求未成功（HTTP 403），请稍后重试。";
                string englishHttpError = Localizer.TranslateExisting(chineseError);
                Check(englishHttpError == "The video platform request failed (HTTP 403). Please try again later.",
                    "Persisted Chinese diagnostics must translate with their HTTP status intact");
                CheckUnknownText();

                Localizer.SetLanguage("zh-CN");
                Check(changes == 2 && engine.StatusText == "微信下载未启用" && engine.StatusState == "disabled" && !engine.IsRunning,
                    "Switching back must restore the existing status without starting the engine");
                Check(Localizer.TranslateExisting(englishError) == "文件访问失败，请检查磁盘空间与文件占用情况。",
                    "An already-visible English error must translate back to Chinese");
                Check(Localizer.TranslateExisting(englishHttpError) == chineseError,
                    "Formatted English errors must translate back without losing their arguments");
                CheckUnknownText();
            }
            finally { Localizer.LanguageChanged -= onChange; }
            Check(!Directory.Exists(root), "Localization must not create engine state or video directories");
            await VerifyPendingTitlesAsync();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                localization = true, catalogEntries = catalog.Count, formatParity = true, duplicateCatalogConsistency = true,
                liveEngineStatusTranslation = true, settingsBackwardCompatible = true, settingsPreserved = true,
                errorsBothDirections = true, unknownTextPreserved = true, taskMetadataPreserved = true, systemChanges = false
            }));
        }
        finally
        {
            Localizer.SetLanguage(originalLanguage);
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static async Task VerifyPendingTitlesAsync()
    {
        const string placeholder = "抖音 · 正在解析分享链接";
        string root = Path.Combine(Path.GetTempPath(), "lumiven-language-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        DownloadTask[] tasks =
        [
            new() { Id = "douyin-pending", Title = placeholder, Status = "failed" },
            new() { Id = "douyin-real", Title = placeholder, VideoId = "123456789", Status = "completed", Author = "原创作者" },
            new() { Id = "douyin-custom", Title = "我的原创视频", Status = "failed" },
            new() { Id = "wechat-original", Title = placeholder, Status = "failed" }
        ];
        await File.WriteAllTextAsync(Path.Combine(root, "link-tasks.json"), JsonSerializer.Serialize(tasks, AppServices.JsonOptions));
        var links = new LinkDownloadService(new AppSettings(), new LibraryService(root), root);
        Localizer.SetLanguage("zh-CN");
        await links.InitializeAsync();
        Localizer.SetLanguage("en-US");
        links.RefreshLanguage();
        var translated = links.GetTasks().ToDictionary(task => task.Id);
        Check(translated["douyin-pending"].Title == "Douyin · Parsing share link", "Unresolved task placeholders must switch languages");
        Check(translated["douyin-real"].Title == placeholder && translated["douyin-real"].Author == "原创作者" &&
            translated["douyin-custom"].Title == "我的原创视频" && translated["wechat-original"].Title == placeholder,
            "Language changes must not rewrite actual or unknown task metadata");
        Localizer.SetLanguage("zh-CN");
        links.RefreshLanguage();
        Check(links.GetTasks().Single(task => task.Id == "douyin-pending").Title == placeholder && !links.HasActiveTasks,
            "Placeholder translation must reverse without starting a download");
    }

    private static Dictionary<string, string> VerifyCatalogs()
    {
        var assembly = typeof(Localizer).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(name => name.EndsWith(".en.json", StringComparison.Ordinal)).ToArray();
        Check(resources.Length >= 4, "The UI, main, actions and service catalogs must be embedded");
        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var document = JsonDocument.Parse(stream);
            foreach (var pair in document.RootElement.EnumerateObject())
            {
                var english = pair.Value.GetString()!;
                Check(!string.IsNullOrWhiteSpace(english), $"Empty translation in {resource}: {pair.Name}");
                Check(!catalog.TryGetValue(pair.Name, out var previous) || previous == english,
                    $"Conflicting translation for {pair.Name}: {previous} / {english}");
                catalog[pair.Name] = english;
                CompositeFormat.Parse(pair.Name);
                CompositeFormat.Parse(english);
                Check(PlaceholderIds(pair.Name).SequenceEqual(PlaceholderIds(english)),
                    $"Translation changed format placeholders in {resource}: {pair.Name}");
            }
        }
        return catalog;
    }

    private static IEnumerable<int> PlaceholderIds(string format) =>
        Regex.Matches(format, @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Order();

    private static void CheckUnknownText()
    {
        foreach (string text in new[] { "My untouched video title", "用户原创标题 · 夏天 🌊", @"C:\Videos\我的视频 {draft}.mp4", "HTTP 599: unknown diagnostic" })
        {
            Check(Localizer.T(text) == text, "Unknown text must not be translated or treated as a format string");
            Check(Localizer.TranslateExisting(text) == text, "Unknown diagnostics must not match an overly broad translation template");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
