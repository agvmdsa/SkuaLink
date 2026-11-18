using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueEngineControlTests
{
    private static (QueueEngine Engine, FakeQueueSink Sink) Started(params QueueStep[] steps)
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, steps.Length == 0 ? [Step(), Step("2", B, "Butler", "Gold Voucher", 1)] : steps);
        RunFirstStep(engine, sink);   // the first script is running, its start was sent once at T0
        return (engine, sink);
    }

    private static void Finish(QueueEngine engine, DateTimeOffset at)
    {
        engine.Evaluate(Ended(), at);
        engine.Evaluate(Idle(), at.AddSeconds(13));
    }

    // ---- revision ----

    [Fact]
    public void Repairing_the_script_id_of_the_current_step_changes_the_revision_and_a_state_change_does_not()
    {
        var sink = new FakeQueueSink();
        var engine = new QueueEngine(Hero, sink, [Step("1", "ffffffffffffffff", "Dailies")], revision: 5);
        Assert.Equal(5, engine.View.Revision);

        engine.Start(Idle(), T0);
        Assert.Equal(5, engine.View.Revision);   // starting is a state change
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        Assert.Equal(A, engine.Steps[0].ScriptId);
        Assert.NotEqual(5, engine.View.Revision);
    }

    [Fact]
    public void An_empty_queue_has_revision_zero_whatever_it_held()
    {
        var engine = new QueueEngine(Hero, new FakeQueueSink(), [Step()], revision: 5);

        Assert.True(engine.Replace([], T0, 5).Ok);

        Assert.Equal(0, engine.View.Revision);
    }

    // ---- pause and continue ----

    [Fact]
    public void Pause_stops_nothing_and_a_paused_queue_decides_nothing()
    {
        var (engine, sink) = Started();

        Assert.True(engine.Pause(T0.AddSeconds(5)).Ok);
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(QueueEngine.PausedByYou, engine.View.Reason);
        Assert.Empty(sink.Notices);

        engine.Evaluate(Running(Bag("Blood Orb", 99)), T0.AddSeconds(10));
        Assert.Equal(0, sink.Count("stop"));
        Assert.Equal(99, engine.View.Have);
        Assert.Equal("The queue is not running.", engine.Pause(T0.AddSeconds(11)).Error);
    }

    [Fact]
    public void Continue_adopts_the_running_script_of_the_current_step_without_sending_anything()
    {
        var (engine, sink) = Started();
        engine.Pause(T0.AddSeconds(5));
        var before = sink.Calls.ToArray();

        Assert.True(engine.Continue(Running(), T0.AddSeconds(20)).Ok);

        Assert.Equal(QueueState.Running, engine.State);
        Assert.Equal(QueuePhase.Running, engine.View.Phase);
        Assert.Equal(before, sink.Calls);
    }

    [Fact]
    public void Continue_adopts_and_then_stops_it_when_the_goal_is_already_reached()
    {
        var (engine, sink) = Started();
        engine.Pause(T0.AddSeconds(5));

        engine.Continue(Running(Bag("Blood Orb", 10)), T0.AddSeconds(20));

        Assert.Equal(1, sink.Count("stop"));
        Assert.Equal(QueuePhase.Stopping, engine.View.Phase);
    }

    [Fact]
    public void Continue_refuses_while_a_different_script_runs()
    {
        var (engine, sink) = Started();
        engine.Pause(T0.AddSeconds(5));
        var before = sink.Calls.ToArray();

        var result = engine.Continue(Running(script: "Butler"), T0.AddSeconds(20));

        Assert.Equal(QueueResult.Refused(QueueEngine.AnotherScriptReason), result);
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(QueueEngine.PausedByYou, engine.View.Reason);   // a refused Continue keeps the original reason
        Assert.Equal(before, sink.Calls);
    }

    [Fact]
    public void Continue_from_idle_starts_a_new_attempt_with_its_own_start()
    {
        var (engine, sink) = Started();
        engine.Pause(T0.AddSeconds(5));

        engine.Continue(Idle(), T0.AddSeconds(20));
        Assert.Equal(2, sink.Count("scripts"));
        engine.OnScripts(Scripts, T0.AddSeconds(20));
        engine.Evaluate(Idle(), T0.AddSeconds(21));
        engine.Evaluate(Idle(), T0.AddSeconds(26));

        Assert.Equal(2, sink.Count("start:" + A));
    }

    [Fact]
    public void Continue_is_refused_unless_paused()
    {
        var stopped = Engine(new FakeQueueSink(), Step());
        Assert.False(stopped.Continue(Idle(), T0).Ok);

        var (running, _) = Started();
        Assert.False(running.Continue(Running(), T0.AddSeconds(5)).Ok);
    }

    // ---- skip ----

    [Fact]
    public void Skip_stops_the_script_the_queue_started_waits_for_idle_and_advances_without_a_notice()
    {
        var (engine, sink) = Started();

        Assert.True(engine.Skip(Running(), T0.AddSeconds(10)).Ok);
        Assert.Equal(1, sink.Count("stop"));
        Assert.Equal(QueuePhase.Stopping, engine.View.Phase);

        engine.Evaluate(Ended(control: "settling"), T0.AddSeconds(12));
        engine.Evaluate(Idle(), T0.AddSeconds(25));

        Assert.Equal(1, engine.Cursor);
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void Skip_with_nothing_of_ours_running_advances_at_once()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        engine.Start(Idle(), T0);

        Assert.True(engine.Skip(Idle(), T0.AddSeconds(1)).Ok);

        Assert.Equal(1, engine.Cursor);
        Assert.Equal(0, sink.Count("stop"));
        Assert.Equal(["scripts", "scripts"], sink.Calls);
    }

    [Fact]
    public void Skip_while_paused_stops_the_owned_script_and_leaves_the_queue_paused()
    {
        var (engine, sink) = Started();
        engine.Pause(T0.AddSeconds(5));

        Assert.True(engine.Skip(Running(), T0.AddSeconds(10)).Ok);
        Assert.Equal(1, sink.Count("stop"));
        engine.Evaluate(Ended(), T0.AddSeconds(12));
        engine.Evaluate(Idle(), T0.AddSeconds(25));

        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(1, engine.Cursor);
        Assert.Equal("Skipped. Press Continue.", engine.View.Reason);
    }

    [Fact]
    public void Skip_is_refused_while_a_start_is_in_flight()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        Assert.False(engine.Skip(Idle(), T0.AddSeconds(1)).Ok);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void Skip_on_the_last_step_finishes_the_queue()
    {
        var (engine, sink) = Started(Step());

        engine.Skip(Running(), T0.AddSeconds(10));
        Finish(engine, T0.AddSeconds(12));

        Assert.Equal(QueueState.Finished, engine.State);
        Assert.Equal(NoticeKind.QueueFinished, Assert.Single(sink.Notices).Kind);
    }

    // ---- stop queue ----

    [Fact]
    public void Stop_queue_stops_the_owned_script_then_is_stopped_with_the_cursor_kept()
    {
        var (engine, sink) = Started();

        Assert.True(engine.StopQueue(Running(), T0.AddSeconds(10)).Ok);
        Assert.Equal(1, sink.Count("stop"));
        Finish(engine, T0.AddSeconds(12));

        Assert.Equal(QueueState.Stopped, engine.State);
        Assert.Equal(0, engine.Cursor);
        Assert.Null(engine.View.Phase);
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void Stop_queue_with_nothing_running_is_stopped_at_once()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);

        Assert.True(engine.StopQueue(Idle(), T0.AddSeconds(1)).Ok);
        Assert.Equal(QueueState.Stopped, engine.State);
        Assert.Equal(0, sink.Count("stop"));

        var (paused, pausedSink) = Started();
        paused.Evaluate(new QueueInputs(false, null, [], null), T0.AddSeconds(5));
        Assert.Equal(QueueState.Paused, paused.State);
        paused.StopQueue(new QueueInputs(false, null, [], null), T0.AddSeconds(6));
        Assert.Equal(QueueState.Stopped, paused.State);
        Assert.Equal(0, pausedSink.Count("stop"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stop_queue_wins_over_skip_while_the_script_is_stopping(bool skipFirst)
    {
        var (engine, _) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        Assert.Equal(QueuePhase.Stopping, engine.View.Phase);

        if (skipFirst)
        {
            engine.Skip(Running(), T0.AddSeconds(11));
            engine.StopQueue(Running(), T0.AddSeconds(12));
        }
        else
        {
            engine.StopQueue(Running(), T0.AddSeconds(11));
            engine.Skip(Running(), T0.AddSeconds(12));
        }

        Finish(engine, T0.AddSeconds(14));

        Assert.Equal(QueueState.Stopped, engine.State);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void A_stopped_queue_keeps_its_cursor_and_start_resumes_at_that_step()
    {
        var (engine, sink) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        Finish(engine, T0.AddSeconds(12));
        Assert.Equal(1, engine.Cursor);

        engine.StopQueue(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(40));
        Assert.Equal(QueueState.Stopped, engine.State);
        Assert.Equal(1, engine.Cursor);

        engine.Start(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(50));
        Assert.Equal(3, sink.Count("scripts"));
        engine.OnScripts(Scripts, T0.AddSeconds(50));
        engine.Evaluate(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(51));

        Assert.Equal(1, sink.Count("start:" + B));
    }

    // ---- replace ----

    [Fact]
    public void Replace_is_refused_while_a_step_is_starting_stopping_or_settling()
    {
        var sink = new FakeQueueSink();
        var starting = Engine(sink, Step());
        starting.Start(Idle(), T0);
        Assert.Equal(QueueResult.Refused(QueueEngine.BusyText), starting.Replace([Step()], T0));

        var (engine, _) = Started();
        Assert.True(engine.Replace([Step()], T0.AddSeconds(2)).Ok);   // phase Running
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        Assert.Equal(QueueResult.Refused(QueueEngine.BusyText), engine.Replace([Step()], T0.AddSeconds(11)));
        engine.Evaluate(Ended(), T0.AddSeconds(12));
        Assert.Equal(QueuePhase.Settling, engine.View.Phase);
        Assert.Equal(QueueResult.Refused(QueueEngine.BusyText), engine.Replace([Step()], T0.AddSeconds(13)));

        Assert.True(Engine(sink, Step()).Replace([Step()], T0).Ok);   // Stopped
        var (paused, _) = Started();
        paused.Pause(T0.AddSeconds(3));
        Assert.True(paused.Replace([Step()], T0.AddSeconds(4)).Ok);
        var finished = new QueueEngine(Hero, sink, [Step()], 0, finished: true);
        Assert.True(finished.Replace([Step()], T0).Ok);
    }

    [Fact]
    public void Replace_keeps_the_cursor_on_the_same_step_by_id()
    {
        Assert.Equal(1, Cursored(Step("3"), Step("2"), Step("1")));
        Assert.Equal(2, Cursored(Step("4"), Step("1"), Step("2"), Step("3")));

        static int Cursored(params QueueStep[] replacement)
        {
            var engine = new QueueEngine(Hero, new FakeQueueSink(), [Step("1"), Step("2"), Step("3")], 1);
            engine.Replace(replacement, T0);
            return engine.Cursor;
        }
    }

    [Fact]
    public void Replace_that_removes_the_current_step_pauses_a_running_queue_and_only_clamps_a_stopped_one()
    {
        var (engine, sink) = Started();

        Assert.True(engine.Replace([Step("9")], T0.AddSeconds(5)).Ok);

        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(QueueEngine.StepChangedReason, engine.View.Reason);
        Assert.Equal(NoticeKind.QueueAttention, Assert.Single(sink.Notices).Kind);

        var stoppedSink = new FakeQueueSink();
        var stopped = new QueueEngine(Hero, stoppedSink, [Step("1"), Step("2")], 1);
        stopped.Replace([Step("9")], T0);
        Assert.Equal(QueueState.Stopped, stopped.State);
        Assert.Equal(0, stopped.Cursor);
        Assert.Empty(stoppedSink.Notices);
    }

    [Fact]
    public void Replace_with_no_steps_stops_the_queue_and_replacing_a_finished_queue_starts_over_stopped()
    {
        var (engine, _) = Started();
        engine.Replace([], T0.AddSeconds(5));
        Assert.Equal(QueueState.Stopped, engine.State);
        Assert.Equal(0, engine.Cursor);
        Assert.Empty(engine.Steps);

        var finished = new QueueEngine(Hero, new FakeQueueSink(), [Step()], 0, finished: true);
        Assert.Equal(QueueState.Finished, finished.State);
        finished.Replace([Step("2"), Step("3")], T0);
        Assert.Equal(QueueState.Stopped, finished.State);
        Assert.Equal(0, finished.Cursor);
    }

    [Fact]
    public void A_goal_edited_while_the_script_runs_applies_at_the_next_evaluation()
    {
        var (engine, sink) = Started();

        engine.Replace([Step(quantity: 5)], T0.AddSeconds(2));
        engine.Evaluate(Running(Bag("Blood Orb", 5)), T0.AddSeconds(3));

        Assert.Equal(1, sink.Count("stop"));
    }

    // ---- never stop what the owner started ----

    private static void Swapped(QueueEngine engine, bool byPause, DateTimeOffset at)
    {
        if (byPause)
        {
            engine.Pause(at);
        }
        else
        {
            engine.Evaluate(Running(script: "Butler"), at);   // "You changed the script."
            Assert.Equal("You changed the script.", engine.View.Reason);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Skip_and_stop_queue_never_stop_a_script_the_owner_started_by_hand(bool byPause, bool stopQueue)
    {
        var (engine, sink) = Started();
        Swapped(engine, byPause, T0.AddSeconds(5));

        var result = stopQueue ? engine.StopQueue(Running(script: "Butler"), T0.AddSeconds(10)) : engine.Skip(Running(script: "Butler"), T0.AddSeconds(10));

        Assert.True(result.Ok);
        Assert.Equal(0, sink.Count("stop"));
    }

    [Fact]
    public void Skip_and_stop_queue_are_refused_while_an_accepted_start_is_not_yet_visible()
    {
        var fresh = new FakeQueueSink();
        var e = Engine(fresh, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        e.Start(Idle(), T0);
        e.OnScripts(Scripts, T0);
        e.Evaluate(Idle(), T0);
        e.OnStartResult(new QueueOutcome(true, true), T0.AddSeconds(1));
        e.Evaluate(Idle(), T0.AddSeconds(2));   // accepted, not yet seen running

        Assert.False(e.Skip(Idle(), T0.AddSeconds(3)).Ok);
        Assert.False(e.StopQueue(Idle(), T0.AddSeconds(3)).Ok);
        Assert.Equal(0, e.Cursor);
        Assert.Equal(QueueState.Running, e.State);
        Assert.Equal(0, fresh.Count("stop"));
    }

    [Fact]
    public void A_pending_stop_queue_survives_a_problem_and_ends_stopped_not_paused()
    {
        var (engine, sink) = Started();
        engine.StopQueue(Running(), T0.AddSeconds(10));
        Assert.Equal(QueuePhase.Stopping, engine.View.Phase);

        engine.Evaluate(new QueueInputs(false, null, [], null), T0.AddSeconds(12));

        Assert.Equal(QueueState.Stopped, engine.State);
        Assert.Equal(0, engine.Cursor);
        Assert.Empty(sink.Notices);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public void A_paused_queue_with_a_start_in_doubt_can_still_skip_and_stop_without_sending_stop(bool accepted, bool disconnect, bool stopQueue)
    {
        var sink = new FakeQueueSink();
        var e = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        e.Start(Idle(), T0);
        e.OnScripts(Scripts, T0);
        e.Evaluate(Idle(), T0);
        if (accepted)
        {
            e.OnStartResult(new QueueOutcome(true, true), T0.AddSeconds(1));
            e.Evaluate(Idle(), T0.AddSeconds(2));
        }

        if (disconnect)
        {
            e.Evaluate(new QueueInputs(false, null, [], null), T0.AddSeconds(3));
        }
        else
        {
            Assert.True(e.Pause(T0.AddSeconds(3)).Ok);
        }

        Assert.Equal(QueueState.Paused, e.State);
        var result = stopQueue ? e.StopQueue(Idle(), T0.AddSeconds(4)) : e.Skip(Idle(), T0.AddSeconds(4));

        Assert.True(result.Ok);
        Assert.Equal(0, sink.Count("stop"));
        Assert.Equal(stopQueue ? QueueState.Stopped : QueueState.Paused, e.State);
        Assert.Equal(stopQueue ? 0 : 1, e.Cursor);
    }

    private static (QueueEngine Engine, FakeQueueSink Sink) UnansweredButSeen()
    {
        var sink = new FakeQueueSink();
        var e = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        e.Start(Idle(), T0);
        e.OnScripts(Scripts, T0);
        e.Evaluate(Idle(), T0);
        e.Evaluate(Running(), T0.AddSeconds(1));   // a same-name script is alive, the start is still unanswered
        Assert.Equal(QueuePhase.Running, e.View.Phase);
        return (e, sink);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unanswered_start_with_a_same_name_script_alive_refuses_skip_and_stop_queue(bool stopQueue)
    {
        var (e, sink) = UnansweredButSeen();

        var result = stopQueue ? e.StopQueue(Running(), T0.AddSeconds(2)) : e.Skip(Running(), T0.AddSeconds(2));

        Assert.False(result.Ok);
        Assert.Equal(0, sink.Count("stop"));
        Assert.Equal(QueueState.Running, e.State);
        Assert.Equal(0, e.Cursor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_same_after_pause_moves_the_queue_but_sends_no_stop(bool stopQueue)
    {
        var (e, sink) = UnansweredButSeen();
        Assert.True(e.Pause(T0.AddSeconds(2)).Ok);

        var result = stopQueue ? e.StopQueue(Running(), T0.AddSeconds(3)) : e.Skip(Running(), T0.AddSeconds(3));

        Assert.True(result.Ok);
        Assert.Equal(0, sink.Count("stop"));
        Assert.Equal(stopQueue ? QueueState.Stopped : QueueState.Paused, e.State);
        Assert.Equal(stopQueue ? 0 : 1, e.Cursor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Once_the_start_is_answered_the_owned_running_script_is_stopped_normally(bool stopQueue)
    {
        var (e, sink) = UnansweredButSeen();
        e.OnStartResult(new QueueOutcome(true, true), T0.AddSeconds(2));

        var result = stopQueue ? e.StopQueue(Running(), T0.AddSeconds(3)) : e.Skip(Running(), T0.AddSeconds(3));

        Assert.True(result.Ok);
        Assert.Equal(1, sink.Count("stop"));
    }

    [Fact]
    public void A_fresh_stop_attempt_is_not_poisoned_by_an_earlier_refusal()
    {
        var (engine, sink) = Started();
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(10));
        engine.OnStopResult(new QueueOutcome(true, false, "busy"), T0.AddSeconds(11));
        engine.Evaluate(Running(), T0.AddSeconds(12));
        Assert.Equal(QueueState.Paused, engine.State);

        Assert.True(engine.Skip(Running(), T0.AddSeconds(20)).Ok);
        engine.Evaluate(Running(), T0.AddSeconds(21));

        Assert.Equal(2, sink.Count("stop"));
        Assert.Equal(QueueState.Running, engine.State);
        Assert.Equal(QueuePhase.Stopping, engine.View.Phase);
        engine.OnStopResult(new QueueOutcome(true, true), T0.AddSeconds(22));
        engine.Evaluate(Ended(), T0.AddSeconds(23));
        engine.Evaluate(Idle(), T0.AddSeconds(36));
        Assert.Equal(1, engine.Cursor);
    }
}
