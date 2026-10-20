using System.Net;
using System.Text;

namespace Lumiven.Services;

internal static class VideoLinkHttp
{
    internal static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        CookieContainer = new CookieContainer()
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static bool Allowed(Uri uri, bool media, bool xiaohongshu = false)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        string[] domains = xiaohongshu
            ? media ? ["xhscdn.com", "xiaohongshu.com"] : ["xhslink.cn", "xhslink.com", "xiaohongshu.com"]
            : media
            ? ["douyin.com", "iesdouyin.com", "douyinvod.com", "snssdk.com", "bytecdn.cn", "sjxydc.com"]
            : ["douyin.com", "iesdouyin.com"];
        return domains.Any(d => uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri url, bool media, CancellationToken token, bool xiaohongshu = false)
    {
        for (int redirects = 0; redirects < 6; redirects++)
        {
            if (!Allowed(url, media, xiaohongshu)) throw new InvalidDataException(Localizer.T("分享链接跳转到了不支持的地址。"),
                new InvalidDataException($"scheme={url.Scheme}; host={url.Host}; port={url.Port}"));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(xiaohongshu ? XiaohongshuLink.UserAgent : DouyinShortLink.UserAgent);
            if (xiaohongshu)
            {
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Accept-Language", "en-us,en;q=0.5");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                request.Headers.Referrer = new Uri("https://www.xiaohongshu.com/");
            }
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException(Localizer.T("分享链接缺少跳转地址。"));
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new HttpRequestException(Localizer.T("视频平台请求未成功（HTTP {0}），请稍后重试。", status));
            }
            return response;
        }
        throw new InvalidDataException(Localizer.T("分享链接跳转次数过多。"));
    }

    internal static async Task<string> ReadPageAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[32768];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException(Localizer.T("分享页面过大，无法解析。"));
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

}
