using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Tests.Boosts;

public sealed class BoostNoticesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("gold", "The gold boost ended.")]
    [InlineData("class", "The class boost ended.")]
    [InlineData("rep", "The reputation boost ended.")]
    [InlineData("xp", "The XP boost ended.")]
    public void The_notice_names_the_type(string type, string body)
    {
        var notice = BoostNotices.Ended("Hero", type, T0);
        Assert.Equal(NoticeKind.BoostEnded, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Boost ended", notice.Title);
        Assert.Equal(body, notice.Body);
        Assert.Equal(T0, notice.AtUtc);
        Assert.Equal($"boost-ended:{type}:Hero:{T0.UtcTicks}", notice.Id);
    }

    [Fact]
    public void The_id_changes_with_the_time_and_the_type()
    {
        Assert.NotEqual(BoostNotices.Ended("Hero", "gold", T0).Id, BoostNotices.Ended("Hero", "gold", T0.AddSeconds(1)).Id);
        Assert.NotEqual(BoostNotices.Ended("Hero", "gold", T0).Id, BoostNotices.Ended("Hero", "xp", T0).Id);
    }

    [Fact]
    public void A_weird_type_is_a_plain_boost_and_the_headings_are_fixed()
    {
        Assert.Equal("boost", BoostTypes.Display("weird"));
        Assert.Equal(["Gold", "Class", "Reputation", "XP"], BoostTracker.Types.Select(BoostTypes.Heading));
    }

    [Fact]
    public void The_texts_are_the_agreed_ones()
    {
        Assert.Equal("A reputation boost is already active.", BoostTexts.AlreadyActive("rep"));
        Assert.Equal("Login started", BoostTexts.LoginStarted);
        Assert.Equal("Update the plugin DLL to use boosts.", BoostTexts.NotSupported);
    }
}
