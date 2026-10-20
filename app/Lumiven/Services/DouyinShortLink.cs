using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumiven.Services;

// Share-page approach: qgeng1465/douyin-watermark-free-downloader and
// wujunwei928/parse-video (MIT). See THIRD-PARTY-NOTICES.md.
public sealed record LinkVideo(string Id, string Title, string Author, string Quality, Uri Url);

public static class DouyinShortLink
{
    internal const string UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 26_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Mobile/15E148 Safari/604.1";
    private const string NoVideo = "抖音暂未返回可下载的视频，请稍后重试。此结果不代表作品已删除。";

    public static Uri Extract(string text)
    {
        var match = Regex.Match(text, @"(?<![A-Za-z0-9_./@-])https://v\.douyin\.com/[A-Za-z0-9]+/?(?=$|[\s\p{IsCJKUnifiedIdeographs}，。！；：、）】!?])",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException(Localizer.T("请粘贴抖音 App 的视频分享短链接（https://v.douyin.com/…），或包含它的整段文案。"));
        return new Uri(match.Value);
    }

    public static async Task<LinkVideo> ResolveAsync(HttpClient http, Uri shortLink, CancellationToken token)
    {
        using var share = await VideoLinkHttp.GetAsync(http, shortLink, false, token);
        var path = share.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        var match = Regex.Match(path, @"^/(?:share/)?video/(\d{10,24})/?$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException(Localizer.T("未找到视频作品。首版暂不支持图集、直播或用户主页。"));
        var id = match.Groups[1].Value;
        var html = await VideoLinkHttp.ReadPageAsync(share, token);
        var video = ParsePage(html, id);
        if (video is not null) return video;
        // The canonical mobile page can return details omitted from the tracking redirect.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(1), token);
            using var page = await VideoLinkHttp.GetAsync(http, new Uri($"https://www.iesdouyin.com/share/video/{id}"), false, token);
            video = ParsePage(await VideoLinkHttp.ReadPageAsync(page, token), id);
            if (video is not null) return video;
        }
        throw new InvalidDataException(Localizer.T(NoVideo));
    }

    internal static LinkVideo? ParsePage(string html, string expectedId)
    {
        var marker = Regex.Match(html, @"window\._ROUTER_DATA\s*=\s*", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!marker.Success) return null;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(html[(marker.Index + marker.Length)..]));
            using var json = JsonDocument.ParseValue(ref reader);
            var root = json.RootElement;
            if (!root.TryGetProperty("loaderData", out var loaders) ||
                !loaders.TryGetProperty("video_(id)/page", out var page) ||
                !page.TryGetProperty("videoInfoRes", out var info) || info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("item_list", out var items) || items.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in items.EnumerateArray())
            {
                if (Text(item, "aweme_id") != expectedId) continue;
                if (!item.TryGetProperty("video", out var video) ||
                    !video.TryGetProperty("play_addr", out var play) ||
                    !play.TryGetProperty("url_list", out var urls) || urls.ValueKind != JsonValueKind.Array) return null;
                foreach (var entry in urls.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String) continue;
                    var value = entry.GetString()!.Replace("/playwm/", "/play/", StringComparison.Ordinal);
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !VideoLinkHttp.Allowed(uri, true)) continue;
                    var title = Text(item, "desc");
                    var author = item.TryGetProperty("author", out var owner) ? Text(owner, "nickname") : "";
                    var quality = video.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var width) &&
                        video.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var height)
                        ? $"{width}×{height}" : "抖音视频";
                    return new(expectedId, string.IsNullOrWhiteSpace(title) ? Localizer.T("抖音视频") : title, author, quality, uri);
                }
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static string Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
}
