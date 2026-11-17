namespace AqwCompanion.Phone.Notices;

public enum NoticeKind
{
    RankUp, HardFarmDrop, ActivityStopped, NewLogin, SkuaDisconnected, SkuaReconnected, SkuaOptionsWaiting,
    ScriptStopped, ScriptError, CharacterKicked, NoProgress, DyingOften,
}

public sealed record Notice(string Id, NoticeKind Kind, string Character, string Title, string Body, DateTimeOffset AtUtc);

public sealed record NoticeOptions(
    TimeSpan NoProgressAfter, int MaxPerCharacterPerMinute, int DyingCount, TimeSpan DyingWindow, TimeSpan DyingRearm,
    TimeSpan KickCooldown, TimeSpan PhoneStopWindow, TimeSpan ErrorStopPair)
{
    public static NoticeOptions Default { get; } = new(
        TimeSpan.FromMinutes(10), 6, 5, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10));
}
