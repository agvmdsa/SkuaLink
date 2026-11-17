using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Push;
using AqwCompanion.Phone.Web;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PushApiTests
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc";

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;

        public VapidKey? GetExisting() => key;
    }

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePushHandler _handler = new();
    private readonly VapidKey _vapid = VapidKey.Create();
    private readonly MemoryPushFileStore _file = new();
    private readonly PushSubscriptionStore _store;
    private readonly PushApi _api;

    public PushApiTests()
    {
        _store = new PushSubscriptionStore(_file, _clock.GetUtcNow);
        var sender = new PushSender(new HttpClient(_handler), new Keys(_vapid), _clock.GetUtcNow, (_, _) => Task.CompletedTask);
        _api = new PushApi(_store, new Keys(_vapid), sender, _clock.GetUtcNow);
    }

    private static PhoneRequest Req(string method, string path, string body = "", string contentType = "application/json") =>
        new(method, path, new Dictionary<string, string> { ["Content-Type"] = contentType }, body);

    private PhoneResponse Call(string method, string path, string body = "", string device = "d1", string contentType = "application/json") =>
        _api.HandleAsync(Req(method, path, body, contentType), path, device, CancellationToken.None).GetAwaiter().GetResult();

    private static string Text(PhoneResponse r) => Encoding.UTF8.GetString(r.Body);

    private static JsonElement Json(PhoneResponse r) => JsonDocument.Parse(r.Body).RootElement.Clone();

    private static string Error(PhoneResponse r) => Json(r).GetProperty("error").GetString()!;

    private static (string P256dh, string Auth) NewKeys()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ua.ExportParameters(false).Q;
        return (Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private static string SubBody(string endpoint = Endpoint, string? kinds = null, (string P256dh, string Auth)? keys = null)
    {
        var (p, a) = keys ?? NewKeys();
        return $"{{\"endpoint\":\"{endpoint}\",\"keys\":{{\"p256dh\":\"{p}\",\"auth\":\"{a}\"}}{(kinds is null ? "" : $",\"kinds\":{kinds}")}}}";
    }

    private PhoneResponse Subscribe(string device = "d1", string? kinds = null) =>
        Call("PUT", "/api/push/subscription", SubBody(kinds: kinds), device);

    private static string[] KindsOf(JsonElement state) => state.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()!).ToArray();

    [Fact]
    public void Key_returns_the_vapid_public_key()
    {
        var response = Call("GET", "/api/push/key");

        Assert.Equal(200, response.Status);
        Assert.Equal(_vapid.PublicKeyText, Json(response).GetProperty("publicKey").GetString());
    }

    [Fact]
    public void State_of_a_new_device_is_not_subscribed_with_no_kinds_and_null_error()
    {
        var response = Call("GET", "/api/push/state");

        Assert.Equal(200, response.Status);
        Assert.Equal("{\"subscribed\":false,\"kinds\":[],\"lastError\":null}", Text(response));
    }

    [Fact]
    public void Subscribe_stores_the_endpoint_keys_and_all_kinds_and_answers_the_state()
    {
        var keys = NewKeys();

        var response = Call("PUT", "/api/push/subscription", SubBody(keys: keys));

        Assert.Equal(200, response.Status);
        var state = Json(response);
        Assert.True(state.GetProperty("subscribed").GetBoolean());
        Assert.Equal(PushKinds.All, KindsOf(state));
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastError").ValueKind);
        var stored = _store.Get("d1")!;
        Assert.Equal(Endpoint, stored.Endpoint);
        Assert.Equal(keys.P256dh, stored.P256dh);
        Assert.Equal(keys.Auth, stored.Auth);
        Assert.Equal(PushKinds.All, stored.Kinds);
    }

    [Fact]
    public void Subscribe_without_kinds_keeps_the_kinds_of_an_existing_subscription()
    {
        Subscribe(kinds: "[\"RankUp\"]");

        Assert.Equal(["RankUp"], KindsOf(Json(Subscribe())));
        Assert.Equal(["RankUp"], KindsOf(Json(Call("PUT", "/api/push/subscription", SubBody(kinds: "null")))));
    }

    [Fact]
    public void Subscribe_with_kinds_replaces_them_and_an_empty_list_is_none()
    {
        Subscribe(kinds: "[\"RankUp\"]");

        Assert.Equal(new[] { "ScriptError", "RankUp" }, KindsOf(Json(Subscribe(kinds: "[\"RankUp\",\"ScriptError\"]"))));
        Assert.Empty(KindsOf(Json(Subscribe(kinds: "[]"))));
        Assert.Empty(_store.Get("d1")!.Kinds);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("http://fcm.googleapis.com/x")]
    [InlineData("https://fcm.googleapis.com.evil.com/x")]
    [InlineData("https://user@fcm.googleapis.com/x")]
    public void Subscribe_refuses_a_foreign_endpoint_http_and_a_look_alike_host(string endpoint)
    {
        var response = Call("PUT", "/api/push/subscription", SubBody(endpoint));

        Assert.Equal(400, response.Status);
        Assert.Equal(PushApi.EndpointRefused, Error(response));
        Assert.Null(_store.Get("d1"));
        Assert.Equal(0, _file.Saves);
    }

    [Fact]
    public void Subscribe_refuses_a_missing_or_non_string_endpoint()
    {
        Assert.Equal(400, Call("PUT", "/api/push/subscription", "{\"keys\":{}}").Status);
        Assert.Equal(400, Call("PUT", "/api/push/subscription", "{\"endpoint\":5}").Status);
    }

    public static TheoryData<string> BadKeys()
    {
        var good = NewKeys();
        using var other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = other.ExportParameters(false).Q;
        var offCurve = Base64Url.EncodeToString([0x04, .. q.X!, .. new byte[32]]);
        string Body(string keys) => $"{{\"endpoint\":\"{Endpoint}\"{keys}}}";
        return new TheoryData<string>
        {
            Body($",\"keys\":{{\"p256dh\":\"{Base64Url.EncodeToString(new byte[64])}\",\"auth\":\"{good.Auth}\"}}"),
            Body($",\"keys\":{{\"p256dh\":\"{offCurve}\",\"auth\":\"{good.Auth}\"}}"),
            Body($",\"keys\":{{\"p256dh\":\"{good.P256dh}\",\"auth\":\"{Base64Url.EncodeToString(new byte[15])}\"}}"),
            Body($",\"keys\":{{\"p256dh\":\"!!not base64url!!\",\"auth\":\"{good.Auth}\"}}"),
            Body(string.Empty),
            Body(",\"keys\":\"abc\""),
            Body(",\"keys\":{\"p256dh\":1,\"auth\":2}"),
            Body($",\"keys\":{{\"p256dh\":\"{new string('A', 600)}\",\"auth\":\"{good.Auth}\"}}"),
        };
    }

    [Theory]
    [MemberData(nameof(BadKeys))]
    public void Subscribe_refuses_bad_keys(string body)
    {
        var response = Call("PUT", "/api/push/subscription", body);

        Assert.Equal(400, response.Status);
        Assert.Equal(PushApi.InvalidKeys, Error(response));
        Assert.Null(_store.Get("d1"));
    }

    [Theory]
    [InlineData("[\"Nope\"]")]
    [InlineData("[\"rankup\"]")]
    [InlineData("[1]")]
    [InlineData("[null]")]
    [InlineData("[[\"RankUp\"]]")]
    public void Subscribe_refuses_an_unknown_kind_and_non_string_kinds(string kinds)
    {
        var response = Subscribe(kinds: kinds);

        Assert.Equal(400, response.Status);
        Assert.Equal(PushApi.UnknownKind, Error(response));
        Assert.Null(_store.Get("d1"));
        Assert.Equal(400, Subscribe(kinds: "\"RankUp\"").Status);
    }

    [Fact]
    public void A_body_over_4096_bytes_is_413_a_non_json_content_type_415_bad_json_and_non_object_400()
    {
        var big = "{\"endpoint\":\"" + new string('a', PushApi.MaxBody) + "\"}";
        Assert.Equal(413, Call("PUT", "/api/push/subscription", big).Status);
        Assert.Equal(PushApi.TooLarge, Error(Call("PUT", "/api/push/kinds", big)));
        Assert.Equal(413, Call("PUT", "/api/push/subscription", "{\"endpoint\":\"" + new string('é', 2100) + "\"}").Status);
        Assert.Equal(415, Call("PUT", "/api/push/subscription", SubBody(), contentType: "text/plain").Status);
        Assert.Equal(415, Call("PUT", "/api/push/kinds", "{\"kinds\":[]}", contentType: "application/x-www-form-urlencoded").Status);
        Assert.Equal(200, Call("PUT", "/api/push/subscription", SubBody(), contentType: "application/json; charset=utf-8").Status);
        Assert.Equal(400, Call("PUT", "/api/push/subscription", "{not json").Status);
        Assert.Equal(400, Call("PUT", "/api/push/subscription", "[1]").Status);
        Assert.Equal(400, Call("PUT", "/api/push/subscription", "").Status);
        Assert.Equal(PushApi.InvalidBody, Error(Call("PUT", "/api/push/kinds", "\"x\"")));
        Assert.Equal(400, Call("PUT", "/api/push/kinds", new string('[', 50) + new string(']', 50)).Status);
    }

    [Fact]
    public void Delete_removes_the_subscription_and_is_fine_without_one()
    {
        Assert.Equal("{\"subscribed\":false,\"kinds\":[],\"lastError\":null}", Text(Call("DELETE", "/api/push/subscription")));
        Subscribe();

        var response = Call("DELETE", "/api/push/subscription");

        Assert.Equal(200, response.Status);
        Assert.False(Json(response).GetProperty("subscribed").GetBoolean());
        Assert.Null(_store.Get("d1"));
    }

    [Fact]
    public void SetKinds_changes_the_kinds_and_is_409_without_a_subscription()
    {
        var none = Call("PUT", "/api/push/kinds", "{\"kinds\":[\"RankUp\"]}");
        Assert.Equal(409, none.Status);
        Assert.Equal(PushApi.NoSubscription, Error(none));
        Subscribe();

        var response = Call("PUT", "/api/push/kinds", "{\"kinds\":[\"RankUp\"]}");

        Assert.Equal(200, response.Status);
        Assert.Equal(["RankUp"], KindsOf(Json(response)));
        Assert.Equal(["RankUp"], _store.Get("d1")!.Kinds);
        Assert.Empty(KindsOf(Json(Call("PUT", "/api/push/kinds", "{\"kinds\":[]}"))));
        Assert.Equal(PushApi.UnknownKind, Error(Call("PUT", "/api/push/kinds", "{\"kinds\":[\"Nope\"]}")));
        Assert.Equal(PushApi.InvalidBody, Error(Call("PUT", "/api/push/kinds", "{}")));
        Assert.Equal(PushApi.InvalidBody, Error(Call("PUT", "/api/push/kinds", "{\"kinds\":\"RankUp\"}")));
    }

    [Fact]
    public void Test_sends_one_push_and_answers_ok_and_a_second_within_10_seconds_is_429_with_retry_after_and_sends_nothing()
    {
        Subscribe();

        var first = Call("POST", "/api/push/test");
        var second = Call("POST", "/api/push/test");

        Assert.Equal(200, first.Status);
        Assert.Equal("{\"ok\":true}", Text(first));
        Assert.Single(_handler.Requests);
        Assert.Equal(429, second.Status);
        Assert.Equal(PushApi.WaitText, Error(second));
        Assert.Equal("10", second.Headers.Single(h => h.Key == "Retry-After").Value);
        Assert.Single(_handler.Requests);
        Assert.NotNull(_store.Get("d1")!.LastSuccessUtc);
    }

    [Fact]
    public void Test_after_10_seconds_works_again()
    {
        Subscribe();
        Call("POST", "/api/push/test");
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(429, Call("POST", "/api/push/test").Status);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(200, Call("POST", "/api/push/test").Status);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public void Test_with_a_403_from_the_push_service_is_502_with_its_text_and_records_last_error_which_state_shows()
    {
        Subscribe();
        _handler.Answer(HttpStatusCode.Forbidden, "bad vapid");

        var response = Call("POST", "/api/push/test");

        Assert.Equal(502, response.Status);
        Assert.Equal("push service answered 403: bad vapid", Error(response));
        var state = Json(Call("GET", "/api/push/state"));
        Assert.True(state.GetProperty("subscribed").GetBoolean());
        Assert.Contains("subscribe again", state.GetProperty("lastError").GetString(), StringComparison.Ordinal);
        // A failed test still counts for the rate limit.
        Assert.Equal(429, Call("POST", "/api/push/test").Status);
    }

    [Fact]
    public void Test_with_a_410_removes_the_subscription()
    {
        Subscribe();
        _handler.Answer(HttpStatusCode.Gone);

        var response = Call("POST", "/api/push/test");

        Assert.Equal(410, response.Status);
        Assert.Equal(PushApi.Expired, Error(response));
        Assert.Null(_store.Get("d1"));
    }

    [Fact]
    public void Test_without_a_subscription_is_409()
    {
        var response = Call("POST", "/api/push/test");

        Assert.Equal(409, response.Status);
        Assert.Equal(PushApi.NoSubscription, Error(response));
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("POST", "/api/push/key")]
    [InlineData("PUT", "/api/push/state")]
    [InlineData("GET", "/api/push/subscription")]
    [InlineData("POST", "/api/push/subscription")]
    [InlineData("GET", "/api/push/kinds")]
    [InlineData("DELETE", "/api/push/kinds")]
    [InlineData("GET", "/api/push/test")]
    [InlineData("DELETE", "/api/push/state")]
    public void Wrong_method_is_405_and_unknown_route_is_404(string method, string path)
    {
        Assert.Equal(405, Call(method, path).Status);
        Assert.Equal(404, Call(method, "/api/push/nothing").Status);
        Assert.Equal(404, Call(method, "/api/push").Status);
        Assert.Equal(404, Call(method, "/api/push/state/x").Status);
    }

    [Fact]
    public void A_device_cannot_read_or_change_another_devices_subscription()
    {
        Subscribe("d1", "[\"RankUp\"]");

        Assert.False(Json(Call("GET", "/api/push/state", device: "d2")).GetProperty("subscribed").GetBoolean());
        Assert.Equal(409, Call("PUT", "/api/push/kinds", "{\"kinds\":[]}", "d2").Status);
        Assert.Equal(409, Call("POST", "/api/push/test", device: "d2").Status);
        Call("DELETE", "/api/push/subscription", device: "d2");
        Assert.Equal(["RankUp"], _store.Get("d1")!.Kinds);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public void The_state_never_contains_the_endpoint_or_the_keys()
    {
        var keys = NewKeys();
        var set = Text(Call("PUT", "/api/push/subscription", SubBody(keys: keys)));
        var state = Text(Call("GET", "/api/push/state"));

        foreach (var text in new[] { set, state })
        {
            Assert.DoesNotContain(Endpoint, text, StringComparison.Ordinal);
            Assert.DoesNotContain("fcm.googleapis", text, StringComparison.Ordinal);
            Assert.DoesNotContain(keys.P256dh, text, StringComparison.Ordinal);
            Assert.DoesNotContain(keys.Auth, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Hostile_text_in_last_error_is_returned_as_a_json_string_only()
    {
        Subscribe();
        _store.RecordError("d1", "\"},\"subscribed\":false,\"x\":\"<script>alert(1)</script>\n");

        var response = Call("GET", "/api/push/state");

        var state = Json(response);
        Assert.True(state.GetProperty("subscribed").GetBoolean());
        Assert.Equal("\"},\"subscribed\":false,\"x\":\"<script>alert(1)</script>\n", state.GetProperty("lastError").GetString());
        Assert.DoesNotContain("<script>", Text(response), StringComparison.Ordinal);
        Assert.Equal(3, state.EnumerateObject().Count());
    }

    [Fact]
    public void Subscribing_while_the_store_is_forgetting_is_503_and_not_a_success()
    {
        _store.Quiesce();

        var response = Subscribe();

        Assert.Equal(503, response.Status);
        Assert.Equal(PushApi.Unavailable, Error(response));
        Assert.Null(_store.Get("d1"));
    }

    [Fact]
    public void An_allowed_endpoint_of_900_characters_is_accepted()
    {
        var endpoint = "https://fcm.googleapis.com/fcm/send/" + new string('a', 900 - "https://fcm.googleapis.com/fcm/send/".Length);

        var response = Call("PUT", "/api/push/subscription", SubBody(endpoint));

        Assert.Equal(200, response.Status);
        Assert.Equal(900, _store.Get("d1")!.Endpoint.Length);
    }

    [Fact]
    public void A_clock_moved_backwards_does_not_block_tests()
    {
        Subscribe();
        Assert.Equal(200, Call("POST", "/api/push/test").Status);
        _clock.Advance(TimeSpan.FromMinutes(-5));

        Assert.Equal(200, Call("POST", "/api/push/test").Status);
    }

    [Fact]
    public void Unsubscribing_drops_the_throttle_entry_of_the_device()
    {
        Subscribe();
        Assert.Equal(200, Call("POST", "/api/push/test").Status);
        Call("DELETE", "/api/push/subscription");
        Subscribe();

        Assert.Equal(200, Call("POST", "/api/push/test").Status);
    }
}
