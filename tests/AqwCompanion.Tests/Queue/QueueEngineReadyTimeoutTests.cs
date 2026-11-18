using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueEngineReadyTimeoutTests
{
    private static readonly QueueInputs Settling = new(true, Status(running: false, control: "settling"), [], null);
    private static readonly QueueInputs NoStatus = new(true, null, [], null);

    private static (QueueEngine Engine, FakeQueueSink Sink) Waiting(QueueInputs inputs)
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        engine.Start(inputs, T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(inputs, T0);
        return (engine, sink);
    }

    private static void AssertPausedOnce(QueueEngine engine, FakeQueueSink sink)
    {
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(QueueEngine.NotReadyReason, engine.View.Reason);
        Assert.Equal("Skua did not become ready.", QueueEngine.NotReadyReason);
        var notice = Assert.Single(sink.Notices);
        Assert.Equal(NoticeKind.QueueAttention, notice.Kind);
        Assert.Equal($"{Hero}: {QueueEngine.NotReadyReason}", notice.Body);
        Assert.Equal(0, sink.Count("start:" + A));
    }

    [Theory]
    [InlineData("settling")]
    [InlineData("stopping")]
    public void A_control_state_that_stays_busy_pauses_after_90_seconds(string control)
    {
        var busy = new QueueInputs(true, Status(running: false, control: control), [], null);
        var (engine, sink) = Waiting(busy);
        engine.Evaluate(busy, T0.AddSeconds(89));
        Assert.Equal(QueueState.Running, engine.State);
        engine.Evaluate(busy, T0.AddSeconds(91));
        AssertPausedOnce(engine, sink);
    }

    [Fact]
    public void A_missing_status_never_waits_silently()
    {
        // The engine already pauses at once on a null status (logged out), so the ready timeout is never what ends this wait.
        var (engine, sink) = Waiting(NoStatus);
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal("The character is logged out.", engine.View.Reason);
        engine.Evaluate(NoStatus, T0.AddSeconds(91));
        Assert.Single(sink.Notices);
    }

    [Fact]
    public void Becoming_idle_at_60_seconds_sends_the_start_without_a_pause()
    {
        var (engine, sink) = Waiting(Settling);
        engine.Evaluate(Idle(), T0.AddSeconds(60));
        Assert.Equal(1, sink.Count("start:" + A));
        engine.OnStartResult(new QueueOutcome(true, true), T0.AddSeconds(60));
        engine.Evaluate(Settling, T0.AddSeconds(100));   // within the start grace of the accepted start: no ready timeout
        Assert.Equal(QueueState.Running, engine.State);
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void Becoming_idle_after_the_pause_changes_nothing()
    {
        var (engine, sink) = Waiting(Settling);
        engine.Evaluate(Settling, T0.AddSeconds(91));
        engine.Evaluate(Idle(), T0.AddSeconds(92));
        AssertPausedOnce(engine, sink);
    }

    [Fact]
    public void Continue_gives_the_new_attempt_a_fresh_90_seconds()
    {
        var (engine, sink) = Waiting(Settling);
        engine.Evaluate(Settling, T0.AddSeconds(91));
        Assert.Equal(QueueState.Paused, engine.State);

        var later = T0.AddSeconds(200);
        engine.Continue(Settling, later);
        engine.OnScripts(Scripts, later);
        engine.Evaluate(Settling, later.AddSeconds(89));
        Assert.Equal(QueueState.Running, engine.State);
        Assert.Single(sink.Notices);
        engine.Evaluate(Settling, later.AddSeconds(91));
        Assert.Equal(QueueState.Paused, engine.State);
        Assert.Equal(2, sink.Notices.Count);
    }

    [Fact]
    public void A_normal_start_and_the_already_satisfied_skip_are_unchanged()
    {
        var sink = new FakeQueueSink();
        var engine = Engine(sink, Step());
        RunFirstStep(engine, sink);
        Assert.Equal(QueuePhase.Running, engine.View.Phase);

        var sink2 = new FakeQueueSink();
        var done = Engine(sink2, Step(), Step("2", B, "Butler", "Gold Voucher", 1));
        done.Start(new QueueInputs(true, Status(running: false, control: "settling"), [], Bag("Blood Orb", 10)), T0.AddSeconds(500));
        Assert.Equal(1, done.Cursor);
        Assert.Equal(QueueState.Running, done.State);
    }
}
