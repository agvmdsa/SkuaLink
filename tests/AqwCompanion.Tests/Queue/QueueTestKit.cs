using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Queue;

internal sealed class FakeQueueSink : IQueueSink
{
    public List<string> Calls { get; } = [];

    public List<Notice> Notices { get; } = [];

    public int Changes { get; private set; }

    public void RequestScripts() => Calls.Add("scripts");

    public void SendStart(string scriptId) => Calls.Add("start:" + scriptId);

    public void SendStop() => Calls.Add("stop");

    public void Notify(Notice notice) => Notices.Add(notice);

    public void Changed() => Changes++;

    public int Count(string call) => Calls.Count(c => c == call);
}

internal static class Kit
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    public const string A = "0123456789abcdef";
    public const string B = "fedcba9876543210";
    public const string Hero = "Hero";

    /// <summary>Replaces with the revision the queue has right now (what an editor that just opened would quote).</summary>
    public static QueueResult Replace(this ISkuaQueues queues, string character, IReadOnlyList<QueueStep> steps) =>
        queues.Replace(character, steps, (queues.Find(character) ?? QueueView.Empty(character)).Revision);

    public static IReadOnlyList<SkuaScriptInfo> Scripts { get; } =
        [new SkuaScriptInfo(A, "Dailies", "Farm"), new SkuaScriptInfo(B, "Butler", "Misc")];

    public static QueueStep Step(string id = "1", string scriptId = A, string name = "Dailies", string item = "Blood Orb", int quantity = 10, ReputationGoal? rep = null, int? minutes = null) =>
        new(id.PadLeft(32, '0'), scriptId, name, item, quantity, rep, minutes);

    public static SkuaStatus Status(
        string? script = "Dailies", bool running = false, string? control = "idle", bool loggedIn = true, bool stuck = false, IReadOnlyList<SkuaFaction>? factions = null) =>
        new(loggedIn, loggedIn ? Hero : null, null, "map", script, running, null, null, factions, control, null, stuck);

    public static SkuaInventory Inventory(params (string Name, int Quantity, string Where)[] items) =>
        new(Hero, [SkuaProtocol.ContainerInventory], [.. items.Select(i => new SkuaItem(i.Name, i.Quantity, i.Where))]);

    public static SkuaInventory Bag(string name, int quantity) => Inventory((name, quantity, SkuaProtocol.ContainerInventory));

    public static QueueInputs Idle(SkuaInventory? inventory = null) => new(true, Status(), [], inventory);

    public static QueueInputs Running(SkuaInventory? inventory = null, string script = "Dailies") =>
        new(true, Status(script, running: true, control: "running"), [], inventory);

    public static QueueInputs Ended(SkuaInventory? inventory = null, string control = "settling") =>
        new(true, Status(running: false, control: control), [], inventory);

    public static QueueEngine Engine(FakeQueueSink sink, params QueueStep[] steps) => new(Hero, sink, steps);

    /// <summary>Starts the engine at the first step and answers the script list and the start, leaving it with the script running.</summary>
    public static void RunFirstStep(QueueEngine engine, FakeQueueSink sink, SkuaInventory? inventory = null)
    {
        engine.Start(Idle(inventory), T0);
        engine.OnScripts(Scripts, T0);
        engine.Evaluate(Idle(inventory), T0);
        engine.OnStartResult(new QueueOutcome(true, true), T0);
        engine.Evaluate(Running(inventory), T0.AddSeconds(1));
    }
}
