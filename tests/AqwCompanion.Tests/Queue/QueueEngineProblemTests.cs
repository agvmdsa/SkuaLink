using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueEngineProblemTests
{
    private static (QueueEngine Engine, FakeQueueSink Sink) Started(params QueueStep[] steps)
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, steps.Length == 0 ? [Step(), Step("2", B, "Butler", "Gold Voucher", 1)] : steps);
        RunFirstStep(engine, sink);
        return (engine, sink);
    }

    private static QueueInputs With(QueueInputs inputs, params SkuaLogEntry[] log) => inputs with { Log = log };

    private static SkuaLogEntry Entry(SkuaLogKind kind, string text, DateTimeOffset at) => new(at, kind, text);

    private static void AssertPausedOnce(QueueEngine engine, FakeQueueSink sink, string reason)
    {
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(reason, engine.View.Reason);
        var notice = Assert.Single(sink.Notices);
        Assert.Equal(NoticeKind.QueueAttention, notice.Kind);
        Assert.Equal("Queue paused", notice.Title);
        Assert.Equal($"{Hero}: {reason}", notice.Body);
        Assert.Equal(1, sink.Count("start:" + A));
    }

    // ---- the connection and the plugin ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_disconnected_plugin_pauses(int phase)
    {
        var (engine, sink) = Started();
        if (phase >= 1)
        {
            engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
            Assert.Equal(QueuePhase.Stopping, engine.View.Phase);
        }

        if (phase >= 2)
        {
            engine.Evaluate(Ended(control: "stopping"), T0.AddSeconds(11));
            Assert.Equal(QueuePhase.Settling, engine.View.Phase);
        }

        engine.Evaluate(new QueueInputs(false, null, [], null), T0.AddSeconds(20));

        AssertPausedOnce(engine, sink, "Skua disconnected.");
    }

    [Fact]
    public void A_logged_out_character_pauses()
    {
        var (engine, sink) = Started();
        engine.Evaluate(new QueueInputs(true, Status(loggedIn: false), [], null), T0.AddSeconds(5));
        AssertPausedOnce(engine, sink, "The character is logged out.");
    }

    [Fact]
    public void A_plugin_without_control_pauses_with_the_update_text()
    {
        var (engine, sink) = Started();
        engine.Evaluate(new QueueInputs(true, Status(running: true, control: null), [], null), T0.AddSeconds(5));
        AssertPausedOnce(engine, sink, QueueEngine.OldPluginReason);
    }

    [Fact]
    public void Options_stuck_pauses_with_the_options_window_text()
    {
        var (engine, sink) = Started();
        engine.Evaluate(new QueueInputs(true, Status(running: true, control: "awaitingOptions", stuck: true), [], null), T0.AddSeconds(5));
        AssertPausedOnce(engine, sink, QueueEngine.OptionsStuckReason);
    }

    // ---- the log ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_script_error_after_the_start_pauses_with_its_first_line(bool scriptAlreadyStopped)
    {
        var (engine, sink) = Started();
        var error = Entry(SkuaLogKind.ScriptError, "Script stopped with an error: NullReferenceException at X\nstack...", T0.AddSeconds(5));

        engine.Evaluate(With(scriptAlreadyStopped ? Ended() : Running(), error), T0.AddSeconds(6));

        AssertPausedOnce(engine, sink, "The script failed: NullReferenceException at X");
    }

    [Fact]
    public void An_error_from_before_the_start_is_ignored()
    {
        var (engine, sink) = Started();
        var old = Entry(SkuaLogKind.ScriptError, "Script stopped with an error: Old", T0.AddSeconds(-10));

        engine.Evaluate(With(Running(), old), T0.AddSeconds(6));

        Assert.Equal(QueueState.Running, engine.State);
        Assert.Empty(sink.Notices);
    }

    [Theory]
    [InlineData(SkuaLogKind.LoggedOut, false)]
    [InlineData(SkuaLogKind.Relogin, false)]
    [InlineData(SkuaLogKind.LoggedOut, true)]
    public void A_logout_or_a_kick_in_the_log_pauses_even_when_the_script_ended_without_an_error_line(SkuaLogKind kind, bool ended)
    {
        var (engine, sink) = Started();

        engine.Evaluate(With(ended ? Ended() : Running(), Entry(kind, "x", T0.AddSeconds(5))), T0.AddSeconds(6));

        AssertPausedOnce(engine, sink, "The character was disconnected.");
    }

    // ---- the owner's own changes ----

    [Fact]
    public void Another_script_started_by_hand_pauses_with_you_changed_the_script()
    {
        var (engine, sink) = Started();

        engine.Evaluate(Running(script: "Butler"), T0.AddSeconds(5));

        AssertPausedOnce(engine, sink, "You changed the script.");
        Assert.Equal(0, sink.Count("stop"));
    }

    [Fact]
    public void A_different_script_while_the_queue_watches_pauses_even_with_the_goal_reached_and_is_never_stopped_or_restarted()
    {
        var (engine, sink) = Started();
        Assert.Equal(QueuePhase.Running, engine.View.Phase);

        engine.Evaluate(Running(Bag("Blood Orb", 10), script: "Butler"), T0.AddSeconds(5));
        engine.Evaluate(Running(Bag("Blood Orb", 10), script: "Butler"), T0.AddSeconds(10));
        engine.Evaluate(Idle(), T0.AddSeconds(20));

        AssertPausedOnce(engine, sink, "You changed the script.");
        Assert.Equal(["scripts", "start:" + A], sink.Calls);
    }

    [Fact]
    public void A_manual_stop_looks_like_the_script_ending_by_itself()
    {
        var (engine, sink) = Started();

        engine.Evaluate(Ended(), T0.AddSeconds(30));
        Assert.Equal(QueueState.Running, engine.State);
        engine.Evaluate(Ended(), T0.AddSeconds(30) + QueueEngine.EndGrace);
        engine.Evaluate(Idle(), T0.AddSeconds(50) + QueueEngine.EndGrace);

        Assert.Equal(QueueState.Running, engine.State);
        Assert.Equal(1, engine.Cursor);
        Assert.Contains("ended without", Assert.Single(sink.Notices).Body);
    }

    // ---- the time cap ----

    [Fact]
    public void A_time_cap_stops_the_script_pauses_after_idle_with_one_notice()
    {
        var (engine, sink) = Started(Step(minutes: 2), Step("2", B, "Butler", "Gold Voucher", 1));

        engine.Evaluate(Running(), T0.AddSeconds(119));
        Assert.Equal(0, sink.Count("stop"));
        engine.Evaluate(Running(), T0.AddSeconds(120));
        engine.Evaluate(Running(), T0.AddSeconds(121));
        Assert.Equal(1, sink.Count("stop"));
        Assert.Equal("Dailies hit its time limit.", Assert.Single(sink.Notices).Body);

        engine.Evaluate(Ended(), T0.AddSeconds(125));
        engine.Evaluate(Idle(), T0.AddSeconds(140));

        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal("Dailies hit its time limit.", engine.View.Reason);
        Assert.Single(sink.Notices);
        Assert.Equal(0, engine.Cursor);

        engine.Continue(Idle(), T0.AddSeconds(200));
        engine.OnScripts(Scripts, T0.AddSeconds(200));
        engine.Evaluate(Idle(), T0.AddSeconds(200));
        engine.OnStartResult(new QueueOutcome(true, true), T0.AddSeconds(200));
        Assert.Equal(2, sink.Count("start:" + A));
        engine.Evaluate(Running(), T0.AddSeconds(201));
        engine.Evaluate(Running(), T0.AddSeconds(319));
        Assert.Equal(1, sink.Count("stop"));   // a fresh timer, not the old one
        engine.Evaluate(Running(), T0.AddSeconds(320));
        Assert.Equal(2, sink.Count("stop"));
    }

    [Fact]
    public void A_goal_reached_at_the_same_time_as_the_cap_is_a_success_not_a_pause()
    {
        var (engine, sink) = Started(Step(minutes: 2), Step("2", B, "Butler", "Gold Voucher", 1));

        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(120));
        engine.Evaluate(Ended(Bag("Blood Orb", 10)), T0.AddSeconds(122));
        engine.Evaluate(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(135));

        Assert.Equal(1, sink.Count("stop"));
        Assert.Equal(QueueState.Running, engine.State);
        Assert.Equal(1, engine.Cursor);
        Assert.Empty(sink.Notices);
    }

    // ---- stop and settle trouble ----

    [Fact]
    public void A_stop_the_plugin_refuses_while_the_script_still_runs_pauses_but_not_when_it_ended()
    {
        var (engine, sink) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        engine.OnStopResult(new QueueOutcome(true, false, "not running"), T0.AddSeconds(11));
        engine.Evaluate(Running(), T0.AddSeconds(12));
        AssertPausedOnce(engine, sink, "Skua refused to stop the script: not running");

        var (other, otherSink) = Started();
        other.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        other.OnStopResult(new QueueOutcome(true, false, "not running"), T0.AddSeconds(11));
        other.Evaluate(Ended(control: "settling"), T0.AddSeconds(12));
        other.Evaluate(Idle(), T0.AddSeconds(25));
        Assert.Equal(QueueState.Running, other.State);
        Assert.Equal(1, other.Cursor);
        Assert.Empty(otherSink.Notices);
    }

    [Fact]
    public void A_stop_that_gets_no_answer_pauses_with_skua_did_not_answer()
    {
        var (engine, sink) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));

        engine.OnStopResult(new QueueOutcome(false, false), T0.AddSeconds(25));

        AssertPausedOnce(engine, sink, QueueEngine.NoAnswerReason);
    }

    [Fact]
    public void A_script_that_does_not_stop_in_two_minutes_pauses()
    {
        var (engine, sink) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        engine.Evaluate(Running(), T0.AddSeconds(10) + QueueEngine.SettleTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(QueueState.Running, engine.State);

        engine.Evaluate(Running(), T0.AddSeconds(10) + QueueEngine.SettleTimeout);

        AssertPausedOnce(engine, sink, "Skua did not stop the script.");
    }

    [Fact]
    public void A_settle_that_never_reaches_idle_pauses()
    {
        var (engine, sink) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        engine.Evaluate(Ended(), T0.AddSeconds(11));
        Assert.Equal(QueuePhase.Settling, engine.View.Phase);

        engine.Evaluate(Ended(), T0.AddSeconds(10) + QueueEngine.SettleTimeout);

        AssertPausedOnce(engine, sink, "Skua did not settle.");
    }

    // ---- nothing retries ----

    [Fact]
    public void Nothing_is_retried_after_a_pause()
    {
        var (engine, sink) = Started();
        engine.Evaluate(With(Running(), Entry(SkuaLogKind.ScriptError, "Script stopped with an error: Boom", T0.AddSeconds(5))), T0.AddSeconds(6));

        for (var n = 0; n < 10; n++)
        {
            engine.Evaluate(Idle(), T0.AddSeconds(10 + n * 5));
        }

        AssertPausedOnce(engine, sink, "The script failed: Boom");
        Assert.False(engine.Continue(Running(script: "Butler"), T0.AddSeconds(100)).Ok);
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Single(sink.Notices);
        Assert.Equal(["scripts", "start:" + A], sink.Calls);
    }

    [Fact]
    public void A_pause_by_the_owner_makes_no_notice_but_a_problem_does()
    {
        var (engine, sink) = Started();
        engine.Pause(T0.AddSeconds(5));
        Assert.Empty(sink.Notices);

        var (other, otherSink) = Started();
        other.Evaluate(new QueueInputs(false, null, [], null), T0.AddSeconds(5));
        Assert.Single(otherSink.Notices);
    }
}
