using System.Net;
using System.Text.Json;
using Lumiven.Services;

internal static class XiaohongshuRegressionTests
{
    private const string Id = "1234567890abcdef12345678";
    private const string Short = "https://xhslink.cn/o/A377QmxsLDs";
    private const string Origin = "https://sns-video-bd.xhscdn.com/origin.mp4";
    private const string Master = "https://sns-video-hw.xhscdn.com/master.mp4";
    private const string Backup = "https://sns-video-qc.xhscdn.com/backup.mp4";

    public static async Task RunAsync()
    {
        Check(XiaohongshuLink.Extract("分享小红书" + Short + "把文本复制下来，打开小红书").AbsoluteUri == Short,
            "Chinese text adjacent to either end must not become part of the share URL");
        string signed = $"https://www.xiaohongshu.com/explore/{Id}?xsec_token=test%2B42%3D&xsec_source=pc_share";
        Check(XiaohongshuLink.Extract("笔记：" + signed + "复制链接").AbsoluteUri == signed,
            "Extracting adjacent text must preserve the entire signed query");
        Check(XiaohongshuLink.Extract("【" + Short + "】").AbsoluteUri == Short, "Common share punctuation must be excluded");
        foreach (string invalid in new[] { "https://xhslink.cn.evil.test/o/abc", "https://www.xiaohongshu.com.evil.test/explore/" + Id })
        {
            try { XiaohongshuLink.Extract(invalid); throw new Exception("Lookalike share host accepted"); }
            catch (InvalidDataException) { }
        }

        var candidates = XiaohongshuLink.ParsePageCandidates(Page(), Id);
        Check(candidates.Select(video => video.Url.AbsoluteUri).SequenceEqual(new[] { Origin, Master, Backup }),
            "Origin, highest-quality master and backups must remain ordered, allowed and deduplicated");
        Check(candidates.Select(video => video.Quality).SequenceEqual(new[] { "原视频", "1080×1920", "1080×1920" }),
            "Each fallback must retain its own quality");
        Check(candidates.All(video => video.Id == Id && video.Title == "测试 undefined 标题" && video.Author == "测试作者"),
            "All candidates must retain the matched note metadata");
        Check(XiaohongshuLink.ParsePageCandidates(Page(), "000000000000000000000000").Count == 0,
            "A different note must not be substituted");
        Check(XiaohongshuLink.ParsePageCandidates(Page("normal"), Id).Count == 0,
            "Image notes must not be advertised as downloadable videos");

        foreach (string mode in new[] { "origin-403", "master-403", "partial-fallback" })
        {
            using var fixture = new Fixture(mode);
            using var http = new HttpClient(fixture);
            var opened = await VideoLinkHttp.OpenMediaAsync(http, candidates, CancellationToken.None, true);
            using var response = opened.Response;
            string expected = mode == "origin-403" ? Master : Backup;
            Check(opened.Video.Url.AbsoluteUri == expected && opened.Video.Quality == "1080×1920" && response.StatusCode == HttpStatusCode.OK,
                $"An available fallback must be returned with its real metadata: {mode}");
            Check(fixture.Requests.SequenceEqual(mode == "origin-403" ? new[] { Origin, Master } : new[] { Origin, Master, Backup }),
                $"Candidates must be fetched once, in order: {mode}");
        }

        foreach (string mode in new[] { "all-403", "all-partial" })
        {
            using var fixture = new Fixture(mode);
            using var http = new HttpClient(fixture);
            bool failed = false;
            try
            {
                var opened = await VideoLinkHttp.OpenMediaAsync(http, candidates, CancellationToken.None, true);
                opened.Response.Dispose();
            }
            catch (HttpRequestException ex) when (mode == "all-403" && ex.StatusCode == HttpStatusCode.Forbidden) { failed = true; }
            catch (InvalidDataException) when (mode == "all-partial") { failed = true; }
            Check(failed && fixture.Requests.Count == 3, $"No successful response may be manufactured when every source fails: {mode}");
        }

        using (var fixture = new Fixture("origin-timeout"))
        using (var http = new HttpClient(fixture))
        {
            var opened = await VideoLinkHttp.OpenMediaAsync(http, candidates, CancellationToken.None, true, TimeSpan.FromMilliseconds(30));
            using var response = opened.Response;
            Check(opened.Video.Url.AbsoluteUri == Master && fixture.Requests.SequenceEqual(new[] { Origin, Master }),
                "A stalled source must time out independently and allow the next source to open");
        }
        using (var fixture = new Fixture("all-timeout"))
        using (var http = new HttpClient(fixture))
        {
            bool timedOut = false;
            try { await VideoLinkHttp.OpenMediaAsync(http, candidates, CancellationToken.None, true, TimeSpan.FromMilliseconds(30)); }
            catch (OperationCanceledException) { timedOut = true; }
            Check(timedOut && fixture.Requests.Count == 3, "Exhausting per-source timeouts must fail instead of returning a video");
        }

        using (var fixture = new Fixture("cancel"))
        using (var http = new HttpClient(fixture))
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = VideoLinkHttp.OpenMediaAsync(http, candidates, cancellation.Token, true);
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            bool cancelled = false;
            try { await pending; }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && fixture.Requests.Count == 1, "Cancellation must stop immediately without requesting a fallback");
        }
        using (var fixture = new Fixture("all-403"))
        using (var http = new HttpClient(fixture))
        {
            var many = Enumerable.Range(0, 10).Select(index => candidates[0] with { Url = new Uri($"https://sns-video-bd.xhscdn.com/{index}.mp4") }).ToArray();
            try { await VideoLinkHttp.OpenMediaAsync(http, many, CancellationToken.None, true); }
            catch (HttpRequestException) { }
            Check(fixture.Requests.Count == 8, "Fallback attempts must stay bounded");
        }
        Console.WriteLine("PASS Xiaohongshu adjacent share text, signed queries, candidate order, 403 fallback, partial-response rejection, header timeouts and cancellation (fake HTTP only)");
    }

    private static string Page(string type = "video") => "window.__INITIAL_STATE__=" + JsonSerializer.Serialize(new
    {
        note = new
        {
            noteDetailMap = new Dictionary<string, object>
            {
                [Id] = new
                {
                    note = new
                    {
                        noteId = Id, type, title = "测试 undefined 标题", user = new { nickName = "测试作者" },
                        video = new
                        {
                            consumer = new { originVideoKey = "origin.mp4" },
                            media = new
                            {
                                stream = new
                                {
                                    h264 = new[]
                                    {
                                        new { width = 720, height = 1280, masterUrl = Origin, backupUrls = new[] { "https://xhscdn.com.evil.test/bad.mp4" } },
                                        new { width = 1080, height = 1920, masterUrl = Master, backupUrls = new[] { Master, Backup } }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }) + ";</script>";

    private sealed class Fixture(string mode) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            Started.TrySetResult();
            if (mode is "cancel" or "all-timeout" || (mode == "origin-timeout" && url == Origin))
                await Task.Delay(Timeout.Infinite, token);
            var status = mode == "all-partial" || (mode == "partial-fallback" && url == Master) ? HttpStatusCode.PartialContent :
                mode == "all-403" || url == Origin || (mode == "master-403" && url == Master) ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
            return new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent([]) };
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
