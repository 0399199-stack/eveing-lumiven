using System.Net;
using System.Text.Json;
using Lumiven.Models;
using Lumiven.Services;

internal static class LinkDownloadTests
{
    private const string Id = "1234567890123456789";
    private const string Short = "https://v.douyin.com/Test123/";
    private static readonly byte[] Mp4 = [0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 109, 112, 52, 49];
    private static string Page(string id = Id, string url = "https://v3.douyinvod.com/video.mp4") =>
        "window._ROUTER_DATA=" + JsonSerializer.Serialize(new
        {
            loaderData = new Dictionary<string, object>
            {
                ["video_(id)/page"] = new { videoInfoRes = new { item_list = new[] {
                    new { aweme_id = id, desc = "测试标题", author = new { nickname = "作者" },
                        video = new { width = 720, height = 1280, play_addr = new { url_list = new[] { url } } } }
                } } }
            }
        }) + ";</script>";

    public static async Task RunAsync()
    {
        const string xhsId = "1234567890abcdef12345678";
        const string xhsLink = "https://www.xiaohongshu.com/explore/" + xhsId + "?xsec_token=test";
        Check(XiaohongshuLink.Extract("视频分享 " + xhsLink + " 查看").AbsoluteUri == xhsLink, "XHS token preserved");
        Check(XiaohongshuLink.Extract("视频 https://xhslink.cn/o/Test123 查看").Host == "xhslink.cn", "XHS short share extraction");
        var xhsPage = "window.__INITIAL_STATE__=" + JsonSerializer.Serialize(new { note = new { noteDetailMap = new Dictionary<string, object> {
            [xhsId] = new { note = new { noteId = xhsId, type = "video", title = "undefined 原文", user = new { nickname = "作者" },
                video = new { consumer = new { originVideoKey = "example/video.mp4" } } } }
        } }, absent = "REPLACE_UNDEFINED" }).Replace("\"REPLACE_UNDEFINED\"", "undefined") + ";</script>";
        Check(XiaohongshuLink.ParsePage(xhsPage, xhsId)?.Title == "undefined 原文", "XHS undefined handling preserves strings");
        var streamPage = xhsPage.Replace("\"consumer\":{\"originVideoKey\":\"example/video.mp4\"}", "\"media\":{\"stream\":{\"h264\":[{\"width\":720,\"height\":1280,\"masterUrl\":\"http://sns-video-v6.xhscdn.com/video.mp4\"}]}}");
        Check(XiaohongshuLink.ParsePage(streamPage, xhsId)?.Url.Scheme == "https", "XHS legacy media address upgraded to HTTPS");
        Check(XiaohongshuLink.ParsePage(xhsPage, "ffffffffffffffffffffffff") is null, "XHS wrong ID rejected");
        Check(XiaohongshuLink.ParsePage(xhsPage.Replace("\"type\":\"video\"", "\"type\":\"normal\""), xhsId) is null, "XHS image note rejected");
        Check(!VideoLinkHttp.Allowed(new Uri("https://xhscdn.com.evil.test/video.mp4"), true, true), "XHS CDN suffix spoof rejected");
        Check(DouyinShortLink.Extract("复制打开抖音 " + Short + " 看视频").AbsoluteUri == Short, "Share text extraction");
        Check(DouyinShortLink.Extract("视频链接：" + Short).AbsoluteUri == Short, "Chinese share prefix extraction");
        Check(DouyinShortLink.Extract("确认" + Short).AbsoluteUri == Short, "Adjacent Chinese share text extraction");
        using (var client = new HttpClient(new Fixture("transient-details")))
            Check((await DouyinShortLink.ResolveAsync(client, new Uri(Short), CancellationToken.None)).Id == Id, "Missing details recover on bounded retry");
        foreach (var input in new[] { "https://v.douyin.com.evil.test/abc", "https://v.douyin.com@evil.test/abc",
            "https://example.com/https://v.douyin.com/abc/", "https://v.douyin.com/abc/../../private", "https://www.douyin.com/video/" + Id })
        {
            try { DouyinShortLink.Extract(input); throw new Exception("Invalid link accepted"); }
            catch (InvalidDataException) { }
        }
        Check(DouyinShortLink.ParsePage(Page(), Id)?.Author == "作者", "Metadata extracted from exact item");
        Check(DouyinShortLink.ParsePage(Page("0000000000000000000"), Id) is null, "Wrong video ID rejected");
        Check(DouyinShortLink.ParsePage(Page(url: "http://127.0.0.1/media"), Id) is null, "Local media URL rejected");
        Check(DouyinShortLink.ParsePage("<html>verification</html>", Id) is null, "Verification page is not a video");
        Check(DouyinShortLink.ParsePage("window._ROUTER_DATA={\"loaderData\":{}};</script>", Id) is null, "Empty details rejected");
        Check(!VideoLinkHttp.Allowed(new Uri("https://v.douyin.com.evil.test/x"), false), "Host suffix exact boundary");
        Check(VideoLinkHttp.Allowed(new Uri("https://cdn.sjxydc.com/video.mp4"), true) &&
            !VideoLinkHttp.Allowed(new Uri("https://cdn.sjxydc.com.evil.test/video.mp4"), true), "Observed CDN allowed without accepting suffix spoofing");
        using (var client = new HttpClient(new Fixture("unsafe")))
        {
            try { await DouyinShortLink.ResolveAsync(client, new Uri(Short), CancellationToken.None); throw new Exception("Unsafe redirect accepted"); }
            catch (InvalidDataException) { }
        }
        var root = Path.Combine(Path.GetTempPath(), "lumiven-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var mode in new[] { "success", "html", "truncated", "no-details", "cancel", "record-failure" })
        {
            var data = Path.Combine(root, mode);
            Directory.CreateDirectory(data);
            var library = new LibraryService(data);
            var settings = new AppSettings { DownloadDirectory = Path.Combine(data, "videos") };
            var fixture = new Fixture(mode);
            var service = new LinkDownloadService(settings, library, data)
            {
                ClientFactory = () => new HttpClient(fixture) { Timeout = Timeout.InfiniteTimeSpan }
            };
            await service.InitializeAsync();
            if (mode == "record-failure") Directory.CreateDirectory(Path.Combine(data, "library.json.tmp"));
            var statuses = new List<string>();
            service.TaskUpdated += (_, task) => statuses.Add(task.Status);
            service.Start("分享 " + Short);
            if (mode == "cancel")
            {
                await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                service.Cancel(service.GetTasks().Single().Id);
            }
            await service.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var task = service.GetTasks().Single();
            if (mode == "success")
            {
                Check(task.Status == "completed" && task.Progress == 100, "Successful download completed");
                Check(File.Exists(task.FilePath) && (await File.ReadAllBytesAsync(task.FilePath)).SequenceEqual(Mp4), "Saved bytes exact");
                var records = await new LibraryService(data).GetAllAsync();
                Check(records.Single().Title == "测试标题" && records.Single().Author == "作者", "Library survives restart with metadata");
                Check(statuses.Contains("parsing") && statuses.Contains("downloading") && statuses.Contains("saving"), "Real stages emitted");
            }
            else
            {
                Check(task.Status == (mode == "cancel" ? "cancelled" : "failed"), $"Failure state: {mode}");
                Check(!statuses.Contains("completed"), $"No false complete event: {mode}");
                Check((await new LibraryService(data).GetAllAsync()).Count == 0, $"No completed library record: {mode}");
                Check((await library.GetAllAsync()).Count == 0, $"No false in-memory library record: {mode}");
            }
            Check(!Directory.EnumerateFiles(data, "*.part", SearchOption.AllDirectories).Any(), $"Partial cleanup: {mode}");
            var restored = new LinkDownloadService(settings, new LibraryService(data), data);
            await restored.InitializeAsync();
            Check(restored.GetTasks().Single().Status == task.Status, $"Task status persists: {mode}");
            Check(!(await File.ReadAllTextAsync(Path.Combine(data, "link-tasks.json"))).Contains("https://"), "Cache contains no share or media tokens");
        }
        var interrupted = new DownloadTask { Id = "interrupted", Status = "downloading", Title = "中断测试" };
        await File.WriteAllTextAsync(Path.Combine(root, "link-tasks.json"), JsonSerializer.Serialize(new[] { interrupted }, AppServices.JsonOptions));
        var recovering = new LinkDownloadService(new(), new(root), root);
        await recovering.InitializeAsync();
        Check(recovering.GetTasks().Single().Status == "interrupted", "Interrupted download restored truthfully");
        Console.WriteLine("PASS Douyin short-link parsing, downloads, cancellation, failures and persistence");
    }

    public static async Task LiveAsync(string link, string root, bool xiaohongshu = false)
    {
        Directory.CreateDirectory(root);
        var service = new LinkDownloadService(new AppSettings { DownloadDirectory = Path.Combine(root, "videos") }, new LibraryService(root), root)
        {
            ClientFactory = () => new HttpClient(new HostTrace
            {
                InnerHandler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }
            }) { Timeout = Timeout.InfiniteTimeSpan }
        };
        await service.InitializeAsync();
        service.Start(link, xiaohongshu);
        await service.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(3));
        var task = service.GetTasks().First();
        Console.WriteLine(JsonSerializer.Serialize(new { task.Status, task.Title, task.Quality, task.BytesReceived, task.FilePath, task.Error }));
        Environment.ExitCode = task.Status == "completed" ? 0 : 1;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class HostTrace : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            Console.WriteLine(JsonSerializer.Serialize(new { host = request.RequestUri?.Host, status = (int)response.StatusCode,
                redirectHost = response.Headers.Location is { IsAbsoluteUri: true } location ? location.Host : null,
                redirectScheme = response.Headers.Location is { IsAbsoluteUri: true } target ? target.Scheme : null }));
            return response;
        }
    }

    private sealed class Fixture(string mode) : HttpMessageHandler
    {
        private int _pageRequests;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult();
            if (mode == "cancel") await Task.Delay(Timeout.Infinite, token);
            var uri = request.RequestUri!;
            if (uri.Host == "v.douyin.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
                redirect.Headers.Location = new Uri(mode == "unsafe" ? "https://127.0.0.1/private" : $"https://www.iesdouyin.com/share/video/{Id}/");
                return redirect;
            }
            if (uri.Host == "www.iesdouyin.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request,
                    Content = new StringContent(mode == "no-details" || (mode == "transient-details" && ++_pageRequests < 3) ? "<html>No details</html>" : Page()) };
            var content = new ByteArrayContent(mode == "html" ? "<html>Not a video</html>"u8.ToArray() : Mp4);
            if (mode == "truncated") content.Headers.ContentLength = Mp4.Length + 100;
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content };
        }
    }
}
