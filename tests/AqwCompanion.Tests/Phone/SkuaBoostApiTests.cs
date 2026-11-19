using System.Globalization;
using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public sealed class SkuaBoostApiTests : IDisposable
{
    private const string GoodHost = "127.0.0.1:47822";
    private const string Json = "application/json";
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(T0);
    private readonly PhoneRegistry _registry;
    private readonly PhoneHub _hub;
    private readonly FakeSkua _skua = new();
    private readonly FakeBoosts _boosts = new();
    private readonly PhoneHandler _handler;
    private readonly string _token;

    public SkuaBoostApiTests()
    {
        _registry = new PhoneRegistry(new MemoryPhoneFileStore(), _clock.GetUtcNow);
        _hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), _clock.GetUtcNow);
        _handler = Handler(_boosts);
        _token = _registry.TryPair(_registry.CreatePairingToken(), "Phone").DeviceToken!;
        _skua.Current = new SkuaHubView(
        [
            FakeSkua.Instance("a1", "Hero", status: new SkuaStatus(true, "Hero", null, null, null, false, null)),
            FakeSkua.Instance("b2", "Zed", disconnected: true),
            FakeSkua.Instance("c3", "Other", status: new SkuaStatus(true, "Other", null, null, null, false, null)),
        ]);
    }

    public void Dispose() => _hub.Dispose();

    private PhoneHandler Handler(ISkuaBoosts? boosts)
    {
        var api = new SkuaApi(
            _skua, _skua, new SkuaScriptPrefs(new MemorySkuaScriptFile(), _clock.GetUtcNow), new SkuaOptionCache(new MemorySkuaScriptFile(), _clock.GetUtcNow),
            CultureInfo.InvariantCulture, null, boosts);
        return new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9", api);
    }

    private PhoneRequest Request(string method, string path, string body, string? contentType, bool cookie = true, bool write = true, string? origin = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = GoodHost };
        if (cookie)
        {
            headers["Cookie"] = $"qkp_device={_token}";
        }

        if (write)
        {
            headers["X-Remote"] = "1";
        }

        if (contentType is not null)
        {
            headers["Content-Type"] = contentType;
        }

        if (origin is not null)
        {
            headers["Origin"] = origin;
        }

        return new PhoneRequest(method, path, headers, body);
    }

    private PhoneResponse Post(string path, string body = "{}", string? contentType = Json, bool cookie = true, bool write = true, string? origin = null) =>
        _handler.Handle(Request("POST", path, body, contentType, cookie, write, origin));

    private PhoneResponse Use(string body, string id = "a1") => Post($"/api/skua/{id}/boosts/use", body);

    private static string Text(PhoneResponse response) => Encoding.UTF8.GetString(response.Body);

    private static string ErrorOf(PhoneResponse response)
    {
        using var doc = JsonDocument.Parse(Text(response));
        Assert.Single(doc.RootElement.EnumerateObject());
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    private static void AssertOk(PhoneResponse response)
    {
        Assert.Equal(200, response.Status);
        Assert.Equal("{\"ok\":true}", Text(response));
    }

    [Theory]
    [InlineData("/api/skua/a1/boosts/use", "{\"itemId\":17439}")]
    [InlineData("/api/skua/a1/relogin", "{}")]
    public void Both_routes_need_the_cookie_the_write_header_and_a_same_origin_request(string path, string body)
    {
        Assert.Equal(401, Post(path, body, cookie: false).Status);
        Assert.Equal(403, Post(path, body, write: false).Status);
        Assert.Equal(403, Post(path, body, origin: "https://evil.example").Status);
        Assert.True(Post(path.ToUpperInvariant(), body).Status >= 400);   // another case is not an API path: never answered by the API
        Assert.Equal(401, Post(path + "/", body, cookie: false).Status);
        Assert.Equal(401, Post(path.Replace("boosts", "%62oosts"), body, cookie: false).Status);
        Assert.Empty(_boosts.Used);
        Assert.Empty(_boosts.Relogged);
    }

    [Fact]
    public void Head_and_options_cannot_reach_the_routes_and_a_method_override_header_is_ignored()
    {
        foreach (var method in new[] { "HEAD", "OPTIONS", "PUT", "DELETE", "PATCH" })
        {
            Assert.Equal(405, _handler.Handle(Request(method, "/api/skua/a1/boosts/use", "{\"itemId\":1}", Json)).Status);
            Assert.Equal(405, _handler.Handle(Request(method, "/api/skua/a1/relogin", "{}", Json)).Status);
        }

        var plain = Request("GET", "/api/skua/a1/boosts/use", "{\"itemId\":1}", Json);
        var headers = plain.Headers.ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);
        headers["X-HTTP-Method-Override"] = "POST";
        Assert.Equal(405, _handler.Handle(plain with { Headers = headers }).Status);
        Assert.Empty(_boosts.Used);
        Assert.Empty(_boosts.Relogged);
    }

    [Fact]
    public void A_use_needs_a_json_content_type_and_a_relogin_may_be_empty_but_not_another_type()
    {
        Assert.Equal(415, Post("/api/skua/a1/boosts/use", "{\"itemId\":1}", "text/plain").Status);
        Assert.Equal(415, Post("/api/skua/a1/boosts/use", "", null).Status);
        Assert.Equal(415, Post("/api/skua/a1/relogin", "{}", "text/plain").Status);
        AssertOk(Post("/api/skua/a1/relogin", "", null));
        Assert.Empty(_boosts.Used);
    }

    [Fact]
    public void A_use_reaches_the_service_with_the_instance_and_the_item_id_only()
    {
        AssertOk(Use("{\"itemId\":17439}"));

        Assert.Equal([("a1", 17439)], _boosts.Used);
        Assert.Empty(_skua.Sent);   // the API itself sends no command to Skua: the service does
    }

    [Theory]
    [InlineData("a1", 200)]
    [InlineData("b2", 409)]
    [InlineData("zz", 404)]
    public void An_unknown_instance_is_404_and_a_disconnected_one_409_for_both_routes(string id, int status)
    {
        Assert.Equal(status, Use("{\"itemId\":5}", id).Status);
        Assert.Equal(status, Post($"/api/skua/{id}/relogin").Status);
        if (status == 404)
        {
            Assert.Equal("unknown Skua", ErrorOf(Use("{\"itemId\":5}", id)));
        }

        if (status == 409)
        {
            Assert.Equal("Skua is disconnected", ErrorOf(Use("{\"itemId\":5}", id)));
        }

        if (status != 200)
        {
            Assert.Empty(_boosts.Used);
            Assert.Empty(_boosts.Relogged);
        }
    }

    [Fact]
    public void An_unknown_instance_text_never_echoes_the_client_text()
    {
        var response = Use("{\"itemId\":5}", "%3Cscript%3E");
        Assert.Equal(404, response.Status);
        Assert.DoesNotContain("script", Text(response));
    }

    [Fact]
    public void Without_a_boost_service_both_routes_are_404()
    {
        var handler = Handler(null);
        var use = handler.Handle(Request("POST", "/api/skua/a1/boosts/use", "{\"itemId\":5}", Json));
        Assert.Equal(404, use.Status);
        Assert.Equal("not found", ErrorOf(use));
        Assert.Equal(404, handler.Handle(Request("POST", "/api/skua/a1/relogin", "{}", Json)).Status);
    }

    [Fact]
    public void A_GET_is_405_and_an_unknown_path_under_boosts_is_404()
    {
        Assert.Equal(405, _handler.Handle(Request("GET", "/api/skua/a1/boosts/use", "", null, write: false)).Status);
        Assert.Equal(405, _handler.Handle(Request("GET", "/api/skua/a1/relogin", "", null, write: false)).Status);
        Assert.Equal(404, Post("/api/skua/a1/boosts/other").Status);
        Assert.Equal(404, Post("/api/skua/a1/boosts").Status);
        Assert.Equal(404, Post("/api/skua/a1/boosts/use/x").Status);
        Assert.Equal(404, Post("/api/skua/a1/relogin/x").Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"itemId\":0}")]
    [InlineData("{\"itemId\":-1}")]
    [InlineData("{\"itemId\":1.5}")]
    [InlineData("{\"itemId\":\"7\"}")]
    [InlineData("{\"itemId\":1e400}")]
    [InlineData("{\"itemId\":9999999999}")]
    [InlineData("{\"itemId\":null}")]
    [InlineData("{\"itemId\":[7]}")]
    [InlineData("{\"itemId\":{\"a\":1}}")]
    [InlineData("{\"itemId\":true}")]
    [InlineData("[]")]
    [InlineData("7")]
    [InlineData("\"7\"")]
    [InlineData("null")]
    [InlineData("{\"itemId\":")]
    public void A_bad_item_id_is_400_with_a_constant_text_and_nothing_is_sent(string body)
    {
        var response = Use(body);

        Assert.Equal(400, response.Status);
        Assert.Equal("invalid body", ErrorOf(response));
        Assert.Empty(_boosts.Used);
    }

    [Fact]
    public void A_hostile_item_id_text_is_never_echoed()
    {
        var response = Use("{\"itemId\":\"<img src=x onerror=alert(1)>\"}");
        Assert.Equal(400, response.Status);
        Assert.DoesNotContain("img", Text(response));
    }

    [Fact]
    public void Extra_fields_are_ignored_and_never_forwarded_and_the_largest_id_is_accepted()
    {
        AssertOk(Use("{\"itemId\":2147483647,\"name\":\"GOLD\",\"quantity\":99,\"type\":\"gold\"}"));

        Assert.Equal([("a1", int.MaxValue)], _boosts.Used);
    }

    [Fact]
    public void A_body_over_1_KB_is_413_and_a_relogin_body_too()
    {
        var big = "{\"itemId\":5,\"pad\":\"" + new string('x', 1100) + "\"}";
        var response = Use(big);
        Assert.Equal(413, response.Status);
        Assert.Equal("body too large", ErrorOf(response));
        Assert.Equal(413, Post("/api/skua/a1/relogin", big).Status);
        Assert.Empty(_boosts.Used);
        Assert.Empty(_boosts.Relogged);
    }

    [Fact]
    public void A_refusal_is_409_with_the_text_and_no_answer_is_504()
    {
        _boosts.UseResult = BoostResult.Refused("A gold boost is already active.");
        var refused = Use("{\"itemId\":17439}");
        Assert.Equal(409, refused.Status);
        Assert.Equal("A gold boost is already active.", ErrorOf(refused));

        _boosts.UseResult = BoostResult.Refused("The game did not activate the boost.");
        Assert.Equal("The game did not activate the boost.", ErrorOf(Use("{\"itemId\":17439}")));

        _boosts.UseResult = BoostResult.Refused("  ");
        var blank = Use("{\"itemId\":17439}");
        Assert.Equal(409, blank.Status);
        Assert.Equal("Skua refused the command", ErrorOf(blank));

        _boosts.UseResult = BoostResult.Unanswered;
        var none = Use("{\"itemId\":17439}");
        Assert.Equal(504, none.Status);
        Assert.Equal("Skua did not answer", ErrorOf(none));

        _boosts.ReloginResult = BoostResult.Refused("Wait a moment before trying again.");
        var relogin = Post("/api/skua/a1/relogin");
        Assert.Equal(409, relogin.Status);
        Assert.Equal("Wait a moment before trying again.", ErrorOf(relogin));
        _boosts.ReloginResult = BoostResult.Unanswered;
        Assert.Equal(504, Post("/api/skua/a1/relogin").Status);
    }

    [Fact]
    public async Task A_second_command_for_the_same_instance_while_one_waits_is_409_busy()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _boosts.UseHold = hold.Task;
        var first = _handler.HandleAsync(Request("POST", "/api/skua/a1/boosts/use", "{\"itemId\":17439}", Json), CancellationToken.None);
        Assert.False(first.IsCompleted);

        var secondUse = await _handler.HandleAsync(Request("POST", "/api/skua/a1/boosts/use", "{\"itemId\":17439}", Json), CancellationToken.None);
        var relogin = await _handler.HandleAsync(Request("POST", "/api/skua/a1/relogin", "{}", Json), CancellationToken.None);
        Assert.Equal(409, secondUse.Status);
        Assert.Equal("busy", ErrorOf(secondUse));
        Assert.Equal(409, relogin.Status);
        Assert.Equal("busy", ErrorOf(relogin));
        Assert.Single(_boosts.Used);
        Assert.Empty(_boosts.Relogged);

        hold.SetResult();
        AssertOk(await first);
        _boosts.UseHold = null;
        AssertOk(Post("/api/skua/a1/relogin"));
        AssertOk(Use("{\"itemId\":17439}"));
    }

    [Fact]
    public async Task The_gate_opens_when_the_service_throws()
    {
        _boosts.ThrowOnUse = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.HandleAsync(Request("POST", "/api/skua/a1/boosts/use", "{\"itemId\":17439}", Json), CancellationToken.None));

        _boosts.ThrowOnUse = false;
        AssertOk(Use("{\"itemId\":17439}"));
    }

    [Fact]
    public async Task The_use_and_relogin_of_one_instance_do_not_block_another_instance()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _boosts.UseHold = hold.Task;
        var first = _handler.HandleAsync(Request("POST", "/api/skua/a1/boosts/use", "{\"itemId\":17439}", Json), CancellationToken.None);

        var relogin = await _handler.HandleAsync(Request("POST", "/api/skua/c3/relogin", "{}", Json), CancellationToken.None);
        AssertOk(relogin);
        Assert.Equal(["c3"], _boosts.Relogged);

        hold.SetResult();
        AssertOk(await first);
    }

    [Fact]
    public void Relogin_sends_no_body_value_to_the_service()
    {
        AssertOk(Post("/api/skua/a1/relogin", "{\"itemId\":9,\"password\":\"x\"}"));

        Assert.Equal(["a1"], _boosts.Relogged);
        Assert.Empty(_boosts.Used);
    }

    [Fact]
    public void The_instance_is_read_once_from_the_feed_per_request()
    {
        var feed = new VanishingFeed(_skua.Current);
        var api = new SkuaApi(
            feed, _skua, new SkuaScriptPrefs(new MemorySkuaScriptFile(), _clock.GetUtcNow), new SkuaOptionCache(new MemorySkuaScriptFile(), _clock.GetUtcNow),
            CultureInfo.InvariantCulture, null, _boosts);
        var handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9", api);

        Assert.Equal(200, handler.Handle(Request("POST", "/api/skua/a1/boosts/use", "{\"itemId\":5}", Json)).Status);
        Assert.Equal(1, feed.Reads);
        feed.Reset();
        Assert.Equal(200, handler.Handle(Request("POST", "/api/skua/a1/relogin", "{}", Json)).Status);
        Assert.Equal(1, feed.Reads);
    }

    private sealed class VanishingFeed(SkuaHubView first) : ISkuaFeed
    {
        private int _reads;

        public int Reads { get; private set; }

        public SkuaHubView Current
        {
            get
            {
                Reads++;
                return ++_reads == 1 ? first : SkuaHubView.Empty;
            }
        }

        public void Reset()
        {
            _reads = 0;
            Reads = 0;
        }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
