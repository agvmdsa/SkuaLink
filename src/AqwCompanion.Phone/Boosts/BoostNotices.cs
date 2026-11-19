using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Phone.Boosts;

public static class BoostNotices
{
    public static Notice Ended(string character, string type, DateTimeOffset at) =>
        new($"boost-ended:{type}:{character}:{at.UtcTicks}", NoticeKind.BoostEnded, character, "Boost ended", $"The {BoostTypes.Display(type)} boost ended.", at);
}
