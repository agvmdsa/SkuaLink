namespace AqwCompanion.Phone.Queue;

/// <summary>An optional extra goal of a step: the faction (matched by name like items) reaches this rank (1 to 10).</summary>
public sealed record ReputationGoal(string Faction, int Rank);

/// <summary>
/// One step of a queue. <paramref name="ScriptId"/> is the plugin's id (16 hex characters); <paramref name="ScriptName"/> is the
/// script's name, kept to show it and to find the script again when the id changed. The goal is "at least
/// <paramref name="GoalQuantity"/> of <paramref name="GoalItem"/> in the inventory container".
/// </summary>
public sealed record QueueStep(
    string Id, string ScriptId, string ScriptName, string GoalItem, int GoalQuantity, ReputationGoal? Reputation = null, int? MaxMinutes = null);

public enum QueueState { Stopped, Running, Paused, Finished }

/// <summary>What a running step is doing: waiting to start, watching its script, stopping it, or waiting for Skua to settle.</summary>
public enum QueuePhase { Starting, Running, Stopping, Settling }

public enum QueueCommand { Start, Pause, Continue, Skip, Stop }

public sealed record QueueResult(bool Ok, string? Error = null)
{
    public static QueueResult Done { get; } = new(true);

    public static QueueResult Refused(string error) => new(false, error);
}

/// <summary>
/// What the PC and the phones see of one queue. <paramref name="Cursor"/> is the current step (the step count once finished);
/// <paramref name="Phase"/> only while running; <paramref name="Have"/> is how many of the current step's item the inventory
/// holds (null when unknown); <paramref name="CanEdit"/> is false while a step is starting, stopping or settling;
/// <paramref name="LastNotice"/> is the text of the last notice the queue sent (in memory only, at most 300 characters), for the PC card;
/// <paramref name="Revision"/> changes whenever the step list changes (0 while the queue is empty) and is what a save must quote.
/// </summary>
public sealed record QueueView(
    string Character, IReadOnlyList<QueueStep> Steps, int Cursor, QueueState State, QueuePhase? Phase, string? Reason, int? Have, bool CanEdit, string? LastNotice = null, long Revision = 0)
{
    public static QueueView Empty(string character) => new(character, [], 0, QueueState.Stopped, null, null, null, true);
}

public static class QueueRevision
{
    public const string ChangedText = "The queue changed on the PC. Open the editor again.";
}

public static class QueueLimits
{
    public const int MaxSteps = 20;
    public const int MaxItemName = 80;
    public const int MaxFaction = 80;
    public const int MaxScriptName = 300;
    public const int MinQuantity = 1;
    public const int MaxQuantity = 1_000_000;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 1440;
    public const int MinRank = 1;
    public const int MaxRank = 10;

    /// <summary>The phone's PUT body cap in bytes (the PC host cap is raised to match).</summary>
    public const int MaxBodyBytes = 8192;
}
