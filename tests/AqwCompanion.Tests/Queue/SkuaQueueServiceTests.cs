using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public sealed class SkuaQueueServiceTests
{
    private sealed class Rig : IDisposable
    {
        public FakeSkua Skua { get; } = new();
        public CharacterBook Book { get; }
        public MemoryQueueStore Store { get; } = new();
        public FakeDelay Delay { get; } = new();
        public DateTimeOffset Now { get; set; } = T0;
        public List<Notice> Notices { get; } = [];
        public List<Exception> Errors { get; } = [];
        public Action<Notice>? OnAnnounce { get; set; }
        public SkuaQueueService Service { get; }
        public int Changes;

        public Rig(ISkuaFeed? feed = null)
        {
            Book = new CharacterBook(new MemoryCharacterBookStore(), () => Now);
            Skua.Current = new SkuaHubView([FakeSkua.Instance("a1", "Skua 1", status: Status())]);
            Skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
            {
                ListScriptsCommand => new ResultMessage(c.ReqId, true, null, Scripts, null, null, false, false),
                _ => new ResultMessage(c.ReqId, true, null, null, null, null, false, false),
            });
            Service = new SkuaQueueService(feed ?? Skua, Skua, Book, Store, Delay, () => Now, n =>
            {
                Notices.Add(n);
                OnAnnounce?.Invoke(n);
            }, Errors.Add);
            Service.Changed += (_, _) => Interlocked.Increment(ref Changes);
        }

        public void Instances(params SkuaInstanceView[] views) => Skua.Current = new SkuaHubView(views);

        public void Script(SkuaStatus status, string id = "a1") => Instances(FakeSkua.Instance(id, "Skua", status: status));

        public Task Settle() => Service.WhenIdleAsync();

        public List<string> SentKinds() => [.. Skua.Sent.Select(s => s.Command switch
        {
            ListScriptsCommand => "list",
            StartCommand c => "start:" + c.ScriptId,
            StopCommand => "stop",
            _ => "other",
        })];

        public void Dispose() => Service.Dispose();
    }

    private static QueueView ViewOf(Rig rig, string character = Hero) => Assert.Single(rig.Service.Views, v => v.Character == character);

    private static StoredQueue Stored(string character = Hero, int cursor = 0, bool finished = false, params QueueStep[] steps) =>
        new(character, steps.Length == 0 ? [Step()] : steps, cursor, finished);

    private static async Task<Rig> StartedAsync(params QueueStep[] steps)
    {
        var rig = new Rig();
        rig.Service.Replace(Hero, steps.Length == 0 ? [Step()] : steps);
        rig.Book.OnInventory(Hero, Bag("Blood Orb", 0));
        Assert.True(rig.Service.Command(Hero, QueueCommand.Start).Ok);
        await rig.Settle();
        return rig;
    }

    [Fact]
    public void Building_the_service_touches_no_disk_and_loads_nothing_until_first_use()
    {
        using var rig = new Rig();
        Assert.Equal(0, rig.Store.Loads);

        _ = rig.Service.Views;
        _ = rig.Service.Views;

        Assert.Equal(1, rig.Store.Loads);
    }

    [Fact]
    public void Stored_queues_load_stopped_with_their_cursor()
    {
        using var rig = new Rig();
        rig.Store.Stored = [Stored(Hero, 1, false, Step("1"), Step("2")), Stored("Mara", 1, true, Step("3"))];

        var views = rig.Service.Views;

        Assert.Equal(["Hero", "Mara"], views.Select(v => v.Character).ToArray());
        Assert.Equal(QueueState.Stopped, views[0].State);
        Assert.Equal(1, views[0].Cursor);
        Assert.Equal(QueueState.Finished, views[1].State);
        Assert.DoesNotContain(views, v => v.State == QueueState.Running);
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public async Task Replace_saves_the_queue_and_raises_Changed_once()
    {
        using var rig = new Rig();

        Assert.True(rig.Service.Replace(Hero, [Step()]).Ok);

        var saved = Assert.Single(rig.Store.Saved);
        Assert.Equal(Hero, saved.Character);
        Assert.Equal([Step()], saved.Steps);
        Assert.Equal(1, rig.Changes);

        rig.Service.Command(Hero, QueueCommand.Start);
        var saves = rig.Store.Saved.Count;
        var changes = rig.Changes;
        Assert.False(rig.Service.Replace(Hero, [Step("2")]).Ok);
        await rig.Settle();
        Assert.Equal(saves, rig.Store.Saved.Count);
        Assert.Equal(changes, rig.Changes);
    }

    [Fact]
    public async Task Start_asks_for_the_list_then_sends_one_start_to_the_instance_of_the_character()
    {
        using var rig = await StartedAsync();

        Assert.Equal(["list", "start:" + A], rig.SentKinds());
        Assert.All(rig.Skua.Sent, s => Assert.Equal("a1", s.InstanceId));
        var view = ViewOf(rig);
        Assert.Equal(QueueState.Running, view.State);
        Assert.Equal(QueuePhase.Starting, view.Phase);

        rig.Service.Pump();
        await rig.Settle();
        Assert.Equal(2, rig.Skua.Sent.Count);
    }

    [Fact]
    public async Task The_character_is_matched_ignoring_case()
    {
        using var rig = new Rig();
        rig.Service.Replace("Hero", [Step()]);

        Assert.True(rig.Service.Command("hero", QueueCommand.Start).Ok);
        await rig.Settle();

        Assert.Equal(2, rig.Skua.Sent.Count);
    }

    [Fact]
    public async Task A_status_change_is_evaluated_without_waiting_for_the_timer()
    {
        using var rig = await StartedAsync();
        rig.Delay.Gate = new TaskCompletionSource();
        rig.Service.Start();
        rig.Script(Status(running: true, control: "running"));
        rig.Skua.Raise();

        rig.Book.OnInventory(Hero, Bag("Blood Orb", 10));
        await rig.Settle();

        Assert.Equal(["list", "start:" + A, "stop"], rig.SentKinds());
    }

    [Fact]
    public async Task The_whole_flow_for_two_steps()
    {
        using var rig = await StartedAsync(Step("1"), Step("2", B, "Butler", "Gold Voucher", 1));

        rig.Script(Status(running: true, control: "running"));
        rig.Service.Pump();
        Assert.Equal(QueuePhase.Running, ViewOf(rig).Phase);

        rig.Book.OnInventory(Hero, Bag("Blood Orb", 10));
        rig.Service.Pump();
        await rig.Settle();
        Assert.Equal("stop", rig.SentKinds().Last());

        rig.Script(Status(running: false, control: "settling"));
        rig.Service.Pump();
        rig.Script(Status(running: false, control: "idle"));
        rig.Service.Pump();
        await rig.Settle();
        Assert.Equal(["list", "start:" + A, "stop", "list", "start:" + B], rig.SentKinds());
        Assert.Equal(1, ViewOf(rig).Cursor);

        rig.Script(Status("Butler", running: true, control: "running"));
        rig.Service.Pump();
        rig.Book.OnInventory(Hero, Kit.Inventory(("Blood Orb", 10, SkuaProtocol.ContainerInventory), ("Gold Voucher", 1, SkuaProtocol.ContainerInventory)));
        rig.Service.Pump();
        await rig.Settle();
        Assert.Equal("stop", rig.SentKinds().Last());
        rig.Script(Status("Butler", running: false, control: "idle"));
        rig.Service.Pump();
        await rig.Settle();

        var view = ViewOf(rig);
        Assert.Equal(QueueState.Finished, view.State);
        Assert.Equal(NoticeKind.QueueFinished, Assert.Single(rig.Notices).Kind);
        var last = rig.Store.Saved.Last();
        Assert.Equal(2, last.Cursor);
        Assert.True(last.Finished);
    }

    [Fact]
    public async Task Two_characters_have_independent_queues_on_their_own_instances()
    {
        using var rig = new Rig();
        var mara = Status() with { Character = "Mara" };
        rig.Instances(
            FakeSkua.Instance("old", "Old", disconnected: true, status: Status()),
            FakeSkua.Instance("a1", "Skua 1", status: Status()),
            FakeSkua.Instance("a2", "Skua 2", status: mara));
        rig.Service.Replace(Hero, [Step()]);
        rig.Service.Replace("MARA", [Step("2", B, "Butler")]);

        rig.Service.Command(Hero, QueueCommand.Start);
        rig.Service.Command("mara", QueueCommand.Start);
        await rig.Settle();

        Assert.Equal(["start:" + A], rig.Skua.Sent.Where(s => s.InstanceId == "a1" && s.Command is StartCommand).Select(s => "start:" + ((StartCommand)s.Command).ScriptId));
        Assert.Equal(["start:" + B], rig.Skua.Sent.Where(s => s.InstanceId == "a2" && s.Command is StartCommand).Select(s => "start:" + ((StartCommand)s.Command).ScriptId));
        Assert.DoesNotContain(rig.Skua.Sent, s => s.InstanceId == "old");
        Assert.Equal(2, rig.Service.Views.Count);
    }

    [Fact]
    public async Task A_disconnected_instance_pauses_a_running_queue_and_announces_it_once()
    {
        using var rig = await StartedAsync();
        rig.Script(Status(running: true, control: "running"));
        rig.Service.Pump();

        rig.Instances(FakeSkua.Instance("a1", "Skua 1", disconnected: true, status: Status(running: true, control: "running")));
        rig.Skua.Raise();
        rig.Service.Pump();
        rig.Service.Pump();

        var notice = Assert.Single(rig.Notices);
        Assert.Equal(NoticeKind.QueueAttention, notice.Kind);
        Assert.Equal("Queue paused", notice.Title);
        Assert.Equal("Hero: Skua disconnected.", notice.Body);
        Assert.Equal(QueueState.Paused, ViewOf(rig).State);
    }

    [Fact]
    public async Task A_command_that_gets_no_answer_pauses_with_skua_did_not_answer()
    {
        using var rig = new Rig();
        var inner = rig.Skua.Answer;
        rig.Skua.Answer = (id, c) => c is StartCommand ? Task.FromResult<ResultMessage?>(null) : inner(id, c);
        rig.Service.Replace(Hero, [Step()]);

        rig.Service.Command(Hero, QueueCommand.Start);
        await rig.Settle();

        var view = ViewOf(rig);
        Assert.Equal(QueueState.Paused, view.State);
        Assert.Equal("Skua did not answer.", view.Reason);
        Assert.Equal("Hero: Skua did not answer.", Assert.Single(rig.Notices).Body);
    }

    [Theory]
    [InlineData(typeof(KeyNotFoundException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task A_command_to_a_vanished_instance_pauses_instead_of_throwing(Type exception)
    {
        using var rig = new Rig();
        var inner = rig.Skua.Answer;
        rig.Skua.Answer = (id, c) => c is StartCommand ? throw (Exception)Activator.CreateInstance(exception)! : inner(id, c);
        rig.Service.Replace(Hero, [Step()]);

        rig.Service.Command(Hero, QueueCommand.Start);
        await rig.Settle();

        Assert.Equal("Skua is disconnected.", ViewOf(rig).Reason);
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public async Task A_first_run_start_pauses_with_the_options_text()
    {
        using var rig = new Rig();
        var inner = rig.Skua.Answer;
        rig.Skua.Answer = (id, c) => c is StartCommand ? Task.FromResult<ResultMessage?>(new ResultMessage(c.ReqId, true, null, null, null, null, true, true)) : inner(id, c);
        rig.Service.Replace(Hero, [Step()]);

        rig.Service.Command(Hero, QueueCommand.Start);
        await rig.Settle();

        Assert.Equal(QueueEngine.FirstRunReason, ViewOf(rig).Reason);
        Assert.Equal(QueueState.Paused, ViewOf(rig).State);
    }

    [Fact]
    public async Task A_refused_start_pauses_with_the_plugins_text()
    {
        using var rig = new Rig();
        var inner = rig.Skua.Answer;
        rig.Skua.Answer = (id, c) => c is StartCommand ? Task.FromResult<ResultMessage?>(new ResultMessage(c.ReqId, false, "settling", null, null, null, false, false)) : inner(id, c);
        rig.Service.Replace(Hero, [Step()]);

        rig.Service.Command(Hero, QueueCommand.Start);
        await rig.Settle();

        Assert.Equal("Skua refused to start the script: settling", ViewOf(rig).Reason);
    }

    [Fact]
    public async Task A_throwing_announce_never_escapes_and_the_state_still_changes()
    {
        using var rig = await StartedAsync();
        rig.OnAnnounce = _ => throw new InvalidOperationException("boom");
        var changes = rig.Changes;

        rig.Instances(FakeSkua.Instance("a1", "Skua 1", disconnected: true, status: Status()));
        rig.Service.Pump();

        Assert.Equal(QueueState.Paused, ViewOf(rig).State);
        Assert.True(rig.Changes > changes);
        Assert.Single(rig.Notices);
    }

    [Fact]
    public async Task A_throwing_store_never_escapes_and_a_failed_save_does_not_stop_the_queue()
    {
        using var rig = new Rig();
        rig.Store.FailSave = true;

        Assert.True(rig.Service.Replace(Hero, [Step()]).Ok);
        Assert.True(rig.Service.Command(Hero, QueueCommand.Start).Ok);
        await rig.Settle();

        Assert.Equal(QueueState.Running, ViewOf(rig).State);
        Assert.Equal(2, rig.Skua.Sent.Count);
        Assert.Empty(rig.Store.Saved);

        rig.Store.FailSave = false;
        rig.Service.Command(Hero, QueueCommand.Pause);
        Assert.Equal(QueueState.Paused, ViewOf(rig).State);
        Assert.Single(rig.Store.Saved);
    }

    [Fact]
    public void A_throwing_store_load_gives_no_queues_and_never_throws()
    {
        using var rig = new Rig();
        rig.Store.FailLoad = true;

        Assert.Empty(rig.Service.Views);
        Assert.Null(rig.Service.Find(Hero));
    }

    [Fact]
    public void Commands_and_edits_made_from_two_threads_at_once_keep_every_save_in_order()
    {
        using var rig = new Rig();

        Parallel.For(1, 51, n => rig.Service.Replace(Hero, [Step("1", quantity: n), Step("2", quantity: n)]));

        var view = ViewOf(rig);
        var last = rig.Store.Saved.Last();
        Assert.Equal(view.Steps, last.Steps);
        Assert.Equal(50, rig.Store.Saved.Count);
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public async Task The_timer_loop_evaluates_every_five_seconds_and_stops_on_dispose()
    {
        using var rig = await StartedAsync();
        rig.Script(Status(running: true, control: "running"));
        rig.Service.Pump();
        rig.Book.OnInventory(Hero, Bag("Blood Orb", 10));
        rig.Script(Status(running: true, control: "running"));   // no Raise: only the timer can notice the item
        var gate = new TaskCompletionSource();
        rig.Delay.Gate = gate;
        rig.Service.Start();
        await Wait.Until(() => rig.Delay.Delays.Count == 1);
        Assert.Equal(["list", "start:" + A], rig.SentKinds());

        rig.Delay.Gate = new TaskCompletionSource();
        gate.SetResult();
        await Wait.Until(() => rig.Delay.Delays.Count >= 2);
        await rig.Settle();

        Assert.All(rig.Delay.Delays, d => Assert.Equal(SkuaQueueService.TickEvery, d));
        Assert.Equal(TimeSpan.FromSeconds(5), SkuaQueueService.TickEvery);
        Assert.Equal("stop", rig.SentKinds().Last());

        rig.Service.Dispose();
        Assert.True(rig.Delay.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Quiesce_stops_everything_and_Forget_drops_the_queues_deletes_the_files_and_ignores_late_answers()
    {
        using var rig = new Rig();
        var late = new TaskCompletionSource<ResultMessage?>();
        var inner = rig.Skua.Answer;
        rig.Skua.Answer = (id, c) => c is StartCommand ? late.Task : inner(id, c);
        rig.Service.Replace(Hero, [Step()]);
        rig.Service.Command(Hero, QueueCommand.Start);
        await Wait.Until(() => rig.Skua.Sent.Any(s => s.Command is StartCommand));
        var saves = rig.Store.Saved.Count;

        rig.Service.Quiesce();
        Assert.Equal("Try again in a moment.", rig.Service.Replace(Hero, [Step("2")]).Error);
        Assert.Equal("Try again in a moment.", rig.Service.Command(Hero, QueueCommand.Pause).Error);
        rig.Service.Forget();

        Assert.Equal(1, rig.Store.Deletes);
        Assert.Empty(rig.Service.Views);
        late.SetResult(new ResultMessage("x", true, null, null, null, null, true, true));   // would pause with a notice if it counted
        await rig.Settle();
        Assert.Empty(rig.Service.Views);
        Assert.Empty(rig.Store.Saved);
        Assert.Empty(rig.Notices);
        _ = saves;

        rig.Service.Resume();
        Assert.True(rig.Service.Replace(Hero, [Step()]).Ok);
        Assert.Single(rig.Service.Views);
    }

    [Fact]
    public async Task Replace_during_a_running_step_is_refused_with_the_engines_text()
    {
        using var rig = await StartedAsync();

        var result = rig.Service.Replace(Hero, [Step("9")]);

        Assert.False(result.Ok);
        Assert.Equal(QueueEngine.BusyText, result.Error);
    }

    private sealed class ThrowingFeed : ISkuaFeed
    {
        public SkuaHubView Current => throw new InvalidOperationException("feed");

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    [Fact]
    public void Pump_never_throws_when_the_feed_throws()
    {
        using var rig = new Rig(new ThrowingFeed());
        rig.Service.Replace(Hero, [Step()]);

        rig.Service.Pump();
        Assert.NotNull(rig.Service.Command(Hero, QueueCommand.Start));

        Assert.NotEmpty(rig.Errors);
    }

    [Fact]
    public async Task A_late_answer_for_an_earlier_attempt_is_dropped()
    {
        using var rig = new Rig();
        var answers = new List<TaskCompletionSource<ResultMessage?>>();
        var inner = rig.Skua.Answer;
        rig.Skua.Answer = (id, c) =>
        {
            if (c is not StartCommand)
            {
                return inner(id, c);
            }

            var gate = new TaskCompletionSource<ResultMessage?>();
            lock (answers)
            {
                answers.Add(gate);
            }

            return gate.Task;
        };
        rig.Service.Replace(Hero, [Step()]);
        rig.Service.Command(Hero, QueueCommand.Start);
        await Wait.Until(() => rig.Skua.Sent.Count(s => s.Command is StartCommand) == 1);

        // A pause and Continue begin a second attempt while the first start is still unanswered.
        Assert.True(rig.Service.Command(Hero, QueueCommand.Pause).Ok);
        Assert.True(rig.Service.Command(Hero, QueueCommand.Continue).Ok);
        await Wait.Until(() => rig.Skua.Sent.Count(s => s.Command is StartCommand) == 2);

        answers[0].SetResult(new ResultMessage("x", false, "not found", null, null, null, false, false));   // the first attempt's answer
        await Wait.Until(() => answers[0].Task.IsCompleted);
        await Task.Delay(50);

        Assert.Equal(QueueState.Running, ViewOf(rig).State);
        Assert.Empty(rig.Notices);

        answers[1].SetResult(new ResultMessage("y", true, null, null, null, null, false, false));
        await rig.Settle();
        Assert.Equal(QueueState.Running, ViewOf(rig).State);
        Assert.Equal(2, rig.Skua.Sent.Count(s => s.Command is StartCommand));
    }
}
