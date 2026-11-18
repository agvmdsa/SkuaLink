using System.Collections.Concurrent;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Queue;

/// <summary>
/// Owns one <see cref="QueueEngine"/> per character: feeds them from the Skua feed and the character book, sends the commands they ask
/// for, runs the 5 s timer, saves after every change, announces their notices and forgets with the rest. Never throws into the UI or
/// the hub.
/// <para>
/// Locks: <c>_io</c> (the store, taken first) then <c>_gate</c> (the engines). The hub, the book, the channel, the announce callback and
/// <see cref="Changed"/> are never called while either is held; the world (feed and book) is read before the locks are taken, so the
/// order hub lock, then service lock is the only one that exists. <see cref="Views"/> and <see cref="Find"/> take only <c>_gate</c>.
/// </para>
/// </summary>
public sealed class SkuaQueueService : ISkuaQueues, IForgettable, IDisposable
{
    public static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(5);

    private const string TryAgain = "Try again in a moment.";
    private const string Disconnected = "Skua is disconnected.";

    private readonly ISkuaFeed _skua;
    private readonly ISkuaCommandChannel _channel;
    private readonly CharacterBook _characters;
    private readonly IQueueStore _store;
    private readonly IDelay _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Notice> _announce;
    private readonly Action<Exception>? _onError;

    private readonly object _gate = new();   // the engines
    private readonly object _io = new();     // the store; always taken BEFORE _gate
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Task, byte> _inflight = new();
    private readonly ConcurrentQueue<Exception> _errors = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly EventHandler _onFeedChanged;
    private bool _loaded;
    private bool _quiesced;
    private int _started;
    private int _disposed;
    private long _stamp;     // taken before every read of the world
    private long _applied;   // under _gate: the newest stamp an Evaluate used

    public SkuaQueueService(
        ISkuaFeed skua, ISkuaCommandChannel channel, CharacterBook characters, IQueueStore store, IDelay delay, Func<DateTimeOffset> now,
        Action<Notice> announce, Action<Exception>? onError = null)
    {
        _skua = skua;
        _channel = channel;
        _characters = characters;
        _store = store;
        _delay = delay;
        _now = now;
        _announce = announce;
        _onError = onError;
        _onFeedChanged = (_, _) => Pump();
    }

    public string Name => "Script queues";

    public event EventHandler? Changed;

    public IReadOnlyList<QueueView> Views
    {
        get
        {
            try
            {
                lock (_gate)
                {
                    EnsureLoaded();
                    return [.. _slots.Values
                        .Where(s => s.Engine.Steps.Count > 0 || s.Engine.State != QueueState.Stopped)
                        .Select(s => s.Engine.View)
                        .OrderBy(v => v.Character, StringComparer.OrdinalIgnoreCase)];
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report(ex);
                return [];
            }
            finally
            {
                FlushErrors();
            }
        }
    }

    public QueueView? Find(string character)
    {
        try
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _slots.TryGetValue(character, out var slot) && (slot.Engine.Steps.Count > 0 || slot.Engine.State != QueueState.Stopped)
                    ? slot.Engine.View
                    : null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return null;
        }
        finally
        {
            FlushErrors();
        }
    }

    public QueueResult Replace(string character, IReadOnlyList<QueueStep> steps)
    {
        var result = QueueResult.Refused(TryAgain);
        if (!TryWorld(out var world))
        {
            return result;   // no half-picture: the owner tries again
        }

        Run(() =>
        {
            var slot = GetSlot(character);
            result = slot.Engine.Replace(steps, _now());
            Evaluate(slot, world);
        });
        return result;
    }

    public QueueResult Command(string character, QueueCommand command)
    {
        var result = QueueResult.Refused(TryAgain);
        var haveWorld = TryWorld(out var world);
        if (!haveWorld && command != QueueCommand.Pause)
        {
            return result;   // Start, Continue, Skip and Stop need the real picture of Skua
        }

        Run(() =>
        {
            var slot = GetSlot(character);
            var inputs = Inputs(slot, world);
            var now = _now();
            result = command switch
            {
                QueueCommand.Start => slot.Engine.Start(inputs, now),
                QueueCommand.Pause => slot.Engine.Pause(now),
                QueueCommand.Continue => slot.Engine.Continue(inputs, now),
                QueueCommand.Skip => slot.Engine.Skip(inputs, now),
                _ => slot.Engine.StopQueue(inputs, now),
            };
            if (haveWorld)
            {
                Evaluate(slot, world);
            }
        });
        return result;
    }

    /// <summary>Evaluates every queue now. Called on every feed or book change and by the timer.</summary>
    public void Pump()
    {
        if (!TryWorld(out var world))
        {
            return;
        }

        Run(() =>
        {
            foreach (var slot in _slots.Values.ToList())
            {
                Evaluate(slot, world);
            }
        });
    }

    /// <summary>Follows the feed and the book and starts the 5 s timer. Does nothing the second time.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || _stop.IsCancellationRequested)
        {
            return;
        }

        _skua.Changed += _onFeedChanged;
        _characters.Changed += _onFeedChanged;
        var token = _stop.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _delay.DelayAsync(TickEvery, token).ConfigureAwait(false);
                    Pump();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Report(ex);   // the loop never ends because of an error
                }
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        _skua.Changed -= _onFeedChanged;
        _characters.Changed -= _onFeedChanged;
    }

    /// <summary>Completes when no command is in flight (for tests).</summary>
    internal async Task WhenIdleAsync()
    {
        while (true)
        {
            foreach (var done in _inflight.Keys.Where(t => t.IsCompleted).ToList())
            {
                _inflight.TryRemove(done, out _);
            }

            var running = _inflight.Keys.ToArray();
            if (running.Length == 0)
            {
                return;
            }

            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    // ---- forgetting ----

    public void Quiesce()
    {
        lock (_gate)
        {
            _quiesced = true;
        }

        lock (_io)
        {
            // Only waits for a save in progress.
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _quiesced = false;
        }

        Run(() =>
        {
            foreach (var slot in _slots.Values)
            {
                var lost = slot.Lost.ToList();
                slot.Lost.Clear();
                foreach (var (attempt, noAnswer) in lost)
                {
                    if (ReferenceEquals(slot.Engine.AttemptToken, attempt))
                    {
                        noAnswer(slot.Engine);
                    }
                }
            }
        });
    }

    public void Forget()
    {
        lock (_io)
        {
            lock (_gate)
            {
                _slots.Clear();
                _loaded = true;
            }

            Safe(_store.DeleteAll);
        }

        lock (_gate)
        {
            _quiesced = false;
        }

        FlushErrors();
        Safe(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    // ---- the core ----

    /// <summary>
    /// Runs <paramref name="underGate"/> on the engines, then, outside the gate: saves what changed (in order), announces the notices,
    /// starts the commands the engines asked for and raises Changed. Never throws.
    /// </summary>
    private void Run(Action underGate, Action? whenQuiesced = null)
    {
        List<Func<Task>> effects = [];
        List<Notice> notices = [];
        var changed = false;
        lock (_io)
        {
            List<StoredQueue> saves = [];
            lock (_gate)
            {
                if (_quiesced)
                {
                    whenQuiesced?.Invoke();   // in the same gate acquisition as this check, so a dropped answer is never lost unrecorded
                    return;
                }

                try
                {
                    EnsureLoaded();
                    underGate();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Report(ex);
                }

                foreach (var slot in _slots.Values)
                {
                    effects.AddRange(slot.Effects);
                    slot.Effects.Clear();
                    notices.AddRange(slot.Notices);
                    slot.Notices.Clear();
                    if (slot.Dirty)
                    {
                        slot.Dirty = false;
                        changed = true;
                        saves.Add(StoredOf(slot.Engine));
                    }
                }
            }

            foreach (var save in saves)
            {
                Safe(() => _store.Save(save));
            }
        }

        FlushErrors();
        foreach (var notice in notices)
        {
            Safe(() => _announce(notice));
        }

        foreach (var effect in effects)
        {
            Launch(effect);
        }

        if (changed)
        {
            Safe(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }

    private static StoredQueue StoredOf(QueueEngine engine) =>
        new(engine.Character, engine.Steps, engine.Cursor, engine.State == QueueState.Finished);

    /// <summary>Under the gate, once: the stored queues become stopped engines (characters already present keep theirs).</summary>
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        IReadOnlyList<StoredQueue> stored;
        try
        {
            stored = _store.LoadAll();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return;
        }

        foreach (var queue in stored)
        {
            if (!_slots.ContainsKey(queue.Character))
            {
                _slots[queue.Character] = NewSlot(queue.Character, queue.Steps, queue.Cursor, queue.Finished);
            }
        }
    }

    private Slot NewSlot(string character, IReadOnlyList<QueueStep> steps, int cursor, bool finished)
    {
        var slot = new Slot(this);
        slot.Engine = new QueueEngine(character, slot, steps, cursor, finished);
        return slot;
    }

    private Slot GetSlot(string character)
    {
        if (!_slots.TryGetValue(character, out var slot))
        {
            slot = NewSlot(character, [], 0, false);
            _slots[character] = slot;
        }

        return slot;
    }

    // ---- the world ----

    private sealed record World(SkuaHubView View, IReadOnlyList<CharacterRecord> Characters, long Stamp);

    /// <summary>
    /// Under the gate: evaluates from <paramref name="world"/> unless a newer read was already applied (two threads can read in one
    /// order and reach the gate in the other; an older picture must not undo what the engine did with a newer one).
    /// </summary>
    private void Evaluate(Slot slot, World world)
    {
        if (world.Stamp < _applied)
        {
            return;
        }

        _applied = world.Stamp;
        slot.Engine.Evaluate(Inputs(slot, world), _now());
    }

    /// <summary>Reads the feed and the book (no service lock held). False when either threw: nothing is decided from half a picture.</summary>
    private bool TryWorld(out World world)
    {
        try
        {
            var stamp = Interlocked.Increment(ref _stamp);   // before the read
            world = new World(_skua.Current, _characters.All, stamp);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            FlushErrors();
            world = new World(SkuaHubView.Empty, [], 0);
            return false;
        }
    }

    /// <summary>Under the gate: finds the character's instance (connected and logged in first, else any with that name) and builds what the engine reads.</summary>
    private static QueueInputs Inputs(Slot slot, World world)
    {
        var character = slot.Engine.Character;
        var named = world.View.Instances.Where(i => QueueText.SameName(i.Status?.Character, character)).ToList();
        var instance = named.FirstOrDefault(i => !i.Disconnected && i.Status is { LoggedIn: true })
            ?? named.FirstOrDefault(i => !i.Disconnected)
            ?? named.FirstOrDefault();
        slot.InstanceId = instance?.InstanceId;
        var inventory = world.Characters.FirstOrDefault(r => QueueText.SameName(r.Name, character))?.Inventory;
        return new QueueInputs(instance is { Disconnected: false }, instance?.Status, instance?.Log ?? [], inventory);
    }

    // ---- commands ----

    private async Task ScriptsAsync(Slot slot, object attempt, string? instanceId)
    {
        var (result, _) = await SendAsync(instanceId, new ListScriptsCommand(string.Empty)).ConfigureAwait(false);
        IReadOnlyList<SkuaScriptInfo>? scripts = result is { Ok: true } ? result.Scripts ?? [] : null;
        Answer(slot, attempt, engine => engine.OnScripts(scripts, _now()), engine => engine.OnScripts(null, _now()));
    }

    private async Task StartAsync(Slot slot, object attempt, string? instanceId, string scriptId)
    {
        var (result, error) = await SendAsync(instanceId, new StartCommand(string.Empty, scriptId)).ConfigureAwait(false);
        var outcome = OutcomeOf(result, error, withFirstRun: true);
        Answer(slot, attempt, engine => engine.OnStartResult(outcome, _now()), engine => engine.OnStartResult(new QueueOutcome(false, false), _now()));
    }

    private async Task StopAsync(Slot slot, object attempt, string? instanceId)
    {
        var (result, error) = await SendAsync(instanceId, new StopCommand(string.Empty)).ConfigureAwait(false);
        var outcome = OutcomeOf(result, error, withFirstRun: false);
        Answer(slot, attempt, engine => engine.OnStopResult(outcome, _now()), engine => engine.OnStopResult(new QueueOutcome(false, false), _now()));
    }

    private static QueueOutcome OutcomeOf(ResultMessage? result, string? error, bool withFirstRun) =>
        result is null
            ? (error is null ? new QueueOutcome(false, false) : new QueueOutcome(false, false, error))
            : new QueueOutcome(true, result.Ok, result.Error, withFirstRun && result.FirstRun);

    private async Task<(ResultMessage? Result, string? Error)> SendAsync(string? instanceId, SkuaCommand command)
    {
        if (instanceId is null)
        {
            return (null, Disconnected);
        }

        try
        {
            return (await _channel.SendCommandAsync(instanceId, command, _stop.Token).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return (null, Disconnected);
        }
    }

    /// <summary>
    /// Reports an answer to the engine, then evaluates (the answer is always reported BEFORE the next evaluation). An answer for a
    /// forgotten queue or for an earlier attempt than the engine's current one changes nothing.
    /// </summary>
    private void Answer(Slot slot, object attempt, Action<QueueEngine> report, Action<QueueEngine> asNoAnswer)
    {
        var haveWorld = TryWorld(out var world);
        Run(() =>
        {
            if (!ReferenceEquals(_slots.GetValueOrDefault(slot.Engine.Character), slot) || !ReferenceEquals(slot.Engine.AttemptToken, attempt))
            {
                return;
            }

            report(slot.Engine);
            if (haveWorld)
            {
                Evaluate(slot, world);
            }
        },
        // Dropped while quiescing; if Resume follows (the removal failed) the command would stay unanswered forever, so Resume
        // reports it as "no answer" and the queue pauses instead of hanging.
        whenQuiesced: () => slot.Lost.Add((attempt, asNoAnswer)));
    }

    private void Launch(Func<Task> effect)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                await effect().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Shutting down.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report(ex);
            }
        });
        _inflight[task] = 0;
    }

    // ---- never throw ----

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            FlushErrors();
        }
    }

    /// <summary>Queues the error; it is delivered to <c>onError</c> outside the locks (at the end of the call that met it).</summary>
    private void Report(Exception ex)
    {
        _errors.Enqueue(ex);
        if (!Monitor.IsEntered(_gate) && !Monitor.IsEntered(_io))
        {
            FlushErrors();
        }
    }

    private void FlushErrors()
    {
        if (Monitor.IsEntered(_gate) || Monitor.IsEntered(_io))
        {
            return;
        }

        while (_errors.TryDequeue(out var ex))
        {
            try
            {
                _onError?.Invoke(ex);
            }
            catch (Exception inner) when (inner is not OutOfMemoryException)
            {
                // Reporting must never throw.
            }
        }
    }

    /// <summary>One engine and what it asked for during the current call; only touched under the gate.</summary>
    private sealed class Slot(SkuaQueueService owner) : IQueueSink
    {
        public QueueEngine Engine = null!;
        public string? InstanceId;
        public readonly List<Func<Task>> Effects = [];
        public readonly List<Notice> Notices = [];
        public bool Dirty;
        public readonly List<(object Attempt, Action<QueueEngine> NoAnswer)> Lost = [];

        public void RequestScripts()
        {
            var (id, attempt) = (InstanceId, Engine.AttemptToken);
            Effects.Add(() => owner.ScriptsAsync(this, attempt, id));
        }

        public void SendStart(string scriptId)
        {
            var (id, attempt) = (InstanceId, Engine.AttemptToken);
            Effects.Add(() => owner.StartAsync(this, attempt, id, scriptId));
        }

        public void SendStop()
        {
            var (id, attempt) = (InstanceId, Engine.AttemptToken);
            Effects.Add(() => owner.StopAsync(this, attempt, id));
        }

        public void Notify(Notice notice) => Notices.Add(notice);

        public void Changed() => Dirty = true;
    }
}
