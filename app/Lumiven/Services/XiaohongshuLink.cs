using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumiven.Services;

// Initial-state / origin-video approach adapted from Andy-SoulShell/xhs-downloader (MIT).
public static class XiaohongshuLink
{
    internal const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";

    public static Uri Extract(string text)
    {
        var match = Regex.Match(text, @"(?<![\w./@-])https://(?:xhslink\.(?:cn|com)|(?:www\.)?xiaohongshu\.com)/[^\s<>""'，。！；：、）】]+", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException("请粘贴小红书视频笔记的完整分享链接，或包含链接的整段文案。");
        var uri = new Uri(match.Value.TrimEnd(')', '.', ',', ';', '!'));
        if (!VideoLinkHttp.Allowed(uri, false, true)) throw new InvalidDataException("不支持这个小红书链接。");
        return uri;
    }

    public static async Task<LinkVideo> ResolveAsync(HttpClient http, Uri link, CancellationToken token)
    {
        using var page = await VideoLinkHttp.GetAsync(http, link, false, token, true);
        var path = page.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        var match = Regex.Match(path, @"^/(?:explore|discovery/item)/([0-9a-f]{24})/?$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException("未找到小红书视频笔记，请复制 App 中的完整分享链接。");
        return ParsePage(await VideoLinkHttp.ReadPageAsync(page, token), match.Groups[1].Value)
            ?? throw new InvalidDataException("小红书暂未返回可下载的视频。请确认是视频笔记，并重新复制完整分享链接后重试。");
    }

    internal static LinkVideo? ParsePage(string html, string expectedId)
    {
        var marker = Regex.Match(html, @"window\.__INITIAL_STATE__\s*=\s*", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!marker.Success) return null;
        var raw = html[(marker.Index + marker.Length)..];
        var end = raw.IndexOf("</script>", StringComparison.OrdinalIgnoreCase);
        if (end >= 0) raw = raw[..end];
        // Keep strings untouched; the site's JS object uses undefined for absent fields.
        raw = Regex.Replace(raw, "\"(?:\\\\.|[^\"\\\\])*\"|(?<absent>\\bundefined\\b)",
            m => m.Groups["absent"].Success ? "null" : m.Value, RegexOptions.None, TimeSpan.FromSeconds(1));
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(raw));
            using var json = JsonDocument.ParseValue(ref reader);
            var note = Get(json.RootElement, "note", "noteDetailMap", expectedId, "note");
            if (note.ValueKind != JsonValueKind.Object) note = Get(json.RootElement, "noteData", "data", "noteData");
            if (Str(note, "noteId") != expectedId || Str(note, "type") != "video") return null;
            var streams = Get(note, "video", "media", "stream");
            var candidates = streams.ValueKind == JsonValueKind.Object ? streams.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray()).ToList() : [];
            var origin = Str(note, "video", "consumer", "originVideoKey");
            var urls = new List<(string Url, string Quality)>();
            if (origin.Length > 0) urls.Add(("https://sns-video-bd.xhscdn.com/" + origin.TrimStart('/'), "原视频"));
            foreach (var stream in candidates.OrderByDescending(s => Number(s, "height")))
            {
                var quality = $"{Number(stream, "width")}×{Number(stream, "height")}";
                urls.Add((Str(stream, "masterUrl"), quality));
                var backups = Get(stream, "backupUrls");
                if (backups.ValueKind == JsonValueKind.Array)
                    urls.AddRange(backups.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => (v.GetString()!, quality)));
            }
            foreach (var (value, quality) in urls)
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var url)) continue;
                if (url.Scheme == "http" && url.IsDefaultPort) url = new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri;
                if (!VideoLinkHttp.Allowed(url, true, true)) continue;
                var title = Str(note, "title");
                var author = Str(note, "user", "nickname");
                if (author.Length == 0) author = Str(note, "user", "nickName");
                return new(expectedId, title.Length == 0 ? "小红书视频" : title, author, quality, url);
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static JsonElement Get(JsonElement item, params string[] path)
    {
        foreach (var key in path)
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out item)) return default;
        return item;
    }
    private static string Str(JsonElement item, params string[] path) => Get(item, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";
    private static int Number(JsonElement item, string key) => Get(item, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 0;
}
