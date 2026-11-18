using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Queue;

public class QueueValidatorTests
{
    private const string A = "0123456789abcdef";
    private const string B = "fedcba9876543210";
    private static readonly IReadOnlyList<SkuaScriptInfo> Scripts =
        [new SkuaScriptInfo(A, "Dailies", "Farm"), new SkuaScriptInfo(B, "Butler", "Misc")];

    private static Func<string> Ids()
    {
        var n = 0;
        return () => (++n).ToString("x32");
    }

    private static QueueStepInput Input(string scriptId = A, string? item = "Blood Orb", int quantity = 10, ReputationGoal? rep = null, int? minutes = null, string? id = null) =>
        new(id, scriptId, item, quantity, rep, minutes);

    [Fact]
    public void A_good_step_gets_the_script_name_from_the_list_and_a_new_id()
    {
        var result = QueueValidator.Validate([Input(item: "  Blood Orb ")], Scripts, Ids());

        Assert.True(result.Ok);
        var step = Assert.Single(result.Steps);
        Assert.Equal(new QueueStep("00000000000000000000000000000001", A, "Dailies", "Blood Orb", 10), step);
    }

    [Fact]
    public void A_valid_unique_client_id_is_kept_and_a_repeated_or_malformed_one_is_replaced()
    {
        var keep = new string('a', 32);

        var result = QueueValidator.Validate([Input(id: keep), Input(id: keep), Input(id: "NOT-AN-ID")], Scripts, Ids());

        Assert.True(result.Ok);
        Assert.Equal(keep, result.Steps[0].Id);
        Assert.NotEqual(keep, result.Steps[1].Id);
        Assert.Matches("^[0-9a-f]{32}$", result.Steps[2].Id);
        Assert.Equal(3, result.Steps.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void A_script_the_plugin_does_not_list_is_refused()
    {
        var result = QueueValidator.Validate([Input(scriptId: "1111111111111111")], Scripts, Ids());

        Assert.False(result.Ok);
        Assert.Equal(QueueValidator.UnknownScript, result.Error);
    }

    [Theory]
    [InlineData("../../x")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcde")]
    [InlineData("")]
    public void A_script_id_that_is_not_sixteen_lowercase_hex_characters_is_refused(string id)
    {
        Assert.Equal(QueueValidator.UnknownScript, QueueValidator.Validate([Input(scriptId: id)], Scripts, Ids()).Error);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void A_queue_has_one_to_twenty_steps(int count, bool ok)
    {
        var result = QueueValidator.Validate([.. Enumerable.Range(0, count).Select(_ => Input())], Scripts, Ids());

        Assert.Equal(ok, result.Ok);
        if (!ok)
        {
            Assert.Equal(QueueValidator.BadCount, result.Error);
        }
    }

    [Fact]
    public void The_editor_may_save_an_empty_queue()
    {
        var result = QueueValidator.Validate([], Scripts, Ids(), allowEmpty: true);

        Assert.True(result.Ok);
        Assert.Empty(result.Steps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001\u0002")]
    public void An_item_name_is_needed(string? item)
    {
        Assert.Equal(QueueValidator.NeedItem, QueueValidator.Validate([Input(item: item)], Scripts, Ids()).Error);
    }

    [Fact]
    public void An_item_name_over_eighty_characters_is_cut_not_refused()
    {
        var result = QueueValidator.Validate([Input(item: new string('x', 120))], Scripts, Ids());

        Assert.Equal(80, result.Steps[0].GoalItem.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_001)]
    public void A_quantity_outside_one_to_a_million_is_refused(int quantity)
    {
        Assert.Equal(QueueValidator.BadQuantity, QueueValidator.Validate([Input(quantity: quantity)], Scripts, Ids()).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(-1)]
    public void A_time_cap_outside_one_to_1440_minutes_is_refused(int minutes)
    {
        Assert.Equal(QueueValidator.BadMinutes, QueueValidator.Validate([Input(minutes: minutes)], Scripts, Ids()).Error);
    }

    [Fact]
    public void A_time_cap_in_range_and_a_missing_one_are_kept()
    {
        var result = QueueValidator.Validate([Input(minutes: 1440), Input()], Scripts, Ids());

        Assert.Equal([1440, null], result.Steps.Select(s => s.MaxMinutes).ToArray());
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("Ebilcorp", 0)]
    [InlineData("Ebilcorp", 11)]
    public void A_reputation_goal_needs_a_faction_and_a_rank_from_one_to_ten(string faction, int rank)
    {
        Assert.Equal(QueueValidator.BadReputation, QueueValidator.Validate([Input(rep: new ReputationGoal(faction, rank))], Scripts, Ids()).Error);
    }

    [Fact]
    public void A_good_reputation_goal_is_kept_with_cleaned_text()
    {
        var result = QueueValidator.Validate([Input(rep: new ReputationGoal("  Ebilcorp\t", 10))], Scripts, Ids());

        Assert.Equal(new ReputationGoal("Ebilcorp", 10), result.Steps[0].Reputation);
    }

    [Fact]
    public void Normalize_drops_damaged_steps_caps_the_list_and_cleans_text()
    {
        var good = new QueueStep(new string('b', 32), A, "Dailies", "Blood Orb", 3);
        var damaged = new QueueStep("x", "bad", "n", "", 0);

        var result = QueueValidator.Normalize([null, damaged, good with { GoalItem = " Blood Orb\t" }, good]);

        var step = Assert.Single(result);   // the duplicate id of the second good step is dropped too
        Assert.Equal("Blood Orb", step.GoalItem);
        Assert.Equal(20, QueueValidator.Normalize(Enumerable.Range(0, 30).Select(i => good with { Id = i.ToString("x32") })).Count);
        Assert.Empty(QueueValidator.Normalize(null));
    }
}
