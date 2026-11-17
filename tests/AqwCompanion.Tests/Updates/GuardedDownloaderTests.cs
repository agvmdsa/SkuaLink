using System.Net;
using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class GuardedDownloaderTests : IDisposable
{
    private const string Asset = "https://github.com/agvmdsa/SkuaLink/releases/download/v0.8.0/SkuaLink-Setup-v0.8.0.exe";
    private const string Cdn = "https://objects.githubusercontent.com/github-production-release-asset/42?sig=abc";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _payload = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();

    public GuardedDownloaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Target => Path.Combine(_dir, "out.bin");

    private GuardedDownloader Downloader(StubHttpHandler handler, TimeSpan? timeout = null) => new(new HttpClient(handler), timeout);

    private sealed class Sync(List<DownloadProgress> seen) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => seen.Add(value);
    }

    [Fact]
    public async Task A_download_writes_the_bytes_and_reports_progress_up_to_the_total()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload));
        var seen = new List<DownloadProgress>();

        var status = await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, new Sync(seen), default);

        Assert.Equal(DownloadStatus.Ok, status);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target));
        Assert.NotEmpty(seen);
        Assert.Equal(new DownloadProgress(_payload.Length, _payload.Length), seen[^1]);
        Assert.All(seen, p => Assert.Equal(_payload.Length, p.Total));
    }

    [Fact]
    public async Task A_redirect_to_the_asset_host_is_followed()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "github.com" ? StubHttpHandler.Redirect(Cdn) : StubHttpHandler.Bytes(_payload));

        var status = await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default);

        Assert.Equal(DownloadStatus.Ok, status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri(Cdn), handler.Requests[1].Uri);
    }

    [Fact]
    public async Task A_relative_redirect_is_resolved_against_the_current_address()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/moved", StringComparison.Ordinal)
            ? StubHttpHandler.Bytes(_payload)
            : StubHttpHandler.Redirect("/agvmdsa/moved", HttpStatusCode.TemporaryRedirect));

        Assert.Equal(DownloadStatus.Ok, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.Equal(new Uri("https://github.com/agvmdsa/moved"), handler.Requests[1].Uri);
    }

    [Theory]
    [InlineData(5, DownloadStatus.Ok)]
    [InlineData(6, DownloadStatus.Refused)]
    public async Task At_most_five_redirects_are_followed(int redirects, DownloadStatus expected)
    {
        var hops = 0;
        var handler = new StubHttpHandler(_ => hops++ < redirects ? StubHttpHandler.Redirect(Cdn) : StubHttpHandler.Bytes(_payload));

        Assert.Equal(expected, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.Equal(expected == DownloadStatus.Ok, File.Exists(Target));
    }

    [Fact]
    public async Task A_redirect_to_a_foreign_host_on_the_third_hop_is_refused_before_it_is_requested()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host switch
        {
            "github.com" => StubHttpHandler.Redirect(Cdn),
            "objects.githubusercontent.com" when r.RequestUri.Query.Contains("hop=2", StringComparison.Ordinal) => StubHttpHandler.Redirect("https://evil.example/x.exe"),
            "objects.githubusercontent.com" => StubHttpHandler.Redirect(Cdn + "&hop=2"),
            _ => StubHttpHandler.Bytes(_payload),
        });

        var status = await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default);

        Assert.Equal(DownloadStatus.Refused, status);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Host == "evil.example");
        Assert.False(File.Exists(Target));
    }

    [Theory]
    [InlineData("http://github.com/x.exe")]
    [InlineData("https://evil.example/x.exe")]
    [InlineData("https://github.com:8443/x.exe")]
    public async Task A_start_address_outside_the_policy_is_refused_without_a_request(string url)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload));

        Assert.Equal(DownloadStatus.Refused, await Downloader(handler).DownloadAsync(new Uri(url), Target, 1_000_000, null, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_redirect_without_a_location_or_a_non_200_final_answer_fails()
    {
        Assert.Equal(DownloadStatus.Failed, await Downloader(new StubHttpHandler(_ => StubHttpHandler.Status(HttpStatusCode.Found))).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.Equal(DownloadStatus.Failed, await Downloader(new StubHttpHandler(_ => StubHttpHandler.Status(HttpStatusCode.NotFound))).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.Equal(DownloadStatus.Failed, await Downloader(new StubHttpHandler(_ => StubHttpHandler.Status(HttpStatusCode.PartialContent))).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_is_too_large_before_anything_is_written()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload));

        Assert.Equal(DownloadStatus.TooLarge, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, _payload.Length - 1, null, default));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task A_body_longer_than_it_declared_is_stopped_while_reading()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload, declaredLength: 1000));

        Assert.Equal(DownloadStatus.TooLarge, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task A_body_shorter_than_it_declared_fails()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload, declaredLength: _payload.Length + 10));

        Assert.Equal(DownloadStatus.Failed, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task An_answer_without_a_content_length_fails()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload, unsized: true));

        Assert.Equal(DownloadStatus.Failed, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
    }

    [Fact]
    public async Task The_deadline_stops_a_stalled_download()
    {
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpHandler.Bytes(_payload);
        });

        Assert.Equal(DownloadStatus.TimedOut, await Downloader(handler, TimeSpan.FromMilliseconds(50)).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.Equal(TimeSpan.FromMinutes(5), GuardedDownloader.DefaultTimeout);
    }

    [Fact]
    public async Task A_cancel_from_the_caller_is_cancelled_not_timed_out()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpHandler.Bytes(_payload);
        });

        Assert.Equal(DownloadStatus.Cancelled, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, cts.Token));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task A_network_error_fails_and_leaves_no_file()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("offline"));

        Assert.Equal(DownloadStatus.Failed, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task An_existing_destination_is_never_overwritten()
    {
        await File.WriteAllTextAsync(Target, "keep");
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(_payload));

        Assert.Equal(DownloadStatus.Failed, await Downloader(handler).DownloadAsync(new Uri(Asset), Target, 1_000_000, null, default));
        Assert.Equal("keep", await File.ReadAllTextAsync(Target));
    }
}
