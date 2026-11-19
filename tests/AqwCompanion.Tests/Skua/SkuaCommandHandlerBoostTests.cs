using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaCommandHandlerBoostTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skualink-handler-boost-test", "Scripts"));

    private DateTimeOffset _now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly SkuaBoostCommandsTests.FakeBoostHost _boostHost = new();

    private static readonly SkuaBoosts Published =
        new(0, 0, 0, 0, 1, [new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 33)]);

    private (SkuaCommandHandler Handler, ScriptHost Host, SkuaControlState Control) NewHandler(bool withBoosts = true, ScriptHost? host = null)
    {
        host ??= new ScriptHost();
        var catalog = new SkuaScriptCatalog(Root, _ => [Path.Combine(Root, "Farm", "Gold.cs")], _ => false);
        var control = new SkuaControlState(() => _now);
        var boosts = withBoosts
            ? new SkuaBoostCommands(_boostHost, () => Published, () => _now, _ => Task.CompletedTask, work => Task.Run(work))
            : null;
        return (new SkuaCommandHandler(host, catalog, control, () => _now, null, _ => Task.CompletedTask, boosts), host, control);
    }

    private static ResultMessage Fail(string reqId, string error) => new(reqId, false, error, null, null, null, false, false);

    [Fact]
    public async Task UseBoost_reaches_the_boost_commands_and_answers_with_the_reqId()
    {
        _boostHost.ActivateAfterReads = 0;
        var (handler, _, _) = NewHandler();

        var result = await handler.HandleAsync(new UseBoostCommand("r7", 17439));

        Assert.Equal(new ResultMessage("r7", true, null, null, null, null, false, false), result);
        Assert.Equal([17439], _boostHost.Used);
    }

    [Fact]
    public async Task Relogin_reaches_the_boost_commands_and_answers_with_the_reqId()
    {
        _boostHost.LoggedIn = false;
        _boostHost.ServerName = "Twilly";
        var (handler, _, _) = NewHandler();

        var result = await handler.HandleAsync(new ReloginCommand("r8"));

        Assert.Equal(new ResultMessage("r8", true, null, null, null, null, false, false), result);
    }

    [Fact]
    public async Task Without_boost_commands_both_are_unknown()
    {
        var (handler, _, _) = NewHandler(withBoosts: false);

        Assert.Equal(Fail("r1", "unknown command"), await handler.HandleAsync(new UseBoostCommand("r1", 17439)));
        Assert.Equal(Fail("r2", "unknown command"), await handler.HandleAsync(new ReloginCommand("r2")));
        Assert.Empty(_boostHost.Used);
    }

    [Fact]
    public async Task A_host_that_throws_on_use_answers_the_error_text_and_the_handler_stays_usable()
    {
        _boostHost.ThrowOnUse = true;
        var (handler, _, _) = NewHandler();

        Assert.Equal(Fail("r1", "use"), await handler.HandleAsync(new UseBoostCommand("r1", 17439)));

        _boostHost.ThrowOnUse = false;
        _boostHost.ActivateAfterReads = 0;
        Assert.True((await handler.HandleAsync(new UseBoostCommand("r2", 17439))).Ok);
    }

    [Fact]
    public async Task UseBoost_waits_for_the_command_before_it()
    {
        var release = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptHost { StartResult = () => release.Task };
        var (handler, _, _) = NewHandler(host: host);
        _boostHost.ActivateAfterReads = 0;
        var order = new List<string>();

        var start = Task.Run(async () =>
        {
            var id = SkuaScriptCatalog.IdOf("Farm/Gold.cs");
            await handler.HandleAsync(new ListScriptsCommand("l"));
            await handler.HandleAsync(new StartCommand("s", id));
            lock (order)
            {
                order.Add("start");
            }
        });
        await host.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var use = Task.Run(async () =>
        {
            await handler.HandleAsync(new UseBoostCommand("u", 17439));
            lock (order)
            {
                order.Add("use");
            }
        });
        await Task.Delay(200);
        Assert.Empty(_boostHost.Used);

        release.SetResult(null);
        await Task.WhenAll(start, use).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["start", "use"], order);
        Assert.Equal([17439], _boostHost.Used);
    }

    [Fact]
    public async Task Tick_still_works_while_a_useBoost_polls()
    {
        var polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new SkuaScriptCatalog(Root, _ => [], _ => false);
        var control = new SkuaControlState(() => _now);
        var boosts = new SkuaBoostCommands(
            _boostHost, () => Published, () => _now,
            async _ =>
            {
                polling.TrySetResult();
                await release.Task;
            });
        var host = new ScriptHost { Running = true };
        var handler = new SkuaCommandHandler(host, catalog, control, () => _now, null, _ => Task.CompletedTask, boosts);

        var use = handler.HandleAsync(new UseBoostCommand("u", 17439));
        await polling.Task.WaitAsync(TimeSpan.FromSeconds(10));

        handler.Tick();
        Assert.Equal("running", control.State);

        release.SetResult();
        await use.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class ScriptHost : ISkuaScriptHost
    {
        public bool Running { get; set; }
        public Func<Task<Exception?>> StartResult { get; set; } = () => Task.FromResult<Exception?>(null);
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ScriptRunning => Running;

        public bool ScriptExists(string fullPath) => true;

        public void SetLoadedScript(string fullPath)
        {
        }

        public Task<Exception?> StartScript()
        {
            StartEntered.TrySetResult();
            return StartResult();
        }

        public bool LoadedScriptHasOptions() => false;

        public Task StopScript() => Task.CompletedTask;
    }
}
