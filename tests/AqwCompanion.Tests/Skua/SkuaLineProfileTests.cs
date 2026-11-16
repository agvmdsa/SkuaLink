using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaLineProfileTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Skua", "fixtures", name)).Trim();

    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly SkuaProfile Full = new(60, 340, 5000, 1200, 777, 10, true, "Void Highlord", 31, 302500, "Heroes", 140, 300, 50);

    private static StatusMessage WithProfile(SkuaProfile? profile) =>
        new(new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon, Profile: profile));

    [Fact]
    public void A_status_with_a_profile_matches_its_fixture_both_ways()
    {
        Assert.Equal(Fixture("status-with-profile.json"), SkuaLine.ToLine(WithProfile(Full)));
        Assert.Equal(WithProfile(Full), SkuaLine.Parse(Fixture("status-with-profile.json")));
    }

    [Fact]
    public void A_status_from_an_older_plugin_has_no_profile()
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(Fixture("status.json")));
        Assert.Null(parsed.Status.Profile);
        Assert.DoesNotContain("profile", SkuaLine.ToLine(WithProfile(null)));
    }

    [Fact]
    public void Optional_profile_parts_are_left_out_when_not_reported_and_read_back_as_null()
    {
        var bare = new SkuaProfile(5, 0, 100, 0, 0, 1, false);
        var line = SkuaLine.ToLine(WithProfile(bare));

        foreach (var name in new[] { "className", "classId", "classPoints", "guild", "bagSlots", "bankSlots", "houseSlots" })
        {
            Assert.DoesNotContain($"\"{name}\"", line);
        }

        Assert.Equal(WithProfile(bare), SkuaLine.Parse(line));
    }

    [Fact]
    public void Statuses_differ_when_only_the_profile_differs()
    {
        var a = WithProfile(Full).Status;
        Assert.NotEqual(a, a with { Profile = Full with { Gold = 1201 } });
        Assert.Equal(a.GetHashCode(), (a with { Profile = Full with { } }).GetHashCode());
    }

    [Fact]
    public void A_damaged_profile_loses_only_the_profile()
    {
        var line = Fixture("status-with-profile.json").Replace("\"level\":60", "\"level\":-1");
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));
        Assert.Null(parsed.Status.Profile);
        Assert.Equal("Hero", parsed.Status.Character);
    }

    [Fact]
    public void Gold_and_experience_above_int32_round_trip()
    {
        var big = Full with { Gold = 3_000_000_000L, Xp = 4_000_000_000L };
        Assert.Equal(WithProfile(big), SkuaLine.Parse(SkuaLine.ToLine(WithProfile(big))));
    }

    [Fact]
    public void Profile_texts_are_cut_to_the_text_cap_without_splitting_a_pair()
    {
        var guild = new string('g', SkuaProtocol.MaxTextLength - 1) + "\U0001F600";
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(SkuaLine.ToLine(WithProfile(Full with { Guild = guild }))));
        Assert.Equal(SkuaProtocol.MaxTextLength - 1, parsed.Status.Profile!.Guild!.Length);
    }
}
