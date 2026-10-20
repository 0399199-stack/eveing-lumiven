using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumiven.Services;

public sealed record AppUpdate(Version Version, string Tag, string FileName, Uri DownloadUrl,
    long Size, string Sha256)
{
    public Uri ReleaseUrl => new($"https://github.com/{UpdateService.Repository}/releases/tag/{Tag}");
    public string ReleaseNotes { get; init; } = "此版本暂未提供更新说明，可打开 GitHub 查看。";
}

public sealed class UpdateService : IDisposable
{
    public const string Repository = "0399199-stack/eveing-lumiven";
    public static Version CurrentVersion { get; } = GetCurrentVersion();
    private readonly HttpClient _http;
    private readonly string _directory;

    public UpdateService(string dataDirectory, HttpMessageHandler? handler = null)
    {
        _directory = Path.Combine(dataDirectory, "updates");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EVEING-Lumiven/" + CurrentVersion);
    }

    private static Version GetCurrentVersion()
    {
        var version = typeof(UpdateService).Assembly.GetName().Version!;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    public async Task<AppUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        var json = await ReadTextAsync(new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=100"), 2_000_000, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        JsonElement? selected = null;
        Version? newest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var match = Regex.Match(tag, @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?$");
            if (!match.Success || !Version.TryParse($"{match.Groups[1].Value}.{match.Groups[2].Value}.{(match.Groups[3].Success ? match.Groups[3].Value : "0")}", out var version)) continue;
            if (version <= currentVersion || (newest is not null && version <= newest)) continue;
            newest = version;
            selected = release;
        }
        if (selected is null) return null;
        var item = selected.Value;
        var releaseTag = item.GetProperty("tag_name").GetString()!;
        var name = $"EVEING-Lumiven-Setup-{newest!.ToString(3)}-x64.exe";
        var assets = item.GetProperty("assets").EnumerateArray().ToArray();
        var installer = RequireAsset(assets, name);
        var manifest = RequireAsset(assets, "SHA256SUMS");
        var size = installer.GetProperty("size").GetInt64();
        if (size <= 0 || size > 512L * 1024 * 1024) throw new InvalidDataException("更新安装包大小无效。");
        var url = AssetUrl(installer, releaseTag, name);
        var sums = await ReadTextAsync(AssetUrl(manifest, releaseTag, "SHA256SUMS"), 128 * 1024, token).ConfigureAwait(false);
        var hashes = sums.Split('\n').Select(line => Regex.Match(line.TrimEnd('\r'), @"^([0-9a-fA-F]{64})  (.+)$"))
            .Where(match => match.Success && match.Groups[2].Value == name).ToArray();
        if (hashes.Length != 1) throw new InvalidDataException("更新缺少唯一的 SHA-256 校验值。");
        var hash = hashes[0].Groups[1].Value.ToLowerInvariant();
        if (installer.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
            !string.Equals(digest.GetString(), "sha256:" + hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub 校验值与发布文件不一致。");
        var update = new AppUpdate(newest, releaseTag, name, url, size, hash);
        if (item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String &&
            body.GetString()?.Trim() is { Length: > 0 } notes)
        {
            update = update with
            {
                ReleaseNotes = notes.Length <= 12_000 ? notes :
                    notes[..12_000] + "\n\n…完整更新说明请打开 GitHub 查看。"
            };
        }
        return update;
    }

    private static JsonElement RequireAsset(JsonElement[] assets, string name)
    {
        var matching = assets.Where(a => a.GetProperty("name").GetString() == name &&
            a.GetProperty("state").GetString() == "uploaded").ToArray();
        if (matching.Length != 1) throw new InvalidDataException("新版本尚未提供完整的安装包和校验文件。");
        return matching[0];
    }

    private static Uri AssetUrl(JsonElement asset, string tag, string name)
    {
        var expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        if (asset.GetProperty("browser_download_url").GetString() != expected)
            throw new InvalidDataException("更新文件地址不属于本项目的 GitHub Release。");
        return new Uri(expected);
    }

    public async Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var token = timeout.Token;
        var path = InstallerPath(update);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && await MatchesAsync(update, path, token).ConfigureAwait(false))
        {
            progress?.Report(100);
            return path;
        }
        var partial = path + ".part";
        try
        {
            using var response = await GetAsync(update.DownloadUrl, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != update.Size)
                throw new InvalidDataException("更新安装包长度与发布信息不一致。");
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                var timer = Stopwatch.StartNew();
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > update.Size) throw new InvalidDataException("更新安装包超过预期大小。");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count);
                    if (timer.ElapsedMilliseconds >= 250) { progress?.Report(100d * total / update.Size); timer.Restart(); }
                }
                if (total != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新文件不完整或校验失败，请重新下载。");
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, path, true);
            progress?.Report(100);
            return path;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    public async Task VerifyInstallerAsync(AppUpdate update, string path, CancellationToken token)
    {
        if (!string.Equals(path, InstallerPath(update), StringComparison.OrdinalIgnoreCase) ||
            !await MatchesAsync(update, path, token).ConfigureAwait(false))
            throw new InvalidDataException("安装包已变化或不完整，请重新检查并下载更新。");
    }

    private string InstallerPath(AppUpdate update) => Path.Combine(_directory, update.Version.ToString(3), update.FileName);

    private static async Task<bool> MatchesAsync(AppUpdate update, string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return stream.Length == update.Size && Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false))
            .Equals(update.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ReadTextAsync(Uri uri, int maximumBytes, CancellationToken token)
    {
        using var response = await GetAsync(uri, token).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("更新信息大小异常。");
            output.Write(buffer, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 ||
                uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("更新请求跳转到了不受支持的地址。");
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("更新下载跳转地址缺失。");
                uri = new Uri(uri, location);
                continue;
            }
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException(status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                ? "GitHub 暂时限制了请求，请稍后重试。" : "无法连接 GitHub 更新服务，请稍后重试。", null, status);
        }
        throw new InvalidDataException("更新下载跳转次数过多。");
    }

    public void Dispose() => _http.Dispose();
}
