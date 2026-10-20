using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lumiven.Models;
using Lumiven.Services;

internal static class UpdateTests
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lumiven-update-test-" + Guid.NewGuid().ToString("N"));
        var bytes = new byte[] { 77, 90, 1, 2, 3, 4, 5, 6 };
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        const string name = "EVEING-Lumiven-Setup-1.10.0-x64.exe";
        const string prefix = "https://github.com/" + UpdateService.Repository + "/releases/download/v1.10.0/";
        var installer = new { name, state = "uploaded", size = bytes.Length, digest = "sha256:" + hash, browser_download_url = prefix + name };
        var sums = new { name = "SHA256SUMS", state = "uploaded", size = 120, browser_download_url = prefix + "SHA256SUMS" };
        object Release(string tag, bool draft = false, bool prerelease = false, object[]? assets = null, object? body = null) =>
            new { tag_name = tag, draft, prerelease, assets = assets ?? new object[] { installer, sums }, body };
        const string notes = "## 新增功能\n- 抖音视频下载\n- 小红书视频下载\n\n修复：下载失败提示。";
        var releases = JsonSerializer.Serialize(new[] { Release("v9.0.0", prerelease: true), Release("v8.0.0", draft: true),
            Release("v1.2.0-beta"), Release("v1.9.0"), Release("v1.10.0", body: notes), Release("V1.1") });
        var manifest = hash + "  " + name + "\n";
        int downloads = 0;
        var corrupt = false;
        var redirectHost = "release-assets.githubusercontent.com";
        var cancelDownload = false;
        var truncated = false;
        using var handler = new FixtureHandler((uri, token) =>
        {
            if (uri.Host == "api.github.com") return Task.FromResult(Text(releases));
            if (uri.AbsolutePath.EndsWith("SHA256SUMS")) return Task.FromResult(Text(manifest));
            if (uri.Host == "github.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri($"https://{redirectHost}/fixture.exe");
                return Task.FromResult(redirect);
            }
            downloads++;
            HttpContent content = cancelDownload ? new StreamContent(new InterruptedStream(bytes)) :
                new ByteArrayContent(truncated ? bytes[..3] : corrupt ? new byte[bytes.Length] : bytes);
            content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var service = new UpdateService(root, handler);
        var update = await service.CheckAsync(new Version(1, 9, 0), CancellationToken.None);
        Check(update?.Version == new Version(1, 10, 0), "Versions must compare numerically and skip drafts/prereleases");
        Check(update!.ReleaseNotes == notes, "Selected release notes must preserve Chinese text and newlines");
        Check(await service.CheckAsync(new Version(1, 10, 0), CancellationToken.None) is null, "Equal or older versions must not update");
        var path = await service.DownloadAsync(update!, null, CancellationToken.None);
        Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Downloaded installer bytes must match");
        await service.VerifyInstallerAsync(update!, path, CancellationToken.None);
        await service.DownloadAsync(update!, null, CancellationToken.None);
        Check(downloads == 1, "Verified cache must avoid a second download");
        await File.WriteAllBytesAsync(path, new byte[bytes.Length]);
        await ExpectAsync<InvalidDataException>(() => service.VerifyInstallerAsync(update!, path, CancellationToken.None));
        await service.DownloadAsync(update!, null, CancellationToken.None);
        Check(downloads == 2 && File.ReadAllBytes(path).SequenceEqual(bytes), "Tampered cache must be redownloaded");
        File.Delete(path);
        corrupt = true;
        await ExpectAsync<InvalidDataException>(() => service.DownloadAsync(update!, null, CancellationToken.None));
        Check(!File.Exists(path) && !File.Exists(path + ".part"), "Corrupt data must not be installable or leave partial data");
        corrupt = false;
        truncated = true;
        await ExpectAsync<InvalidDataException>(() => service.DownloadAsync(update!, null, CancellationToken.None));
        Check(!File.Exists(path) && !File.Exists(path + ".part"), "Truncated data must not become an installer");
        truncated = false;
        redirectHost = "example.com";
        await ExpectAsync<InvalidDataException>(() => service.DownloadAsync(update!, null, CancellationToken.None));
        redirectHost = "release-assets.githubusercontent.com";
        cancelDownload = true;
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await ExpectAsync<OperationCanceledException>(() => service.DownloadAsync(update!, null, cancel.Token));
        Check(!File.Exists(path + ".part"), "Cancelled downloads must not leave partial files");
        cancelDownload = false;
        await service.DownloadAsync(update!, null, CancellationToken.None);

        manifest = new string('0', 64) + "  " + name;
        await ExpectAsync<InvalidDataException>(() => service.CheckAsync(new Version(1, 1, 0), CancellationToken.None));
        manifest = hash + "  " + name + "\n";
        releases = JsonSerializer.Serialize(new[] { Release("v1.10.0", assets: new object[] { installer }) });
        await ExpectAsync<InvalidDataException>(() => service.CheckAsync(new Version(1, 1, 0), CancellationToken.None));
        var external = new { name, state = "uploaded", size = bytes.Length, browser_download_url = "https://example.com/file.exe" };
        releases = JsonSerializer.Serialize(new[] { Release("v1.10.0", assets: new object[] { external, sums }) });
        await ExpectAsync<InvalidDataException>(() => service.CheckAsync(new Version(1, 1, 0), CancellationToken.None));
        releases = JsonSerializer.Serialize(new[] { Release("V1.1") });
        Check(await service.CheckAsync(new Version(1, 1, 0), CancellationToken.None) is null, "V1.1 must normalize to 1.1.0");
        foreach (var body in new object?[] { null, " \r\n ", 123 })
        {
            releases = JsonSerializer.Serialize(new[] { Release("v1.10.0", body: body) });
            Check((await service.CheckAsync(new Version(1, 1, 0), CancellationToken.None))!.ReleaseNotes.Contains("暂未提供"),
                "Missing or unusable notes must show a clear fallback");
        }
        releases = JsonSerializer.Serialize(new[] { Release("v1.10.0", body: new string('字', 12_001)) });
        var longNotes = (await service.CheckAsync(new Version(1, 1, 0), CancellationToken.None))!.ReleaseNotes;
        Check(longNotes.Length < 12_100 && longNotes.EndsWith("GitHub 查看。"), "Long notes must remain bounded with a full-notes link hint");
        releases = "[]";
        Check(await service.CheckAsync(new Version(1, 1, 0), CancellationToken.None) is null, "No releases must be a normal result");
        using var limited = new UpdateService(root, new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        await ExpectAsync<HttpRequestException>(() => limited.CheckAsync(new Version(1, 1, 0), CancellationToken.None));
        Check(JsonSerializer.Deserialize<AppSettings>("{}", AppServices.JsonOptions)!.AutomaticUpdates, "Existing settings must enable automatic updates by default");
        var settings = new AppSettings { AutomaticUpdates = false };
        Check(!JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, AppServices.JsonOptions), AppServices.JsonOptions)!.AutomaticUpdates,
            "Disabling automatic updates must survive restart");
        Console.WriteLine("Update tests passed: stable version selection, GitHub URLs, SHA-256, cache, corruption, cancellation, retry, rate limit and settings.");
    }

    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class FixtureHandler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request.RequestUri!, cancellationToken);
    }

    private sealed class InterruptedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == 0) return await base.ReadAsync(buffer[..2], cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
