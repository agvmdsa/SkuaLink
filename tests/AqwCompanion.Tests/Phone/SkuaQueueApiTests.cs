using System.Globalization;
using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public sealed class SkuaQueueApiTests : IDisposable
{
    private const string GoodHost = "127.0.0.1:47822";
    private const string IdA = "aaaaaaaaaaaaaaaa";
    private const string IdB = "bbbbbbbbbbbbbbbb";
    private const string Json = "application/json";
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(T0);
    private readonly PhoneRegistry _registry;
    private readonly PhoneHub _hub;
    private readonly FakeSkua _skua = new();
    private readonly FakeQueues _queues = new();
    private readonly PhoneHandler _handler;
    private readonly string _token;

    public SkuaQueueApiTests()
    {
        _registry = new PhoneRegistry(new MemoryPhoneFileStore(), _clock.GetUtcNow);
        _hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), _clock.GetUtcNow);
        _handler = Handler(_queues);
        _token = _registry.TryPair(_registry.CreatePairingToken(), "Phone").DeviceToken!;
        _skua.Current = new SkuaHubView(
        [
            FakeSkua.Instance("a1", "Hero", status: new SkuaStatus(true, "Hero", null, null, null, false, null)),
            FakeSkua.Instance("b2", "Zed", disconnected: true),
            FakeSkua.Instance("c3", "Nobody", status: new SkuaStatus(false, null, null, null, null, false, null)),
        ]);
        _skua.AnswerWith(ScriptList());
    }

    public void Dispose() => _hub.Dispose();

    private PhoneHandler Handler(ISkuaQueues? queues)
    {
        var api = new SkuaApi(
            _skua, _skua, new SkuaScriptPrefs(new MemorySkuaScriptFile(), _clock.GetUtcNow), new SkuaOptionCache(new MemorySkuaScriptFile(), _clock.GetUtcNow),
            CultureInfo.InvariantCulture, queues);
        return new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9", api);
    }

    private static ResultMessage ScriptList() =>
        new("x", true, null, [new SkuaScriptInfo(IdA, "Dailies", "f"), new SkuaScriptInfo(IdB, "Butler", "f")], null, null, false, false, false);

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

    private PhoneResponse Get(string path, bool cookie = true) => _handler.Handle(Request("GET", path, "", null, cookie, write: false));

    private PhoneResponse Put(string body, string path = "/api/skua/a1/queue", string? contentType = Json, bool cookie = true, bool write = true, string? origin = null) =>
        _handler.Handle(Request("PUT", path, body, contentType, cookie, write, origin));

    private PhoneResponse Post(string path, string body = "{}", string? contentType = Json, bool cookie = true, bool write = true, string? origin = null) =>
        _handler.Handle(Request("POST", path, body, contentType, cookie, write, origin));

    private static string Text(PhoneResponse response) => Encoding.UTF8.GetString(response.Body);

    private static string ErrorOf(PhoneResponse response)
    {
        using var doc = JsonDocument.Parse(Text(response));
        Assert.Single(doc.RootElement.EnumerateObject());
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    private static QueueStep Step(string id, string item = "Gold", ReputationGoal? reputation = null) =>
        new(id, IdA, "Dailies", item, 5, reputation);

    private static string OneStep(string scriptId = IdA, string item = "\"Gold\"", string quantity = "5", string extra = "") =>
        $"{{\"steps\":[{{\"scriptId\":\"{scriptId}\",\"goalItem\":{item},\"goalQuantity\":{quantity}{extra}}}]}}";

    private int ListCalls => _skua.Sent.Count(s => s.Command is ListScriptsCommand);

    // ---- read ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void GET_returns_the_queue_of_the_instances_character_or_an_empty_one()
    {
        var first = Get("/api/skua/a1/queue");
        Assert.Equal(200, first.Status);
        using (var doc = JsonDocument.Parse(Text(first)))
        {
            var queue = doc.RootElement.GetProperty("queue");
            Assert.Equal("Hero", queue.GetProperty("character").GetString());
            Assert.Equal(0, queue.GetProperty("steps").GetArrayLength());
            Assert.Equal("Stopped", queue.GetProperty("state").GetString());
        }

        _queues.Items.Add(new QueueView("hero", [Step("0123456789abcdef0123456789abcdef")], 0, QueueState.Running, QueuePhase.Running, null, 2, true));
        using var stored = JsonDocument.Parse(Text(Get("/api/skua/a1/queue")));
        var found = stored.RootElement.GetProperty("queue");
        Assert.Equal("hero", found.GetProperty("character").GetString());
        Assert.Equal("Running", found.GetProperty("state").GetString());
        Assert.Equal("Gold", found.GetProperty("steps")[0].GetProperty("goalItem").GetString());
        Assert.Empty(_skua.Sent);
    }

    // ---- request rules -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_queue_route_needs_the_cookie_the_write_header_and_a_same_origin_request()
    {
        Assert.Equal(401, Get("/api/skua/a1/queue", cookie: false).Status);
        Assert.Equal(401, Put(OneStep(), cookie: false).Status);
        Assert.Equal(401, Post("/api/skua/a1/queue/start", cookie: false).Status);

        Assert.Equal(403, Put(OneStep(), write: false).Status);
        Assert.Equal(403, Post("/api/skua/a1/queue/start", write: false).Status);
        Assert.Equal(403, Put(OneStep(), origin: "https://evil.example").Status);
        Assert.Equal(403, Post("/api/skua/a1/queue/start", origin: "https://evil.example").Status);
        Assert.Equal(403, _handler.Handle(Request("GET", "/api/skua/a1/queue", "", null, write: false, origin: "https://evil.example")).Status);

        Assert.Equal(415, Put(OneStep(), contentType: "text/plain").Status);
        Assert.Equal(415, Put(OneStep(), contentType: null).Status);
        Assert.Equal(415, Post("/api/skua/a1/queue/start", "{}", contentType: "text/plain").Status);

        Assert.Empty(_skua.Sent);
        Assert.Empty(_queues.Replaced);
        Assert.Empty(_queues.Commands);
    }

    [Fact]
    public void An_unknown_instance_is_404_a_disconnected_one_409_and_an_instance_without_a_character_is_409_skua_has_no_character()
    {
        foreach (var call in new Func<string, PhoneResponse>[]
                 {
                     id => Get($"/api/skua/{id}/queue"),
                     id => Put(OneStep(), $"/api/skua/{id}/queue"),
                     id => Post($"/api/skua/{id}/queue/start"),
                 })
        {
            Assert.Equal(404, call("nope").Status);
            Assert.Equal("unknown Skua", ErrorOf(call("nope")));
            Assert.Equal(409, call("b2").Status);
            Assert.Equal("Skua is disconnected", ErrorOf(call("b2")));
            Assert.Equal(409, call("c3").Status);
            Assert.Equal("Skua has no character", ErrorOf(call("c3")));
        }

        Assert.Empty(_skua.Sent);
        Assert.Empty(_queues.Replaced);
        Assert.Empty(_queues.Commands);
    }

    [Fact]
    public void Without_a_queue_service_every_queue_route_is_404()
    {
        var handler = Handler(null);
        Assert.Equal(404, handler.Handle(Request("GET", "/api/skua/a1/queue", "", null, write: false)).Status);
        Assert.Equal(404, handler.Handle(Request("PUT", "/api/skua/a1/queue", OneStep(), Json)).Status);
        Assert.Equal(404, handler.Handle(Request("POST", "/api/skua/a1/queue/start", "{}", Json)).Status);
        Assert.Empty(_skua.Sent);
    }

    // ---- PUT ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PUT_validates_and_replaces()
    {
        const string clientId = "0123456789abcdef0123456789abcdef";
        _queues.Items.Add(new QueueView("Hero", [Step(clientId)], 0, QueueState.Stopped, null, null, null, true));
        var body = $"{{\"steps\":[{{\"id\":\"{clientId}\",\"scriptId\":\"{IdB}\",\"goalItem\":\"  Gold \",\"goalQuantity\":5,\"maxMinutes\":30}},"
                   + $"{{\"scriptId\":\"{IdA}\",\"goalItem\":\"Dragon Scale\",\"goalQuantity\":100}}]}}";

        var response = Put(body);

        Assert.Equal(200, response.Status);
        using var doc = JsonDocument.Parse(Text(response));
        Assert.Equal("Hero", doc.RootElement.GetProperty("queue").GetProperty("character").GetString());
        var (character, steps) = Assert.Single(_queues.Replaced);
        Assert.Equal("Hero", character);
        Assert.Equal(2, steps.Count);
        Assert.Equal(clientId, steps[0].Id);
        Assert.Equal((IdB, "Butler", "Gold", 5, 30), (steps[0].ScriptId, steps[0].ScriptName, steps[0].GoalItem, steps[0].GoalQuantity, steps[0].MaxMinutes));
        Assert.Matches("^[0-9a-f]{32}$", steps[1].Id);
        Assert.NotEqual(clientId, steps[1].Id);
        Assert.Equal((IdA, "Dailies", "Dragon Scale", 100, null), (steps[1].ScriptId, steps[1].ScriptName, steps[1].GoalItem, steps[1].GoalQuantity, steps[1].MaxMinutes));
        Assert.All(steps, s => Assert.Null(s.Reputation));
        Assert.Equal(1, ListCalls);
    }

    [Fact]
    public void PUT_keeps_the_reputation_of_steps_with_a_known_id()
    {
        const string known = "0123456789abcdef0123456789abcdef";
        var goal = new ReputationGoal("Loremaster", 6);
        _queues.Items.Add(new QueueView("Hero", [Step(known, reputation: goal)], 0, QueueState.Stopped, null, null, null, true));
        var body = $"{{\"steps\":[{{\"id\":\"{known}\",\"scriptId\":\"{IdA}\",\"goalItem\":\"Gold\",\"goalQuantity\":5,"
                   + "\"reputation\":{\"faction\":\"Evil\",\"rank\":10}},"
                   + $"{{\"scriptId\":\"{IdA}\",\"goalItem\":\"Gold\",\"goalQuantity\":5,\"reputation\":{{\"faction\":\"Evil\",\"rank\":10}}}}]}}";

        Assert.Equal(200, Put(body).Status);

        var steps = Assert.Single(_queues.Replaced).Steps;
        Assert.Equal(goal, steps[0].Reputation);
        Assert.Null(steps[1].Reputation);
    }

    [Fact]
    public void PUT_refuses_what_the_validator_refuses()
    {
        var many = string.Join(',', Enumerable.Repeat($"{{\"scriptId\":\"{IdA}\",\"goalItem\":\"Gold\",\"goalQuantity\":1}}", 21));
        var cases = new (string Body, string Error)[]
        {
            ("{\"steps\":[]}", QueueValidator.BadCount),
            ($"{{\"steps\":[{many}]}}", QueueValidator.BadCount),
            (OneStep(scriptId: "cccccccccccccccc"), QueueValidator.UnknownScript),
            (OneStep(scriptId: "../x"), QueueValidator.UnknownScript),
            (OneStep(item: "\"\""), QueueValidator.NeedItem),
            (OneStep(item: "\"   \""), QueueValidator.NeedItem),
            (OneStep(quantity: "0"), QueueValidator.BadQuantity),
            (OneStep(quantity: "1000001"), QueueValidator.BadQuantity),
            (OneStep(extra: ",\"maxMinutes\":0"), QueueValidator.BadMinutes),
            (OneStep(extra: ",\"maxMinutes\":1441"), QueueValidator.BadMinutes),
            ("{\"steps\":{}}", SkuaApi.InvalidBody),
            ("{\"steps\":\"x\"}", SkuaApi.InvalidBody),
            ("{}", SkuaApi.InvalidBody),
            ("{\"steps\":[5]}", SkuaApi.InvalidBody),
            ("{\"steps\":[\"x\"]}", SkuaApi.InvalidBody),
            ("{\"steps\":[null]}", SkuaApi.InvalidBody),
            ("{\"steps\":[{\"scriptId\":5,\"goalItem\":\"Gold\",\"goalQuantity\":1}]}", SkuaApi.InvalidBody),
            ($"{{\"steps\":[{{\"scriptId\":\"{IdA}\",\"goalQuantity\":1}}]}}", SkuaApi.InvalidBody),
            ($"{{\"steps\":[{{\"scriptId\":\"{IdA}\",\"goalItem\":7,\"goalQuantity\":1}}]}}", SkuaApi.InvalidBody),
            (OneStep(extra: ",\"maxMinutes\":\"5\""), SkuaApi.InvalidBody),
            (OneStep(extra: ",\"id\":5"), SkuaApi.InvalidBody),
        };

        foreach (var (body, error) in cases)
        {
            var response = Put(body);
            Assert.True(response.Status == 400, $"{body}: {response.Status}");
            Assert.Equal(error, ErrorOf(response));
        }

        Assert.Empty(_queues.Replaced);
    }

    [Fact]
    public void PUT_refuses_more_than_20_steps_and_zero_steps_before_asking_the_plugin_anything()
    {
        var many = string.Join(',', Enumerable.Repeat($"{{\"scriptId\":\"{IdA}\",\"goalItem\":\"Gold\",\"goalQuantity\":1}}", 21));
        Assert.Equal(400, Put($"{{\"steps\":[{many}]}}").Status);
        Assert.Equal(400, Put("{\"steps\":[]}").Status);
        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void PUT_refuses_a_body_over_8192_bytes_with_413_and_bodies_of_the_other_routes_keep_their_smaller_caps()
    {
        Assert.Equal(8192, SkuaApi.MaxQueueBody);
        var padded = (int size) => "{\"steps\":[]}" + new string(' ', size - 12);

        var over = Put(padded(8193));
        Assert.Equal(413, over.Status);
        Assert.Equal(SkuaApi.TooLarge, ErrorOf(over));
        Assert.Equal(400, Put(padded(8192)).Status);
        Assert.Equal(413, Put("{\"steps\":[],\"x\":\"" + new string('é', 4100) + "\"}").Status);

        Assert.Equal(413, Post("/api/skua/a1/queue/start", padded(SkuaApi.MaxBody + 1)).Status);
        Assert.Equal(200, Post("/api/skua/a1/queue/start", padded(SkuaApi.MaxBody)).Status);
        Assert.Equal(413, Post("/api/skua/a1/start", padded(SkuaApi.MaxBody + 1)).Status);

        Assert.Empty(_queues.Replaced);
        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void PUT_returns_409_with_the_engines_text_while_a_step_is_busy()
    {
        _queues.ReplaceResult = QueueResult.Refused(QueueEngine.BusyText);

        var response = Put(OneStep());

        Assert.Equal(409, response.Status);
        Assert.Equal(QueueEngine.BusyText, ErrorOf(response));
        Assert.Single(_queues.Replaced);
    }

    [Fact]
    public void PUT_asks_the_plugin_for_its_scripts_and_a_missing_answer_is_504()
    {
        _skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(null);

        var response = Put(OneStep());

        Assert.Equal(504, response.Status);
        Assert.Equal(SkuaApi.NoAnswer, ErrorOf(response));
        Assert.Single(_skua.Sent);
        Assert.IsType<ListScriptsCommand>(_skua.Sent[0].Command);
        Assert.Equal("a1", _skua.Sent[0].InstanceId);
        Assert.Empty(_queues.Replaced);
    }

    [Fact]
    public void A_refusal_of_listScripts_is_400_with_the_plugins_text()
    {
        _skua.AnswerWith(new ResultMessage("x", false, "scripts folder missing", null, null, null, false, false, false));

        var response = Put(OneStep());

        Assert.Equal(400, response.Status);
        Assert.Equal("scripts folder missing", ErrorOf(response));
        Assert.Empty(_queues.Replaced);
    }

    [Fact]
    public async Task A_second_PUT_for_the_same_instance_while_one_waits_is_409_busy()
    {
        var pending = new TaskCompletionSource<ResultMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _skua.Answer = (_, _) => pending.Task;

        var first = _handler.HandleAsync(Request("PUT", "/api/skua/a1/queue", OneStep(), Json), CancellationToken.None);
        Assert.False(first.IsCompleted);

        var busy = await _handler.HandleAsync(Request("PUT", "/api/skua/a1/queue", OneStep(), Json), CancellationToken.None);
        Assert.Equal(409, busy.Status);
        Assert.Equal(SkuaApi.Busy, ErrorOf(busy));
        Assert.Single(_skua.Sent);

        pending.SetResult(ScriptList());
        Assert.Equal(200, (await first).Status);
        Assert.Equal(200, (await _handler.HandleAsync(Request("PUT", "/api/skua/a1/queue", OneStep(), Json), CancellationToken.None)).Status);
        Assert.Equal(2, _queues.Replaced.Count);
    }

    // ---- commands ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void POST_commands_reach_the_queue_of_the_character()
    {
        _queues.Items.Add(new QueueView("Hero", [Step("0123456789abcdef0123456789abcdef")], 0, QueueState.Running, QueuePhase.Running, null, null, true));
        var words = new (string Word, QueueCommand Command)[]
        {
            ("start", QueueCommand.Start), ("pause", QueueCommand.Pause), ("continue", QueueCommand.Continue), ("skip", QueueCommand.Skip), ("stop", QueueCommand.Stop),
        };

        foreach (var (word, command) in words)
        {
            var response = Post($"/api/skua/a1/queue/{word}");
            Assert.Equal(200, response.Status);
            using var doc = JsonDocument.Parse(Text(response));
            Assert.Equal("Running", doc.RootElement.GetProperty("queue").GetProperty("state").GetString());
        }

        Assert.Equal(words.Select(w => ("Hero", w.Command)), _queues.Commands);

        // An empty body is fine too.
        Assert.Equal(200, _handler.Handle(Request("POST", "/api/skua/a1/queue/pause", "", null)).Status);

        Assert.Equal(404, Post("/api/skua/a1/queue/explode").Status);
        Assert.Equal(404, Post("/api/skua/a1/queue/Start").Status);
        Assert.Equal(405, Get("/api/skua/a1/queue/start").Status);
        Assert.Equal(405, Post("/api/skua/a1/queue").Status);
        Assert.Equal(405, _handler.Handle(Request("DELETE", "/api/skua/a1/queue", "", null)).Status);
        Assert.Equal(405, _handler.Handle(Request("PUT", "/api/skua/a1/queue/start", "{}", Json)).Status);
        Assert.Equal(6, _queues.Commands.Count);

        _queues.CommandResult = QueueResult.Refused("The queue is paused. Press Continue.");
        var refused = Post("/api/skua/a1/queue/start");
        Assert.Equal(409, refused.Status);
        Assert.Equal("The queue is paused. Press Continue.", ErrorOf(refused));
        Assert.Empty(_skua.Sent);
    }

    // ---- hostile input -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Hostile_text_in_item_names_is_cleaned_not_echoed_raw()
    {
        var item = "\"Gold\\u0007\\u001b[31m\\n" + new string('x', 200) + "\"";

        Assert.Equal(200, Put(OneStep(item: item)).Status);

        var stored = Assert.Single(_queues.Replaced).Steps[0].GoalItem;
        Assert.Equal(80, stored.Length);
        Assert.All(stored, c => Assert.False(char.IsControl(c)));
        Assert.StartsWith("Gold", stored, StringComparison.Ordinal);

        var refused = Put(OneStep(scriptId: "<script>alert(1)</script>"));
        Assert.Equal(400, refused.Status);
        Assert.DoesNotContain("script>", Text(refused), StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_integer_quantity_or_a_huge_number_is_400_not_a_crash()
    {
        foreach (var quantity in new[] { "1.5", "1e400", "\"7\"", "9999999999", "-1", "null", "true", "[]" })
        {
            var response = Put(OneStep(quantity: quantity));
            Assert.True(response.Status == 400, $"{quantity}: {response.Status}");
            Assert.NotEmpty(ErrorOf(response));
        }

        Assert.Empty(_queues.Replaced);
    }
}
