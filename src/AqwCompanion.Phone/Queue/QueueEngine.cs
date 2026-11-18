using AqwCompanion.Phone.Notices;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Queue;

/// <summary>What the engine does to the world. Never blocks and never throws; the caller performs the command and reports the answer.</summary>
public interface IQueueSink
{
    /// <summary>Ask the plugin for its script list; answer with <see cref="QueueEngine.OnScripts"/>.</summary>
    void RequestScripts();

    /// <summary>Send <c>start</c>; answer with <see cref="QueueEngine.OnStartResult"/>.</summary>
    void SendStart(string scriptId);

    /// <summary>Send <c>stop</c>; answer with <see cref="QueueEngine.OnStopResult"/>.</summary>
    void SendStop();

    void Notify(Notice notice);

    /// <summary>The view or the steps changed (persist and publish).</summary>
    void Changed();
}

/// <summary>What the engine reads: the plugin's connection, the instance's status and log (newest first), the character's inventory snapshot.</summary>
public sealed record QueueInputs(bool Connected, SkuaStatus? Status, IReadOnlyList<SkuaLogEntry> Log, SkuaInventory? Inventory);

/// <summary>A command's answer. <paramref name="Answered"/> false: no answer in time or the pipe closed (<paramref name="Error"/> may say why).</summary>
public sealed record QueueOutcome(bool Answered, bool Ok, string? Error = null, bool FirstRun = false);

/// <summary>
/// One queue, one character. Single-threaded: the caller serialises every call. It decides from the inputs and the <c>now</c> it is given
/// (no clock, no I/O); at most one <c>start</c> per attempt and nothing is ever retried.
/// </summary>
public sealed partial class QueueEngine
{
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(2);

    public const string ChangedReason = "You changed the script.";
    public const string NotFoundReason = "Script not found. Choose it again in the queue.";
    public const string NoAnswerReason = "Skua did not answer.";
    public const string FirstRunReason = "This script has options that were never saved. Start it once from the phone, set its options, then press Continue.";

    private enum ExitKind { Advance, AdvanceThenPause, Pause, Halt }

    private sealed record Exit(ExitKind Kind, string? Reason = null);

    /// <summary>Everything about the current attempt at the current step; a new attempt starts with a new one.</summary>
    private sealed class Attempt
    {
        public bool ScriptsAsked;
        public bool StartSent;
        public bool StartAnswered;
        public DateTimeOffset StartedAt;
        public DateTimeOffset? StartOkAt;
        public bool Owns;
        public DateTimeOffset? EndedSeenAt;
        public bool StopSent;
        public bool StopAnswered;
        public string? StopRefused;
        public DateTimeOffset ExitAt;
        public Exit? Exit;
    }

    private readonly string _character;
    private readonly IQueueSink _sink;
    private List<QueueStep> _steps;
    private int _cursor;
    private QueueState _state;
    private QueuePhase _phase = QueuePhase.Starting;
    private string? _reason;
    private int? _have;
    private Attempt _a = new();
    private IReadOnlyList<SkuaScriptInfo>? _scripts;
    private bool _dirty;

    public QueueEngine(string character, IQueueSink sink, IReadOnlyList<QueueStep>? steps = null, int cursor = 0, bool finished = false)
    {
        _character = character;
        _sink = sink;
        _steps = [.. steps ?? []];
        _state = finished && _steps.Count > 0 ? QueueState.Finished : QueueState.Stopped;
        _cursor = _state == QueueState.Finished ? _steps.Count : Math.Clamp(cursor, 0, Math.Max(0, _steps.Count - 1));
    }

    public string Character => _character;

    /// <summary>
    /// Identifies the current attempt: a new object for every attempt. The caller takes it when the engine asks for a command and drops the
    /// answer when the engine has moved on to another attempt.
    /// </summary>
    public object AttemptToken => _a;

    public IReadOnlyList<QueueStep> Steps => _steps;

    public int Cursor => _cursor;

    public QueueState State => _state;

    /// <summary>False while a step is starting, stopping or settling: the step list is then not replaced.</summary>
    public bool CanEdit => !(_state == QueueState.Running && _phase != QueuePhase.Running);

    public QueueView View => new(_character, [.. _steps], _cursor, _state, _state == QueueState.Running ? _phase : null, _reason, _have, CanEdit);

    /// <summary>Begins at the cursor step (at the first step after <c>Finished</c>). Only from <c>Stopped</c> or <c>Finished</c>.</summary>
    public QueueResult Start(QueueInputs inputs, DateTimeOffset now)
    {
        if (_state == QueueState.Running)
        {
            return QueueResult.Refused("The queue is already running.");
        }

        if (_state == QueueState.Paused)
        {
            return QueueResult.Refused("The queue is paused. Press Continue.");
        }

        if (_steps.Count == 0)
        {
            return QueueResult.Refused("The queue has no steps.");
        }

        if (_state == QueueState.Finished || _cursor >= _steps.Count)
        {
            _cursor = 0;
        }

        BeginAttempt();
        Evaluate(inputs, now);
        return QueueResult.Done;
    }

    /// <summary>Re-reads the world. Call it on every status change and on the 5 s timer.</summary>
    public void Evaluate(QueueInputs inputs, DateTimeOffset now)
    {
        // A step that ends (or is skipped) starts the next attempt in the same call, so a chain of satisfied steps does not wait 5 s per step.
        for (var guard = 0; guard <= QueueLimits.MaxSteps; guard++)
        {
            if ((_state != QueueState.Running && _state != QueueState.Paused) || _cursor >= _steps.Count)
            {
                break;
            }

            var step = _steps[_cursor];
            SetHave(Have(inputs.Inventory, step));
            if (_state != QueueState.Running)
            {
                break;
            }

            var attempt = _a;
            Step(inputs, step, now);
            if (ReferenceEquals(attempt, _a))
            {
                break;   // nothing moved on to a new attempt
            }
        }

        Flush();
    }

    /// <summary>The plugin's script list (null: no answer). Call <see cref="Evaluate"/> afterwards.</summary>
    public void OnScripts(IReadOnlyList<SkuaScriptInfo>? scripts, DateTimeOffset now)
    {
        if (_state != QueueState.Running || _phase != QueuePhase.Starting || !_a.ScriptsAsked || _scripts is not null)
        {
            return;
        }

        if (scripts is null)
        {
            PauseWith(NoAnswerReason, now);
        }
        else
        {
            _scripts = scripts;
        }

        Flush();
    }

    /// <summary>The answer to <c>start</c>. Call <see cref="Evaluate"/> afterwards.</summary>
    public void OnStartResult(QueueOutcome outcome, DateTimeOffset now)
    {
        if (_state != QueueState.Running || (_phase != QueuePhase.Starting && _phase != QueuePhase.Running) || !_a.StartSent || _a.StartAnswered)
        {
            return;
        }

        _a.StartAnswered = true;
        if (!outcome.Answered)
        {
            PauseWith(outcome.Error ?? NoAnswerReason, now);
        }
        else if (outcome.FirstRun)
        {
            PauseWith(FirstRunReason, now);
        }
        else if (!outcome.Ok)
        {
            PauseWith(RefusalReason(outcome.Error), now);
        }
        else
        {
            _a.StartOkAt = now;
        }

        Flush();
    }

    /// <summary>The answer to <c>stop</c>. Call <see cref="Evaluate"/> afterwards.</summary>
    public void OnStopResult(QueueOutcome outcome, DateTimeOffset now)
    {
        if (_state != QueueState.Running || _phase != QueuePhase.Stopping || !_a.StopSent || _a.StopAnswered)
        {
            return;
        }

        _a.StopAnswered = true;
        if (!outcome.Answered)
        {
            PauseWith(outcome.Error ?? NoAnswerReason, now);
        }
        else if (!outcome.Ok)
        {
            _a.StopRefused = outcome.Error ?? "refused";   // judged in Stopping(): a script that ended meanwhile makes this harmless
        }

        Flush();
    }

    // ---- the phases ----

    private void Step(QueueInputs i, QueueStep step, DateTimeOffset now)
    {
        if (Problem(i, step) is { } problem)
        {
            if (_a.Exit?.Kind == ExitKind.Halt)
            {
                _state = QueueState.Stopped;   // the owner already asked to stop the queue: a problem must not turn that into a pause
                _reason = null;
                _have = null;
                Mark();
                return;
            }

            if (problem == ChangedReason)
            {
                _a.Owns = false;   // from now on the running script is not ours to stop
            }

            PauseWith(problem, now);
            return;
        }

        switch (_phase)
        {
            case QueuePhase.Starting:
                Starting(i, step, now);
                break;
            case QueuePhase.Running:
                Watching(i, step, now);
                break;
            case QueuePhase.Stopping:
                Stopping(i, now);
                break;
            default:
                Settling(i, now);
                break;
        }
    }

    private void Starting(QueueInputs i, QueueStep step, DateTimeOffset now)
    {
        var status = i.Status;
        if (Alive(status))
        {
            if (_a.StartSent && status!.ScriptRunning && !QueueText.SameName(status.Script, step.ScriptName))
            {
                PauseWith(ChangedReason, now);
                _a.Owns = false;   // not ours: never adopted, never stopped
            }
            else if (_a.StartSent)
            {
                _a.Owns = true;
                SetPhase(QueuePhase.Running);
                Watching(i, step, now);
            }
            else if (status!.ScriptRunning && QueueText.SameName(status.Script, step.ScriptName))
            {
                _a.Owns = true;                 // Continue adopts a running script of the current step
                _a.StartedAt = now;
                SetPhase(QueuePhase.Running);
                Watching(i, step, now);
            }
            else
            {
                PauseWith(AnotherScriptReason, now);
            }

            return;
        }

        if (_a.StartSent)
        {
            if (_a.StartOkAt is { } accepted && now - accepted >= StartGrace)
            {
                _a.Owns = true;                 // it came and went: judged like any script that ended by itself
                SetPhase(QueuePhase.Running);
                Watching(i, step, now);
            }

            return;
        }

        if (GoalReached(i, step))
        {
            _sink.Notify(QueueNotices.AlreadyHad(_character, step, now, Have(i.Inventory, step) >= step.GoalQuantity));
            Advance(now, pauseAfter: false);
            return;
        }

        if (_scripts is null)
        {
            if (!_a.ScriptsAsked)
            {
                _a.ScriptsAsked = true;
                _sink.RequestScripts();
            }

            return;
        }

        var resolved = Resolve(step);
        if (resolved is null)
        {
            PauseWith(NotFoundReason, now);
            return;
        }

        if (status is { Control: "idle", ScriptRunning: false })
        {
            _a.StartSent = true;
            _a.StartedAt = now;
            _sink.SendStart(resolved.ScriptId);
        }
    }

    private void Watching(QueueInputs i, QueueStep step, DateTimeOffset now)
    {
        if (Alive(i.Status))
        {
            _a.EndedSeenAt = null;
            if (GoalReached(i, step))
            {
                BeginStop(new Exit(ExitKind.Advance), now);
            }
            else if (step.MaxMinutes is { } minutes && now - _a.StartedAt >= TimeSpan.FromMinutes(minutes))
            {
                _sink.Notify(QueueNotices.TimeLimit(_character, step, now));
                BeginStop(new Exit(ExitKind.Pause, step.ScriptName + " hit its time limit."), now);
            }

            return;
        }

        _a.EndedSeenAt ??= now;
        if (GoalReached(i, step))
        {
            Settle(new Exit(ExitKind.Advance), now);
        }
        else if (now - _a.EndedSeenAt.Value >= EndGrace)
        {
            _sink.Notify(QueueNotices.Skipped(_character, step, now));
            Settle(new Exit(ExitKind.Advance), now);
        }
    }

    private void Stopping(QueueInputs i, DateTimeOffset now)
    {
        if (!Alive(i.Status))
        {
            SetPhase(QueuePhase.Settling);
            Settling(i, now);
            return;
        }

        if (_a.StopRefused is { } refused)
        {
            PauseWith("Skua refused to stop the script: " + refused, now);
        }
        else if (now - _a.ExitAt >= SettleTimeout)
        {
            PauseWith("Skua did not stop the script.", now);
        }
    }

    private void Settling(QueueInputs i, DateTimeOffset now)
    {
        if (i.Status is { Control: "idle", ScriptRunning: false })
        {
            ApplyExit(now);
        }
        else if (now - _a.ExitAt >= SettleTimeout)
        {
            PauseWith("Skua did not settle.", now);
        }
    }

    // ---- moving on ----

    private void BeginStop(Exit exit, DateTimeOffset now)
    {
        _a.Exit = exit;
        _a.StopSent = true;
        _a.StopAnswered = false;   // a fresh stop attempt must not inherit an earlier answer or refusal
        _a.StopRefused = null;
        _a.ExitAt = now;
        SetPhase(QueuePhase.Stopping);
        _sink.SendStop();
    }

    private void Settle(Exit exit, DateTimeOffset now)
    {
        _a.Exit = exit;
        _a.ExitAt = now;
        SetPhase(QueuePhase.Settling);
    }

    private void ApplyExit(DateTimeOffset now)
    {
        var exit = _a.Exit ?? new Exit(ExitKind.Advance);
        switch (exit.Kind)
        {
            case ExitKind.Advance:
                Advance(now, pauseAfter: false);
                break;
            case ExitKind.AdvanceThenPause:
                Advance(now, pauseAfter: true);
                break;
            case ExitKind.Pause:
                PauseWith(exit.Reason ?? "Paused.", now, notify: false);
                break;
            default:
                _state = QueueState.Stopped;
                _reason = null;
                Mark();
                break;
        }
    }

    /// <summary>Moves to the next step (or finishes). <paramref name="pauseAfter"/>: the owner skipped while paused, so it stays paused.</summary>
    private void Advance(DateTimeOffset now, bool pauseAfter)
    {
        _cursor++;
        _have = null;
        _reason = null;
        if (_cursor >= _steps.Count)
        {
            _cursor = _steps.Count;
            _state = QueueState.Finished;
            _sink.Notify(QueueNotices.Finished(_character, now));
            Mark();
            return;
        }

        BeginAttempt();
        if (pauseAfter)
        {
            _state = QueueState.Paused;
            _reason = "Skipped. Press Continue.";
        }

        Mark();
    }

    private void BeginAttempt()
    {
        _a = new Attempt();
        _scripts = null;
        _state = QueueState.Running;
        _phase = QueuePhase.Starting;
        _reason = null;
        Mark();
    }

    private void PauseWith(string reason, DateTimeOffset now, bool notify = true)
    {
        _state = QueueState.Paused;
        _reason = reason;
        Mark();
        if (notify)
        {
            _sink.Notify(QueueNotices.Paused(_character, reason, now));
        }
    }

    // ---- reading the world ----

    private static bool Alive(SkuaStatus? status) =>
        status is { } s && (s.ScriptRunning || s.Control is "running" or "awaitingOptions");

    private static int Have(SkuaInventory? inventory, QueueStep step)
    {
        long total = 0;
        foreach (var item in inventory?.Items ?? [])
        {
            if (item.Where == SkuaProtocol.ContainerInventory && QueueText.SameName(item.Name, step.GoalItem))
            {
                total += item.Quantity;
            }
        }

        return (int)Math.Min(total, int.MaxValue);
    }

    private static bool GoalReached(QueueInputs i, QueueStep step)
    {
        if (Have(i.Inventory, step) >= step.GoalQuantity)
        {
            return true;
        }

        return step.Reputation is { } goal
            && i.Status?.Factions?.FirstOrDefault(f => QueueText.SameName(f.Name, goal.Faction)) is { } faction
            && faction.Rank >= goal.Rank;
    }

    private QueueStep? Resolve(QueueStep step)
    {
        var list = _scripts!;
        if (list.Any(s => s.Id == step.ScriptId))
        {
            return step;
        }

        var named = list.Where(s => QueueText.SameName(s.Name, step.ScriptName)).ToList();
        if (named.Count != 1)
        {
            return null;
        }

        var fixedStep = step with { ScriptId = named[0].Id };
        _steps[_cursor] = fixedStep;
        Mark();
        return fixedStep;
    }

    private static string RefusalReason(string? error) =>
        error is "unknown script id" or "not found" ? NotFoundReason : "Skua refused to start the script: " + (string.IsNullOrWhiteSpace(error) ? "refused" : error);

    // ---- bookkeeping ----

    private void SetPhase(QueuePhase phase)
    {
        if (_phase != phase)
        {
            _phase = phase;
            Mark();
        }
    }

    private void SetHave(int have)
    {
        if (_have != have)
        {
            _have = have;
            Mark();
        }
    }

    private void Mark() => _dirty = true;

    private void Flush()
    {
        if (_dirty)
        {
            _dirty = false;
            _sink.Changed();
        }
    }
}
