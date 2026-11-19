using System.Reflection;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaBoostCommandsTests
{
    private readonly FakeBoostHost _host = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly List<Action> _queued = [];
    private readonly List<TaskCompletionSource> _tasks = [];
    private DateTimeOffset _now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private int _changed;
    private SkuaBoosts? _published = new(0, 0, 0, 0, 1, [new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 33), new SkuaBoostItem(9, "Mystery Boost", "unknown", 1)]);

    private SkuaBoostCommands New() =>
        new(_host, () => _published, () => _now,
            time =>
            {
                _delays.Add(time);
                return Task.CompletedTask;
            },
            work =>
            {
                var source = new TaskCompletionSource();
                _tasks.Add(source);
                _queued.Add(work);
                return source.Task;
            },
            () => _changed++);

    private static ResultMessage Fail(string reqId, string error) => new(reqId, false, error, null, null, null, false, false);

    private static ResultMessage Ok(string reqId) => new(reqId, true, null, null, null, null, false, false);

    [Fact]
    public async Task UseBoost_activates_and_answers_ok_when_the_value_turns_positive()
    {
        _host.ActivateAfterReads = 2;

        var result = await New().UseAsync(new UseBoostCommand("r1", 17439));

        Assert.Equal(Ok("r1"), result);
        Assert.Equal([17439], _host.Used);
        Assert.Equal(3, _delays.Count);
        Assert.All(_delays, d => Assert.Equal(SkuaBoostCommands.ActivationPoll, d));
        Assert.Equal(1, _changed);
    }

    [Fact]
    public async Task UseBoost_still_answers_ok_when_the_changed_callback_throws()
    {
        _host.ActivateAfterReads = 1;
        var commands = new SkuaBoostCommands(_host, () => _published, () => _now, _ => Task.CompletedTask, null, () => throw new InvalidOperationException("boom"));

        Assert.Equal(Ok("r1"), await commands.UseAsync(new UseBoostCommand("r1", 17439)));
    }

    [Fact]
    public async Task UseBoost_that_does_not_activate_answers_the_game_text_and_never_retries()
    {
        var result = await New().UseAsync(new UseBoostCommand("r1", 17439));

        Assert.Equal(Fail("r1", SkuaBoostCommands.NotActivated), result);
        Assert.Single(_host.Used);
        Assert.Equal(6, _delays.Count);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public async Task UseBoost_refuses_when_logged_out()
    {
        _host.LoggedIn = false;
        Assert.Equal(Fail("r1", SkuaBoostCommands.NotLoggedIn), await New().UseAsync(new UseBoostCommand("r1", 17439)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public async Task UseBoost_refuses_an_id_that_is_not_in_the_published_list()
    {
        Assert.Equal(Fail("r1", SkuaBoostCommands.NotInBag), await New().UseAsync(new UseBoostCommand("r1", 123)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public async Task UseBoost_refuses_when_nothing_is_published()
    {
        _published = null;
        Assert.Equal(Fail("r1", SkuaBoostCommands.NotInBag), await New().UseAsync(new UseBoostCommand("r1", 17439)));
        _published = new SkuaBoosts(0, 0, 0, 0, 1, null);
        Assert.Equal(Fail("r1", SkuaBoostCommands.NotInBag), await New().UseAsync(new UseBoostCommand("r1", 17439)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public async Task UseBoost_refuses_a_quantity_of_zero()
    {
        _published = new SkuaBoosts(0, 0, 0, 0, 1, [new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 0)]);
        Assert.Equal(Fail("r1", SkuaBoostCommands.NotInBag), await New().UseAsync(new UseBoostCommand("r1", 17439)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public async Task UseBoost_refuses_the_unknown_type()
    {
        Assert.Equal(Fail("r1", SkuaBoostCommands.UnknownType), await New().UseAsync(new UseBoostCommand("r1", 9)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public async Task UseBoost_refuses_when_the_type_is_already_active()
    {
        _host.Values[0] = 3600;
        Assert.Equal(Fail("r1", SkuaBoostCommands.AlreadyActive), await New().UseAsync(new UseBoostCommand("r1", 17439)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public async Task UseBoost_trusts_the_game_value_not_the_published_one()
    {
        // The published list says 0 everywhere; the fresh read says the type is active: refused.
        _host.Values[0] = 1;
        Assert.Equal(Fail("r1", SkuaBoostCommands.AlreadyActive), await New().UseAsync(new UseBoostCommand("r1", 17439)));
    }

    [Fact]
    public async Task UseBoost_refuses_when_the_values_cannot_be_read()
    {
        _host.NullValues = true;
        Assert.Equal(Fail("r1", SkuaBoostCommands.CouldNotRead), await New().UseAsync(new UseBoostCommand("r1", 17439)));
        _host.NullValues = false;
        _host.ShortValues = true;
        Assert.Equal(Fail("r2", SkuaBoostCommands.CouldNotRead), await New().UseAsync(new UseBoostCommand("r2", 17439)));
        Assert.Empty(_host.Used);
    }

    [Fact]
    public void UseBoost_never_takes_a_name_or_a_count()
    {
        var names = typeof(UseBoostCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(UseBoostCommand) || p.DeclaringType == typeof(SkuaCommand))
            .Select(p => p.Name).Order().ToList();
        Assert.Equal(["ItemId", "ReqId"], names);
    }

    [Fact]
    public async Task A_host_that_throws_is_not_caught_here()
    {
        _host.ThrowOnUse = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => New().UseAsync(new UseBoostCommand("r1", 17439)));
        _host.ThrowOnUse = false;
        _host.ThrowOnRead = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => New().UseAsync(new UseBoostCommand("r1", 17439)));
    }

    [Fact]
    public void Relogin_starts_one_attempt_on_a_background_task_and_answers_ok_at_once()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        var commands = New();

        var result = commands.Relogin(new ReloginCommand("r1"));

        Assert.Equal(Ok("r1"), result);
        Assert.Empty(_host.Relogged);
        Assert.Single(_queued);
        _queued[0]();
        Assert.Equal(["Twilly"], _host.Relogged);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public void Relogin_swallows_a_host_that_throws()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        _host.ThrowOnRelogin = true;
        var commands = New();

        Assert.Equal(Ok("r1"), commands.Relogin(new ReloginCommand("r1")));
        _queued[0]();

        Assert.Equal(1, _changed);
    }

    [Fact]
    public void Relogin_refuses_when_logged_in()
    {
        _host.ServerName = "Twilly";
        Assert.Equal(Fail("r1", SkuaBoostCommands.AlreadyLoggedIn), New().Relogin(new ReloginCommand("r1")));
        Assert.Empty(_queued);
    }

    [Fact]
    public void Relogin_refuses_a_second_tap_within_the_cooldown()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        var commands = New();
        commands.Relogin(new ReloginCommand("r1"));
        _tasks[0].SetResult();
        _now = _now.AddSeconds(5);

        Assert.Equal(Fail("r2", SkuaBoostCommands.WaitAMoment), commands.Relogin(new ReloginCommand("r2")));
        Assert.Single(_queued);
    }

    [Fact]
    public void Relogin_refuses_while_the_first_login_is_still_running_after_the_cooldown()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        var commands = New();
        commands.Relogin(new ReloginCommand("r1"));
        _now = _now.AddSeconds(16);

        Assert.Equal(Fail("r2", SkuaBoostCommands.LoginRunning), commands.Relogin(new ReloginCommand("r2")));
        Assert.Single(_queued);
    }

    [Fact]
    public void Relogin_starts_again_after_the_cooldown_when_the_first_login_is_over()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        var commands = New();
        commands.Relogin(new ReloginCommand("r1"));
        _tasks[0].SetResult();
        _now = _now.AddSeconds(16);

        Assert.Equal(Ok("r2"), commands.Relogin(new ReloginCommand("r2")));
        Assert.Equal(2, _queued.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Relogin_refuses_without_a_server(string? server)
    {
        _host.LoggedIn = false;
        _host.ServerName = server;
        Assert.Equal(Fail("r1", SkuaBoostCommands.NoServer), New().Relogin(new ReloginCommand("r1")));
        Assert.Empty(_queued);
    }

    [Fact]
    public void The_cooldown_is_measured_from_the_accepted_attempt_not_from_a_refusal()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        var commands = New();
        commands.Relogin(new ReloginCommand("r1"));
        _tasks[0].SetResult();
        _now = _now.AddSeconds(10);
        Assert.Equal(Fail("r2", SkuaBoostCommands.WaitAMoment), commands.Relogin(new ReloginCommand("r2")));
        _now = _now.AddSeconds(6);

        // 16 s after the accepted attempt, only 6 s after the refused tap.
        Assert.Equal(Ok("r3"), commands.Relogin(new ReloginCommand("r3")));
    }

    [Fact]
    public async Task Relogin_never_reads_or_uses_the_boosts_and_UseBoost_never_logs_in()
    {
        _host.LoggedIn = false;
        _host.ServerName = "Twilly";
        var commands = New();
        commands.Relogin(new ReloginCommand("r1"));
        await commands.UseAsync(new UseBoostCommand("r2", 17439));
        Assert.Empty(_host.Used);
        Assert.Equal(0, _host.Reads);
    }

    internal sealed class FakeBoostHost : ISkuaBoostHost
    {
        private int _readsSinceUse = -1;

        public bool LoggedIn { get; set; } = true;
        public int[] Values { get; } = new int[4];
        public int ActivateAfterReads { get; set; } = int.MaxValue;
        public List<int> Used { get; } = [];
        public string? ServerName { get; set; }
        public List<string> Relogged { get; } = [];
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnUse { get; set; }
        public bool ThrowOnRelogin { get; set; }
        public bool NullValues { get; set; }
        public bool ShortValues { get; set; }
        public int Reads { get; private set; }

        public IReadOnlyList<int>? ReadBoostValues()
        {
            Reads++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("read");
            }

            if (NullValues)
            {
                return null;
            }

            if (ShortValues)
            {
                return [0, 0, 0];
            }

            var values = (int[])Values.Clone();
            if (_readsSinceUse >= 0)
            {
                _readsSinceUse++;
                if (_readsSinceUse > ActivateAfterReads)
                {
                    values[0] = 3600;
                }
            }

            return values;
        }

        public void UseBoost(int itemId)
        {
            if (ThrowOnUse)
            {
                throw new InvalidOperationException("use");
            }

            Used.Add(itemId);
            _readsSinceUse = 0;
        }

        public bool Relogin(string serverName)
        {
            if (ThrowOnRelogin)
            {
                throw new InvalidOperationException("relogin");
            }

            Relogged.Add(serverName);
            return true;
        }
    }
}
