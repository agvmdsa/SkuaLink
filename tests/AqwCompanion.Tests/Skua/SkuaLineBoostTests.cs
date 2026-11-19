using System.Text;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaLineBoostTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Skua", "fixtures", name)).Trim();

    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly SkuaBoosts Full = new(840, -1, 0, 0, 1763481234567,
        [new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 33), new SkuaBoostItem(22447, "Daily Login Class Boost (20 min)", "class", 61)]);

    private static StatusMessage WithBoosts(SkuaBoosts? boosts) =>
        new(new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon, Boosts: boosts));

    private static SkuaStatus ParseStatus(string line) => Assert.IsType<StatusMessage>(SkuaLine.Parse(line)).Status;

    [Fact]
    public void A_status_with_boosts_matches_its_fixture_both_ways()
    {
        Assert.Equal(Fixture("status-with-boosts.json"), SkuaLine.ToLine(WithBoosts(Full)));
        Assert.Equal(WithBoosts(Full), SkuaLine.Parse(Fixture("status-with-boosts.json")));
        Assert.Equal(1763481234567, ParseStatus(Fixture("status-with-boosts.json")).Boosts!.ReadAt);
    }

    [Fact]
    public void A_status_from_an_older_plugin_has_no_boosts()
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(Fixture("status.json")));
        Assert.Null(parsed.Status.Boosts);
        Assert.DoesNotContain("boosts", SkuaLine.ToLine(WithBoosts(null)));
    }

    [Fact]
    public void Boosts_without_items_round_trip_with_null_items()
    {
        var bare = new SkuaBoosts(0, 0, 0, 0, 5);
        var line = SkuaLine.ToLine(WithBoosts(bare));
        Assert.DoesNotContain("\"items\"", line);
        Assert.Equal(WithBoosts(bare), SkuaLine.Parse(line));
        Assert.Null(ParseStatus(line).Boosts!.Items);

        var empty = bare with { Items = [] };
        var emptyLine = SkuaLine.ToLine(WithBoosts(empty));
        Assert.Contains("\"items\":[]", emptyLine);
        var back = ParseStatus(emptyLine).Boosts!.Items;
        Assert.NotNull(back);
        Assert.Empty(back);
    }

    [Fact]
    public void Statuses_differ_when_only_the_boosts_differ()
    {
        var a = WithBoosts(Full).Status;
        var same = a with { Boosts = Full with { Items = [.. Full.Items!] } };
        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, a with { Boosts = Full with { ReadAt = Full.ReadAt + 1 } });
        Assert.NotEqual(a, a with { Boosts = Full with { Gold = 841 } });
        Assert.NotEqual(a, a with { Boosts = Full with { Items = [Full.Items![0] with { Quantity = 32 }, Full.Items![1]] } });
        Assert.NotEqual(a, a with { Boosts = null });
    }

    [Theory]
    [InlineData("\"gold\":840", "\"gold\":-2")]
    [InlineData("\"gold\":840", "\"gold\":\"x\"")]
    [InlineData("\"gold\":840", "\"gold\":1.5")]
    [InlineData("\"readAt\":1763481234567,", "")]
    [InlineData("\"readAt\":1763481234567", "\"readAt\":-1")]
    [InlineData("\"boosts\":{", "\"boosts\":[],\"other\":{")]
    public void A_damaged_boosts_part_loses_only_the_boosts(string from, string to)
    {
        var line = Fixture("status-with-boosts.json").Replace(from, to);
        Assert.NotEqual(Fixture("status-with-boosts.json"), line);
        var status = ParseStatus(line);
        Assert.Equal("Hero", status.Character);
        Assert.Null(status.Boosts);
    }

    [Fact]
    public void A_damaged_item_is_dropped_on_its_own_and_an_unknown_type_reads_as_unknown()
    {
        var items = string.Join(",",
            "{\"itemId\":1,\"type\":\"gold\",\"quantity\":1}",
            "{\"itemId\":0,\"name\":\"zero\",\"type\":\"gold\",\"quantity\":1}",
            "{\"itemId\":-4,\"name\":\"negative\",\"type\":\"gold\",\"quantity\":1}",
            "{\"itemId\":5,\"name\":\"fraction\",\"type\":\"gold\",\"quantity\":1.5}",
            "{\"itemId\":6,\"name\":\"potion\",\"type\":\"potion\",\"quantity\":2}",
            "{\"itemId\":7,\"name\":\"fine\",\"type\":\"xp\",\"quantity\":3}");
        var line = $"{{\"v\":1,\"type\":\"status\",\"loggedIn\":true,\"running\":false,\"boosts\":{{\"gold\":0,\"class\":0,\"rep\":0,\"xp\":0,\"readAt\":9,\"items\":[{items}]}}}}";
        var parsed = ParseStatus(line).Boosts!.Items!;
        Assert.Equal([new SkuaBoostItem(6, "potion", "unknown", 2), new SkuaBoostItem(7, "fine", "xp", 3)], parsed);

        var many = string.Join(",", Enumerable.Range(1, 20).Select(i => $"{{\"itemId\":{i},\"name\":\"n{i}\",\"type\":\"gold\",\"quantity\":1}}"));
        var manyLine = $"{{\"v\":1,\"type\":\"status\",\"loggedIn\":true,\"running\":false,\"boosts\":{{\"gold\":0,\"class\":0,\"rep\":0,\"xp\":0,\"readAt\":9,\"items\":[{many}]}}}}";
        Assert.Equal(SkuaProtocol.MaxBoostItems, ParseStatus(manyLine).Boosts!.Items!.Count);
    }

    [Fact]
    public void The_writer_clamps_and_cuts()
    {
        var items = new List<SkuaBoostItem>
        {
            new(1, new string('a', 300), "gold", -3),
            new(2, "  ", "gold", 1),
            new(0, "zero", "gold", 1),
            new(3, "odd", "potion", 4),
        };
        items.AddRange(Enumerable.Range(10, 20).Select(i => new SkuaBoostItem(i, $"n{i}", "rep", 1)));
        var line = SkuaLine.ToLine(WithBoosts(new SkuaBoosts(-5, -9, 7, 0, -100, items)));
        var boosts = ParseStatus(line).Boosts!;

        Assert.Equal(-1, boosts.Gold);
        Assert.Equal(-1, boosts.Class);
        Assert.Equal(7, boosts.Rep);
        Assert.Equal(0, boosts.ReadAt);
        Assert.Equal(SkuaProtocol.MaxBoostItems, boosts.Items!.Count);
        Assert.Equal(new string('a', SkuaProtocol.MaxBoostNameLength), boosts.Items[0].Name);
        Assert.Equal(0, boosts.Items[0].Quantity);
        Assert.Equal(new SkuaBoostItem(3, "odd", "unknown", 4), boosts.Items[1]);
        Assert.DoesNotContain("\"itemId\":2,", line);
        Assert.DoesNotContain("\"itemId\":0,", line);
    }

    [Fact]
    public void An_oversized_status_drops_boost_items_after_the_factions_and_the_quests()
    {
        var name = new string('é', 300);
        var status = new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon,
            Quests: [.. Enumerable.Range(1, 30).Select(i => new SkuaQuest(i, name, false))],
            Factions: [.. Enumerable.Range(1, 60).Select(i => new SkuaFaction(i, name, 1, 1, 1, 1))],
            Boosts: new SkuaBoosts(840, 0, 0, 0, 1763481234567,
                [.. Enumerable.Range(1, 12).Select(i => new SkuaBoostItem(i, new string('é', 100), "gold", 1))]));

        var line = SkuaLine.ToLine(new StatusMessage(status));
        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxLineBytes);
        var parsed = ParseStatus(line);
        Assert.Equal(840, parsed.Boosts!.Gold);
        Assert.Equal(1763481234567, parsed.Boosts.ReadAt);

        // Boost items alone too large: they are cut before the line is refused.
        var itemsOnly = status with
        {
            Quests = null,
            Factions = null,
            Boosts = status.Boosts! with { Items = [.. Enumerable.Range(1, 12).Select(i => new SkuaBoostItem(i, new string('é', 100), "gold", 1))] },
            Character = new string('é', 300),
            Server = new string('é', 300),
            Map = new string('é', 300),
            Script = new string('é', 300),
        };
        var itemsLine = SkuaLine.ToLine(new StatusMessage(itemsOnly));
        Assert.True(Encoding.UTF8.GetByteCount(itemsLine) <= SkuaProtocol.MaxLineBytes);
        Assert.Equal(840, ParseStatus(itemsLine).Boosts!.Gold);
    }

    [Fact]
    public void UseBoost_and_Relogin_match_their_fixtures_both_ways()
    {
        var use = new UseBoostCommand("r7", 17439);
        Assert.Equal(Fixture("command-use-boost.json"), SkuaLine.ToLine(use));
        Assert.Equal(Fixture("command-use-boost.json"), SkuaLine.ToCommandLine(use));
        Assert.Equal(use, SkuaLine.Parse(Fixture("command-use-boost.json")));

        var relogin = new ReloginCommand("r8");
        Assert.Equal(Fixture("command-relogin.json"), SkuaLine.ToLine(relogin));
        Assert.Equal(Fixture("command-relogin.json"), SkuaLine.ToCommandLine(relogin));
        Assert.Equal(relogin, SkuaLine.Parse(Fixture("command-relogin.json")));
    }

    [Theory]
    [InlineData("{\"v\":1,\"type\":\"useBoost\",\"itemId\":17439}")]
    [InlineData("{\"v\":1,\"type\":\"useBoost\",\"reqId\":\"r7\"}")]
    [InlineData("{\"v\":1,\"type\":\"useBoost\",\"reqId\":\"r7\",\"itemId\":0}")]
    [InlineData("{\"v\":1,\"type\":\"useBoost\",\"reqId\":\"r7\",\"itemId\":-3}")]
    [InlineData("{\"v\":1,\"type\":\"useBoost\",\"reqId\":\"r7\",\"itemId\":\"1\"}")]
    [InlineData("{\"v\":1,\"type\":\"useBoost\",\"reqId\":\"r7\",\"itemId\":1.5}")]
    [InlineData("{\"v\":1,\"type\":\"relogin\"}")]
    public void A_damaged_command_is_not_a_command(string line) => Assert.Null(SkuaLine.Parse(line));

    [Fact]
    public void A_command_over_the_command_cap_is_not_a_command()
    {
        var line = $"{{\"v\":1,\"type\":\"useBoost\",\"reqId\":\"{new string('x', SkuaProtocol.MaxCommandLineBytes)}\",\"itemId\":1}}";
        Assert.Null(SkuaLine.Parse(line));
        var relogin = $"{{\"v\":1,\"type\":\"relogin\",\"reqId\":\"{new string('x', SkuaProtocol.MaxCommandLineBytes)}\"}}";
        Assert.Null(SkuaLine.Parse(relogin));
    }
}
