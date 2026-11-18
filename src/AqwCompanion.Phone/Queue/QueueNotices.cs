using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Phone.Queue;

/// <summary>The notices of a queue (R11). The character is the notice's character, so a push opens its Bot section.</summary>
internal static class QueueNotices
{
    public static Notice Skipped(string character, QueueStep step, DateTimeOffset at) =>
        Make("skip", NoticeKind.QueueAttention, character, "Queue step skipped",
            $"The step {step.ScriptName} ended without {step.GoalItem}. It may be a daily you already did today.", at);

    public static Notice AlreadyHad(string character, QueueStep step, DateTimeOffset at) =>
        Make("had", NoticeKind.QueueAttention, character, "Queue step skipped", $"Already had {step.GoalItem}. Skipped {step.ScriptName}.", at);

    public static Notice TimeLimit(string character, QueueStep step, DateTimeOffset at) =>
        Make("time", NoticeKind.QueueAttention, character, "Queue paused", $"{step.ScriptName} hit its time limit.", at);

    public static Notice Paused(string character, string reason, DateTimeOffset at) =>
        Make("paused", NoticeKind.QueueAttention, character, "Queue paused", $"{character}: {reason}", at);

    public static Notice Finished(string character, DateTimeOffset at) =>
        Make("finished", NoticeKind.QueueFinished, character, "Queue finished", $"{character}: the queue finished.", at);

    private static Notice Make(string tag, NoticeKind kind, string character, string title, string body, DateTimeOffset at) =>
        new($"queue-{tag}:{character}:{at.UtcTicks}", kind, character, title, body, at);
}
