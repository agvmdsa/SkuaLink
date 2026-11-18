using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueEngineFlowTests
{
    // ---- starting a step ----

    [Fact]
    public void Start_asks_for_the_script_list_once_and_sends_one_start_only_when_idle()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());

        Assert.True(engine.Start(Idle(), T0).Ok);
        engine.Evaluate(Idle(), T0.AddSeconds(5));
        Assert.Equal(["scripts"], sink.Calls);
        Assert.Equal(QueueState.Running, engine.View.State);
        Assert.Equal(QueuePhase.Starting, engine.View.Phase);

        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0.AddSeconds(5));
        engine.Evaluate(Idle(), T0.AddSeconds(10));

        Assert.Equal(["scripts", "start:" + A], sink.Calls);
    }

    [Theory]
    [InlineData("settling")]
    [InlineData("stopping")]
    public void It_waits_while_the_previous_script_settles(string control)
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(new QueueInputs(true, Status(running: false, control: control), [], null), T0);
        engine.OnScripts(Scripts, T0);

        engine.Evaluate(new QueueInputs(true, Status(running: false, control: control), [], null), T0.AddSeconds(5));
        Assert.Equal(["scripts"], sink.Calls);

        engine.Evaluate(Idle(), T0.AddSeconds(15));
        Assert.Equal(["scripts", "start:" + A], sink.Calls);
    }

    [Fact]
    public void A_step_whose_item_is_already_in_the_bag_is_skipped_at_once_with_a_notice()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(item: "Blood Orb", quantity: 10), Step("2", B, "Butler", "Gold Voucher", 1));

        engine.Start(Idle(Bag("blood orb ", 12)), T0);

        Assert.Equal(1, engine.View.Cursor);
        Assert.Equal(["scripts"], sink.Calls);   // the next step now asks for the list; nothing was started
        var notice = Assert.Single(sink.Notices);
        Assert.Equal(NoticeKind.QueueAttention, notice.Kind);
        Assert.Equal("Queue step skipped", notice.Title);
        Assert.Equal("Already had Blood Orb. Skipped Dailies.", notice.Body);
        Assert.Equal(Hero, notice.Character);
    }

    [Fact]
    public void When_every_step_is_already_satisfied_the_queue_finishes_with_the_finished_notice()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());

        engine.Start(Idle(Bag("Blood Orb", 10)), T0);

        Assert.Equal(QueueState.Finished, engine.View.State);
        Assert.Equal(1, engine.View.Cursor);
        Assert.Equal([NoticeKind.QueueAttention, NoticeKind.QueueFinished], sink.Notices.Select(n => n.Kind).ToArray());
        Assert.Equal("Queue finished", sink.Notices[1].Title);
        Assert.Empty(sink.Calls);
    }

    // ---- counting the goal ----

    [Fact]
    public void Only_the_inventory_container_counts_names_match_exactly_and_stacks_add_up()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(item: "Blood Orb", quantity: 10));
        var inventory = Kit.Inventory(("Blood Orb", 4, "inv"), ("blood orb", 3, "inv"), ("Blood Orb", 100, "bank"), ("Blood Orbs", 100, "inv"), ("Blood Orb", 100, "house"));

        engine.Start(Idle(inventory), T0);

        Assert.Equal(QueueState.Running, engine.View.State);
        Assert.Equal(7, engine.View.Have);
    }

    [Fact]
    public void A_reputation_rank_reaches_the_step_even_without_the_item()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(rep: new ReputationGoal("ebilcorp", 10)));
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);
        engine.OnStartResult(new QueueOutcome(true, true), T0);
        var factions = new[] { new SkuaFaction(1, "Ebilcorp", 10, 5, 0, 0) };
        var running = new QueueInputs(true, Status(running: true, control: "running", factions: factions), [], null);

        engine.Evaluate(running, T0.AddSeconds(1));

        Assert.Equal(["scripts", "start:" + A, "stop"], sink.Calls);
    }

    // ---- reaching the goal ----

    [Fact]
    public void Reaching_the_goal_sends_one_stop_waits_for_idle_and_starts_the_next_step()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        RunFirstStep(engine, sink);
        Assert.Equal(QueuePhase.Running, engine.View.Phase);

        engine.Evaluate(Running(Bag("Blood Orb", 9)), T0.AddSeconds(10));
        Assert.Equal(0, sink.Count("stop"));   // no stop below the goal
        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(20));
        engine.Evaluate(Running(Bag("Blood Orb", 11)), T0.AddSeconds(21));
        Assert.Equal(1, sink.Count("stop"));
        Assert.Equal(QueuePhase.Stopping, engine.View.Phase);
        Assert.False(engine.View.CanEdit);

        engine.OnStopResult(new QueueOutcome(true, true), T0.AddSeconds(21));
        engine.Evaluate(Ended(Bag("Blood Orb", 11), "stopping"), T0.AddSeconds(22));
        engine.Evaluate(Ended(Bag("Blood Orb", 11), "settling"), T0.AddSeconds(25));
        Assert.Equal(QueuePhase.Settling, engine.View.Phase);
        Assert.Equal(0, engine.View.Cursor);

        engine.Evaluate(Idle(Bag("Blood Orb", 11)), T0.AddSeconds(35));

        Assert.Equal(1, engine.View.Cursor);
        Assert.Equal(QueuePhase.Starting, engine.View.Phase);
        Assert.Equal(1, sink.Count("stop"));
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void After_the_last_goal_the_queue_finishes_with_one_finished_notice()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        RunFirstStep(engine, sink);

        engine.Evaluate(Running(Bag("Blood Orb", 10)), T0.AddSeconds(20));
        engine.Evaluate(Ended(Bag("Blood Orb", 10), "settling"), T0.AddSeconds(22));
        engine.Evaluate(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(35));
        engine.Evaluate(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(40));

        Assert.Equal(QueueState.Finished, engine.View.State);
        var notice = Assert.Single(sink.Notices);
        Assert.Equal(NoticeKind.QueueFinished, notice.Kind);
        Assert.Equal("Hero: the queue finished.", notice.Body);
    }

    // ---- ending by itself ----

    [Fact]
    public void A_script_that_ends_without_the_item_is_skipped_after_the_grace_with_the_daily_notice_and_no_stop()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        RunFirstStep(engine, sink);

        engine.Evaluate(Ended(), T0.AddSeconds(30));
        engine.Evaluate(Ended(), T0.AddSeconds(30) + QueueEngine.EndGrace - TimeSpan.FromSeconds(1));
        Assert.Empty(sink.Notices);
        Assert.Equal(QueuePhase.Running, engine.View.Phase);

        engine.Evaluate(Ended(), T0.AddSeconds(30) + QueueEngine.EndGrace);

        var notice = Assert.Single(sink.Notices);
        Assert.Equal(NoticeKind.QueueAttention, notice.Kind);
        Assert.Equal("The step Dailies ended without Blood Orb. It may be a daily you already did today.", notice.Body);
        Assert.Equal(QueuePhase.Settling, engine.View.Phase);

        engine.Evaluate(Idle(), T0.AddSeconds(60));

        Assert.Equal(1, engine.View.Cursor);
        Assert.Equal(0, sink.Count("stop"));
    }

    [Fact]
    public void The_last_step_ending_without_the_item_finishes_the_queue_with_both_notices()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        RunFirstStep(engine, sink);

        engine.Evaluate(Ended(), T0.AddSeconds(30));
        engine.Evaluate(Ended(), T0.AddSeconds(30) + QueueEngine.EndGrace);
        engine.Evaluate(Idle(), T0.AddSeconds(70));

        Assert.Equal(QueueState.Finished, engine.View.State);
        Assert.Equal([NoticeKind.QueueAttention, NoticeKind.QueueFinished], sink.Notices.Select(n => n.Kind).ToArray());
    }

    [Fact]
    public void The_item_arriving_inside_the_grace_is_a_normal_success_with_no_notice()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        RunFirstStep(engine, sink);
        engine.Evaluate(Ended(), T0.AddSeconds(30));

        engine.Evaluate(Ended(Bag("Blood Orb", 10)), T0.AddSeconds(38));
        engine.Evaluate(Idle(Bag("Blood Orb", 10)), T0.AddSeconds(50));

        Assert.Empty(sink.Notices);
        Assert.Equal(1, engine.View.Cursor);
        Assert.Equal(0, sink.Count("stop"));
    }

    [Fact]
    public void A_start_that_was_accepted_but_never_seen_running_counts_as_ended_after_the_grace_periods()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);
        engine.OnStartResult(new QueueOutcome(true, true), T0.AddSeconds(2));

        engine.Evaluate(Ended(), T0.AddSeconds(3));
        Assert.Equal(QueuePhase.Starting, engine.View.Phase);   // inside StartGrace the "not running" is not believed yet

        var later = T0.AddSeconds(2) + QueueEngine.StartGrace;
        engine.Evaluate(Ended(), later);
        engine.Evaluate(Ended(), later + QueueEngine.EndGrace);

        Assert.Equal(QueuePhase.Settling, engine.View.Phase);
        Assert.Contains("ended without Blood Orb", Assert.Single(sink.Notices).Body, StringComparison.Ordinal);
    }

    // ---- finding the script ----

    [Fact]
    public void A_moved_script_is_found_again_by_its_unique_name_and_the_step_keeps_the_new_id()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(scriptId: "1111111111111111"));
        engine.Start(Idle(), T0);

        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        Assert.Equal(["scripts", "start:" + A], sink.Calls);
        Assert.Equal(A, engine.Steps[0].ScriptId);
    }

    [Fact]
    public void A_script_that_is_gone_or_ambiguous_pauses_with_script_not_found_and_a_notice()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step(scriptId: "1111111111111111", name: "Gone"));
        engine.Start(Idle(), T0);

        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        Assert.Equal(QueueState.Paused, engine.View.State);
        Assert.Equal(QueueEngine.NotFoundReason, engine.View.Reason);
        Assert.Equal(["scripts"], sink.Calls);
        Assert.Equal("Queue paused", Assert.Single(sink.Notices).Title);

        var twins = new[] { new SkuaScriptInfo(A, "Dailies", "Farm"), new SkuaScriptInfo(B, "dailies", "Misc") };
        var sink2 = new FakeQueueSink();
        var engine2 = Engine(sink2, Step(scriptId: "1111111111111111"));
        engine2.Start(Idle(), T0);
        engine2.OnScripts(twins, T0);
        engine2.Evaluate(Idle(), T0);
        Assert.Equal(QueueState.Paused, engine2.View.State);
    }

    [Fact]
    public void A_script_list_that_gets_no_answer_pauses_the_queue()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);

        engine.OnScripts(null, T0.AddSeconds(15));

        Assert.Equal(QueueState.Paused, engine.View.State);
        Assert.Equal(QueueEngine.NoAnswerReason, engine.View.Reason);
    }

    // ---- start answers ----

    [Fact]
    public void A_start_with_no_answer_pauses_and_sends_nothing_again()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        engine.OnStartResult(new QueueOutcome(false, false), T0.AddSeconds(45));
        engine.Evaluate(Idle(), T0.AddSeconds(50));

        Assert.Equal(QueueState.Paused, engine.View.State);
        Assert.Equal(QueueEngine.NoAnswerReason, engine.View.Reason);
        Assert.Equal(1, sink.Calls.Count(c => c.StartsWith("start:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("unknown script id")]
    [InlineData("not found")]
    public void A_start_the_plugin_does_not_know_pauses_with_script_not_found(string error)
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        engine.OnStartResult(new QueueOutcome(true, false, error), T0);

        Assert.Equal(QueueEngine.NotFoundReason, engine.View.Reason);
    }

    [Fact]
    public void A_start_the_plugin_refuses_pauses_with_its_text_and_a_compile_error_is_shown_as_it_is()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        engine.OnStartResult(new QueueOutcome(true, false, "CS1002: ; expected"), T0);

        Assert.Equal(QueueState.Paused, engine.View.State);
        Assert.Equal("Skua refused to start the script: CS1002: ; expected", engine.View.Reason);
    }

    [Fact]
    public void A_first_run_start_pauses_because_the_plugin_stopped_the_script_at_once()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(), T0);

        engine.OnStartResult(new QueueOutcome(true, true, FirstRun: true), T0);

        Assert.Equal(QueueState.Paused, engine.View.State);
        Assert.Equal(QueueEngine.FirstRunReason, engine.View.Reason);
    }

    // ---- bookkeeping ----

    [Fact]
    public void Every_state_change_tells_the_sink_and_an_unchanged_evaluation_does_not()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(Idle(), T0);
        var afterStart = sink.Changes;

        engine.Evaluate(Idle(), T0.AddSeconds(5));
        engine.Evaluate(Idle(), T0.AddSeconds(10));

        Assert.True(afterStart > 0);
        Assert.Equal(afterStart, sink.Changes);
    }

    [Fact]
    public void A_stopped_queue_decides_nothing()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());

        engine.Evaluate(Idle(), T0);

        Assert.Empty(sink.Calls);
        Assert.Equal(QueueState.Stopped, engine.View.State);
        Assert.Null(engine.View.Phase);
    }

    [Fact]
    public void Start_refuses_an_empty_queue_and_a_running_one()
    {
        var empty = Engine(new FakeQueueSink());
        Assert.False(empty.Start(Idle(), T0).Ok);

        var engine = Engine(new FakeQueueSink(), Step());
        Assert.True(engine.Start(Idle(), T0).Ok);
        Assert.False(engine.Start(Idle(), T0).Ok);
    }

    [Fact]
    public void A_finished_queue_starts_again_at_the_first_step()
    {
        var sink = new FakeQueueSink();
        var engine = new QueueEngine(Hero, sink, [Step(), Step("2", B, "Butler", "Gold Voucher", 1)], cursor: 2, finished: true);
        Assert.Equal(QueueState.Finished, engine.View.State);
        Assert.Equal(2, engine.View.Cursor);

        Assert.True(engine.Start(Idle(), T0).Ok);

        Assert.Equal(0, engine.View.Cursor);
        Assert.Equal(QueueState.Running, engine.View.State);
    }
}
