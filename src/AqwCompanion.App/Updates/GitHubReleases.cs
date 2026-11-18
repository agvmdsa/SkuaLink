using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace AqwCompanion.App.Updates;

public enum ReleaseCheckStatus { Found, NotReady, NotModified, Failed }

public sealed record ReleaseCheck(ReleaseCheckStatus Status, ReleaseInfo? Release = null, string? ETag = null);

public interface IGitHubReleases
{
    /// <summary>The latest non-draft, non-prerelease release. <paramref name="etag"/> is the one of the last answer. Throws only when <paramref name="ct"/> is cancelled.</summary>
    Task<ReleaseCheck> GetLatestAsync(string? etag, CancellationToken ct);

    /// <summary>The release tagged exactly <c>v{version}</c> (the plugin of the app's own version). Same rules.</summary>
    Task<ReleaseCheck> GetByVersionAsync(ReleaseVersion version, CancellationToken ct);
}

public sealed partial class GitHubReleases(HttpClient http, string appVersion, TimeSpan? timeout = null) : IGitHubReleases
{
    /// <summary>The repository is a constant, never a setting.</summary>
    public const string Repository = "agvmdsa/SkuaLink";

    public const int MaxBodyBytes = 256 * 1024;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly Uri LatestUrl = new($"https://api.github.com/repos/{Repository}/releases/latest");

    [GeneratedRegex(@"\A[0-9A-Za-z.\-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeToken();

    private readonly string _agentVersion = SafeToken().IsMatch(appVersion) ? appVersion : "0.0.0";

    public Task<ReleaseCheck> GetLatestAsync(string? etag, CancellationToken ct) => GetAsync(LatestUrl, etag, null, ct);

    public Task<ReleaseCheck> GetByVersionAsync(ReleaseVersion version, CancellationToken ct) =>
        GetAsync(new Uri($"https://api.github.com/repos/{Repository}/releases/tags/{version.Tag}"), null, version, ct);

    private async Task<ReleaseCheck> GetAsync(Uri url, string? etag, ReleaseVersion? expected, CancellationToken ct)
    {
        var failed = new ReleaseCheck(ReleaseCheckStatus.Failed);
        if (!UpdateUrlPolicy.IsAllowedApi(url))
        {
            return failed;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SkuaLink", _agentVersion));
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new ReleaseCheck(ReleaseCheckStatus.NotModified);
            }

            if (response.StatusCode != HttpStatusCode.OK || await ReadBoundedAsync(response.Content, deadline.Token) is not { } body)
            {
                return failed;
            }

            var parsed = ReleaseParser.Parse(body);
            var newTag = response.Headers.ETag?.ToString();
            return parsed.Status switch
            {
                ReleaseParseStatus.Ready when expected is { } want && parsed.Release!.Version != want => failed,
                ReleaseParseStatus.Ready => new ReleaseCheck(ReleaseCheckStatus.Found, parsed.Release, newTag),
                ReleaseParseStatus.NotReady => new ReleaseCheck(ReleaseCheckStatus.NotReady, null, newTag),
                _ => failed,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException or InvalidDataException)
        {
            return failed;
        }
    }

    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaxBodyBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
