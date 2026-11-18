using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Queue;

public sealed partial class QueueEngine
{
    public const string PausedByYou = "Paused by you.";
    public const string BusyText = "The queue is starting or stopping a script. Try again in a moment.";
    public const string StepChangedReason = "The current step was changed. Check it, then press Continue.";
    public const string AnotherScriptReason = "Another script is running.";
    public const string OldPluginReason = "This Skua plugin is too old for the queue. Update it to the same release as SkuaLink.";
    public const string OptionsStuckReason = "Skua is waiting for you to close the script's options window on the PC.";
    private const string NotRunningText = "The queue is not running.";
    private const string StartInFlightText = "A script is being started. Try again in a moment.";

    /// <summary>Stops deciding; the script keeps running and Continue re-evaluates.</summary>
    public QueueResult Pause(DateTimeOffset now)
    {
        if (_state != QueueState.Running)
        {
            return QueueResult.Refused(NotRunningText);
        }

        _state = QueueState.Paused;
        _reason = PausedByYou;
        Mark();
        Flush();
        return QueueResult.Done;
    }

    /// <summary>A new attempt at the current step: a running script of that step is adopted, a different one refuses.</summary>
    public QueueResult Continue(QueueInputs inputs, DateTimeOffset now)
    {
        if (_state != QueueState.Paused || _cursor >= _steps.Count)
        {
            return QueueResult.Refused("The queue is not paused.");
        }

        var step = _steps[_cursor];
        var status = inputs.Status;
        if (Alive(status) && !(status!.ScriptRunning && QueueText.SameName(status.Script, step.ScriptName)))
        {
            return QueueResult.Refused(AnotherScriptReason);
        }

        BeginAttempt();
        Evaluate(inputs, now);
        return QueueResult.Done;
    }

    /// <summary>Stops the script the queue owns, waits for idle and advances; from <c>Paused</c> the queue stays paused afterwards.</summary>
    public QueueResult Skip(QueueInputs inputs, DateTimeOffset now)
    {
        if (_state != QueueState.Running && _state != QueueState.Paused)
        {
            return QueueResult.Refused(NotRunningText);
        }

        var paused = _state == QueueState.Paused;
        var exit = new Exit(paused ? ExitKind.AdvanceThenPause : ExitKind.Advance);
        if (_state == QueueState.Running && (_phase == QueuePhase.Stopping || _phase == QueuePhase.Settling))
        {
            if (_a.Exit?.Kind != ExitKind.Halt)
            {
                _a.Exit = exit;
            }

            Mark();
            Flush();
            return QueueResult.Done;
        }

        if (StartInFlight(inputs.Status))
        {
            return QueueResult.Refused(StartInFlightText);
        }

        if (OwnedAndRunning(inputs.Status))
        {
            _state = QueueState.Running;
            _reason = null;
            BeginStop(exit, now);
            Flush();
            return QueueResult.Done;
        }

        Advance(now, pauseAfter: paused);
        Evaluate(inputs, now);
        return QueueResult.Done;
    }

    /// <summary>Stops the script the queue owns, waits for idle, then <c>Stopped</c> with the cursor kept.</summary>
    public QueueResult StopQueue(QueueInputs inputs, DateTimeOffset now)
    {
        if (_state != QueueState.Running && _state != QueueState.Paused)
        {
            return QueueResult.Refused(NotRunningText);
        }

        if (_state == QueueState.Running && (_phase == QueuePhase.Stopping || _phase == QueuePhase.Settling))
        {
            _a.Exit = new Exit(ExitKind.Halt);
            Mark();
            Flush();
            return QueueResult.Done;
        }

        if (StartInFlight(inputs.Status))
        {
            return QueueResult.Refused(StartInFlightText);
        }

        if (OwnedAndRunning(inputs.Status))
        {
            _state = QueueState.Running;
            _reason = null;
            BeginStop(new Exit(ExitKind.Halt), now);
        }
        else
        {
            _state = QueueState.Stopped;
            _reason = null;
            _have = null;
            Mark();
        }

        Flush();
        return QueueResult.Done;
    }

    /// <summary>
    /// Replaces the step list (R8). Refused while a step is starting, stopping or settling, and, when <paramref name="expectedRevision"/>
    /// is given, when the queue has another revision (nothing changes then). A list equal to the current one keeps the revision.
    /// </summary>
    public QueueResult Replace(IReadOnlyList<QueueStep> steps, DateTimeOffset now, long? expectedRevision = null)
    {
        if (expectedRevision is { } expected && expected != Revision)
        {
            return QueueResult.Refused(QueueRevision.ChangedText);
        }

        if (!CanEdit)
        {
            return QueueResult.Refused(BusyText);
        }

        var currentId = _cursor < _steps.Count ? _steps[_cursor].Id : null;
        if (!_steps.SequenceEqual(steps))
        {
            _revision++;
        }

        _steps = [.. steps];
        if (_steps.Count == 0)
        {
            _state = QueueState.Stopped;
            _cursor = 0;
            _reason = null;
            _have = null;
        }
        else if (_state == QueueState.Finished)
        {
            _state = QueueState.Stopped;
            _cursor = 0;
        }
        else
        {
            var at = currentId is null ? -1 : _steps.FindIndex(s => s.Id == currentId);
            if (at >= 0)
            {
                _cursor = at;
            }
            else
            {
                _cursor = Math.Min(_cursor, _steps.Count - 1);
                if (_state == QueueState.Running)
                {
                    PauseWith(StepChangedReason, now);
                }
            }
        }

        Mark();
        Flush();
        return QueueResult.Done;
    }

    /// <summary>True only for the script the queue started or adopted and that still runs under the step's name: never a script the owner swapped in.</summary>
    private bool OwnedAndRunning(SkuaStatus? status) =>
        _a.Owns && !(_a.StartSent && !_a.StartAnswered) && status is { ScriptRunning: true } && _cursor < _steps.Count && QueueText.SameName(status.Script, _steps[_cursor].ScriptName);

    /// <summary>A start is unanswered, or accepted but its script is not visible yet: stopping or skipping now would leave it unattended.</summary>
    private bool StartInFlight(SkuaStatus? status) =>
        _state == QueueState.Running && _a.StartSent
        && (!_a.StartAnswered || (_phase == QueuePhase.Starting && _a.StartOkAt is not null && !_a.Owns && !Alive(status)));

    /// <summary>A reason to pause, or null. Judged before every decision (decision 4: nothing retries, nothing reinstates).</summary>
    private string? Problem(QueueInputs i, QueueStep step)
    {
        if (!i.Connected)
        {
            return "Skua disconnected.";
        }

        var status = i.Status;
        if (status is null || !status.LoggedIn)
        {
            return "The character is logged out.";
        }

        if (status.Control is null)
        {
            return OldPluginReason;
        }

        if (status.OptionsStuck)
        {
            return OptionsStuckReason;
        }

        if (_a.StopSent || !(_a.StartSent || _a.Owns))
        {
            return null;   // nothing of ours is running yet, or we are already ending it ourselves
        }

        if (Since(i.Log, _a.StartedAt, SkuaLogKind.ScriptError) is { } error)
        {
            return "The script failed: " + FirstLine(error.Text);
        }

        if (Since(i.Log, _a.StartedAt, SkuaLogKind.LoggedOut, SkuaLogKind.Relogin) is not null)
        {
            return "The character was disconnected.";
        }

        if (status.ScriptRunning && !QueueText.SameName(status.Script, step.ScriptName))
        {
            return ChangedReason;
        }

        return null;
    }

    private static SkuaLogEntry? Since(IReadOnlyList<SkuaLogEntry> log, DateTimeOffset from, params SkuaLogKind[] kinds) =>
        log.Where(e => e.At >= from && kinds.Contains(e.Kind)).OrderBy(e => e.At).FirstOrDefault();

    private static string FirstLine(string text)
    {
        const string prefix = "Script stopped with an error: ";
        var body = text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
        return QueueText.Clean(body.Split('\n', 2)[0], 120);
    }
}
