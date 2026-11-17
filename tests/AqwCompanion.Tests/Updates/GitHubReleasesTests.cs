using System.Net;
using System.Net.Http.Headers;
using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class GitHubReleasesTests
{
    private const string Base = "https://github.com/agvmdsa/SkuaLink/releases/download/v0.8.0/";

    private static string ReadyJson(string tag = "v0.8.0") =>
        $$"""
        {"tag_name":"{{tag}}","draft":false,"prerelease":false,"assets":[
        {"name":"SkuaLink-Setup-{{tag}}.exe","size":1000,"browser_download_url":"{{Base}}SkuaLink-Setup-{{tag}}.exe"},
        {"name":"SkuaLink.SkuaBridge.dll","size":100,"browser_download_url":"{{Base}}SkuaLink.SkuaBridge.dll"},
        {"name":"SHA256SUMS.txt","size":200,"browser_download_url":"{{Base}}SHA256SUMS.txt"}]}
        """;

    private static GitHubReleases Client(StubHttpHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), "0.7.0", timeout);

    [Fact]
    public async Task The_latest_request_is_anonymous_https_with_the_fixed_headers_and_no_etag_at_first()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Ok(ReadyJson()));

        await Client(handler).GetLatestAsync(null, CancellationToken.None);

        var seen = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.github.com/repos/agvmdsa/SkuaLink/releases/latest"), seen.Uri);
        Assert.Equal("application/vnd.github+json", seen.Accept);
        Assert.Equal("SkuaLink/0.7.0", seen.UserAgent);
        Assert.Null(seen.IfNoneMatch);
    }

    [Fact]
    public async Task A_known_etag_is_sent_and_a_new_one_is_returned()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Ok(ReadyJson());
            response.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
            return response;
        });

        var result = await Client(handler).GetLatestAsync("\"old\"", CancellationToken.None);

        Assert.Equal("\"old\"", Assert.Single(handler.Requests).IfNoneMatch);
        Assert.Equal(ReleaseCheckStatus.Found, result.Status);
        Assert.Equal("\"abc\"", result.ETag);
        Assert.Equal(new ReleaseVersion(0, 8, 0), result.Release!.Version);
    }

    [Fact]
    public async Task A_304_keeps_the_known_state()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Status(HttpStatusCode.NotModified));

        var result = await Client(handler).GetLatestAsync("\"old\"", CancellationToken.None);

        Assert.Equal(ReleaseCheckStatus.NotModified, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task A_release_without_its_assets_is_not_ready_but_keeps_its_etag()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Ok("""{"tag_name":"v0.8.0","draft":false,"prerelease":false,"assets":[]}""");
            response.Headers.ETag = new EntityTagHeaderValue("\"e\"");
            return response;
        });

        var result = await Client(handler).GetLatestAsync(null, CancellationToken.None);

        Assert.Equal(ReleaseCheckStatus.NotReady, result.Status);
        Assert.Equal("\"e\"", result.ETag);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task Any_other_status_including_a_redirect_fails(HttpStatusCode code)
    {
        var handler = new StubHttpHandler(_ => code is HttpStatusCode.Found or HttpStatusCode.MovedPermanently
            ? StubHttpHandler.Redirect("https://evil.example/", code)
            : StubHttpHandler.Status(code));

        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(handler).GetLatestAsync(null, CancellationToken.None)).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Invalid_json_and_a_bad_tag_fail()
    {
        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(new StubHttpHandler(_ => StubHttpHandler.Ok("not json"))).GetLatestAsync(null, default)).Status);
        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(new StubHttpHandler(_ => StubHttpHandler.Ok(ReadyJson("vX")))).GetLatestAsync(null, default)).Status);
    }

    [Fact]
    public async Task A_body_over_256_KB_fails_whether_declared_or_not()
    {
        var big = new byte[GitHubReleases.MaxBodyBytes + 1];
        Array.Fill(big, (byte)' ');

        var declared = new StubHttpHandler(_ => StubHttpHandler.Bytes(big));
        var unsized = new StubHttpHandler(_ => StubHttpHandler.Bytes(big, unsized: true));

        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(declared).GetLatestAsync(null, default)).Status);
        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(unsized).GetLatestAsync(null, default)).Status);
    }

    [Fact]
    public async Task A_network_error_fails_quietly()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("offline"));

        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(handler).GetLatestAsync(null, default)).Status);
    }

    [Fact]
    public async Task The_timeout_fails_and_the_request_deadline_is_15_seconds()
    {
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpHandler.Ok("");
        });

        Assert.Equal(ReleaseCheckStatus.Failed, (await Client(handler, TimeSpan.FromMilliseconds(50)).GetLatestAsync(null, default)).Status);
        Assert.Equal(TimeSpan.FromSeconds(15), GitHubReleases.RequestTimeout);
    }

    [Fact]
    public async Task A_cancel_from_the_caller_is_not_a_failure()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpHandler.Ok("");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetLatestAsync(null, cts.Token));
    }

    [Fact]
    public async Task A_release_by_version_uses_the_tag_address_and_must_be_that_version()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Ok(ReadyJson("v0.7.0")));
        var client = Client(handler);

        var same = await client.GetByVersionAsync(new ReleaseVersion(0, 7, 0), default);
        var other = await client.GetByVersionAsync(new ReleaseVersion(0, 6, 0), default);

        Assert.Equal(new Uri("https://api.github.com/repos/agvmdsa/SkuaLink/releases/tags/v0.7.0"), handler.Requests[0].Uri);
        Assert.Equal(ReleaseCheckStatus.Found, same.Status);
        Assert.Equal(ReleaseCheckStatus.Failed, other.Status);
        Assert.Null(handler.Requests[0].IfNoneMatch);
    }

    [Fact]
    public async Task A_user_agent_with_odd_characters_falls_back_to_a_plain_version()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Ok(ReadyJson()));

        await new GitHubReleases(new HttpClient(handler), "bad value!\r\n").GetLatestAsync(null, default);

        Assert.Equal("SkuaLink/0.0.0", Assert.Single(handler.Requests).UserAgent);
    }
}
