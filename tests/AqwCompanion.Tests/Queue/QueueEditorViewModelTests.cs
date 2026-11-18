using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueEditorViewModelTests
{
    private sealed class Source(IReadOnlyList<SkuaScriptInfo>? scripts) : IQueueScriptSource
    {
        public Task<IReadOnlyList<SkuaScriptInfo>?> ListAsync(string character, CancellationToken ct) => Task.FromResult(scripts);
    }

    private static async Task<(QueueEditorViewModel Vm, FakeQueues Queues)> OpenAsync(
        IReadOnlyList<QueueStep>? steps = null, bool listed = true, QueueResult? replace = null)
    {
        var queues = new FakeQueues();
        if (replace is not null)
        {
            queues.ReplaceResult = replace;
        }

        var vm = new QueueEditorViewModel(Hero, steps ?? [], queues, new Source(listed ? Scripts : null), ["Blood Orb"]);
        await vm.LoadAsync();
        return (vm, queues);
    }

    private static void Fill(QueueEditorRow row, int script = 0, string item = "Blood Orb", string quantity = "5", string minutes = "", string faction = "", string rank = "", QueueEditorViewModel? vm = null)
    {
        row.Script = vm is null ? new SkuaScriptInfo(script == 0 ? A : B, "x", "") : vm.Scripts[script];
        row.GoalItem = item;
        row.QuantityText = quantity;
        row.MinutesText = minutes;
        row.Faction = faction;
        row.RankText = rank;
    }

    [Fact]
    public async Task Opening_builds_a_row_per_stored_step_with_the_script_matched_by_id()
    {
        var (vm, _) = await OpenAsync([Step("1", A, "Dailies", "Blood Orb", 10, minutes: 30), Step("2", B, "Butler", "Gold", 3)]);

        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.ScriptsLoaded);
        Assert.Null(vm.ErrorText);
        Assert.Same(vm.Scripts[0], vm.Rows[0].Script);
        Assert.Same(vm.Scripts[1], vm.Rows[1].Script);
        Assert.Equal("Blood Orb", vm.Rows[0].GoalItem);
        Assert.Equal("10", vm.Rows[0].QuantityText);
        Assert.Equal("30", vm.Rows[0].MinutesText);
        Assert.Equal("", vm.Rows[1].MinutesText);
    }

    [Fact]
    public async Task Scripts_that_cannot_be_listed_show_the_text()
    {
        var (vm, _) = await OpenAsync([Step()], listed: false);

        Assert.False(vm.ScriptsLoaded);
        Assert.Equal(QueueEditorViewModel.NoScriptsText, vm.ErrorText);
        Assert.Equal("Dailies", vm.Rows[0].Script!.Name);
    }

    [Fact]
    public async Task Add_appends_an_empty_row_up_to_twenty()
    {
        var (vm, _) = await OpenAsync();

        vm.AddCommand.Execute(null);
        Assert.Single(vm.Rows);
        Assert.Equal("1", vm.Rows[0].QuantityText);
        Assert.Null(vm.Rows[0].Script);
        Assert.Equal(["Blood Orb"], vm.Rows[0].Suggestions);

        for (var i = 0; i < 30; i++)
        {
            vm.AddCommand.Execute(null);
        }

        Assert.Equal(20, vm.Rows.Count);
    }

    [Fact]
    public async Task Remove_and_move_reorder_and_stop_at_the_ends()
    {
        var (vm, _) = await OpenAsync([Step("1", item: "a"), Step("2", item: "b"), Step("3", item: "c")]);
        string Order() => string.Concat(vm.Rows.Select(r => r.GoalItem));

        vm.MoveUpCommand.Execute(vm.Rows[0]);
        Assert.Equal("abc", Order());
        vm.MoveDownCommand.Execute(vm.Rows[2]);
        Assert.Equal("abc", Order());
        vm.MoveDownCommand.Execute(vm.Rows[0]);
        Assert.Equal("bac", Order());
        vm.MoveUpCommand.Execute(vm.Rows[2]);
        Assert.Equal("bca", Order());
        vm.RemoveCommand.Execute(vm.Rows[1]);
        Assert.Equal("ba", Order());
    }

    [Fact]
    public async Task Save_replaces_with_the_validated_steps_and_closes()
    {
        var stored = Step("1", A, "Dailies", "Blood Orb", 10);
        var (vm, queues) = await OpenAsync([stored]);
        vm.AddCommand.Execute(null);
        Fill(vm.Rows[1], 1, "Gold", "2", "15", vm: vm);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        var (character, steps) = Assert.Single(queues.Replaced);
        Assert.Equal(Hero, character);
        Assert.Equal(2, steps.Count);
        Assert.Equal(stored.Id, steps[0].Id);
        Assert.Equal(32, steps[1].Id.Length);
        Assert.NotEqual(stored.Id, steps[1].Id);
        Assert.Equal(new QueueStep(steps[1].Id, B, "Butler", "Gold", 2, null, 15), steps[1]);
        Assert.Equal(1, closed);
        Assert.Null(vm.ErrorText);
    }

    public static IEnumerable<object[]> Refusals() =>
    [
        ["noscript", QueueEditorViewModel.NoScriptChosenText],
        ["q-abc", QueueEditorViewModel.BadQuantityText],
        ["q-0", QueueEditorViewModel.BadQuantityText],
        ["q-1000001", QueueEditorViewModel.BadQuantityText],
        ["q--5", QueueEditorViewModel.BadQuantityText],
        ["m-abc", QueueEditorViewModel.BadMinutesText],
        ["m-0", QueueEditorViewModel.BadMinutesText],
        ["m-1441", QueueEditorViewModel.BadMinutesText],
        ["r-abc", QueueEditorViewModel.BadRankText],
        ["faction-only", QueueValidator.BadReputation],
        ["rank-only", QueueValidator.BadReputation],
        ["rank-11", QueueValidator.BadReputation],
        ["noitem", QueueValidator.NeedItem],
        ["unknown", QueueValidator.UnknownScript],
    ];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Save_refuses_bad_rows_with_the_matching_text(string kind, string expected)
    {
        var (vm, queues) = await OpenAsync();
        vm.AddCommand.Execute(null);
        var row = vm.Rows[0];
        Fill(row, vm: vm);
        switch (kind)
        {
            case "noscript": row.Script = null; break;
            case "q-abc": row.QuantityText = "abc"; break;
            case "q-0": row.QuantityText = "0"; break;
            case "q-1000001": row.QuantityText = "1000001"; break;
            case "q--5": row.QuantityText = "-5"; break;
            case "m-abc": row.MinutesText = "abc"; break;
            case "m-0": row.MinutesText = "0"; break;
            case "m-1441": row.MinutesText = "1441"; break;
            case "r-abc": row.Faction = "Good"; row.RankText = "abc"; break;
            case "faction-only": row.Faction = "Good"; break;
            case "rank-only": row.RankText = "3"; break;
            case "rank-11": row.Faction = "Good"; row.RankText = "11"; break;
            case "noitem": row.GoalItem = "  "; break;
            case "unknown": row.Script = new SkuaScriptInfo("aaaaaaaaaaaaaaaa", "Gone", ""); break;
        }

        var closed = 0;
        vm.Closed += (_, _) => closed++;
        vm.SaveCommand.Execute(null);

        Assert.Equal(expected, vm.ErrorText);
        Assert.Empty(queues.Replaced);
        Assert.Equal(0, closed);
    }

    [Fact]
    public async Task More_than_twenty_rows_are_refused()
    {
        var (vm, queues) = await OpenAsync();
        for (var i = 0; i < 21; i++)
        {
            var row = new QueueEditorRow(Guid.NewGuid().ToString("N"), [], vm.Scripts[0], "x", "1", "", "", "");
            vm.Rows.Add(row);
        }

        vm.SaveCommand.Execute(null);

        Assert.Equal(QueueValidator.BadCount, vm.ErrorText);
        Assert.Empty(queues.Replaced);
    }

    [Fact]
    public async Task A_reputation_goal_is_saved_and_shown_again()
    {
        var (vm, queues) = await OpenAsync();
        vm.AddCommand.Execute(null);
        Fill(vm.Rows[0], faction: "Good", rank: "4", vm: vm);

        vm.SaveCommand.Execute(null);

        var saved = Assert.Single(queues.Replaced).Steps[0];
        Assert.Equal(new ReputationGoal("Good", 4), saved.Reputation);

        var (again, _) = await OpenAsync([saved]);
        Assert.Equal("Good", again.Rows[0].Faction);
        Assert.Equal("4", again.Rows[0].RankText);
    }

    [Fact]
    public async Task An_empty_list_can_be_saved_even_when_scripts_could_not_be_listed()
    {
        var (vm, queues) = await OpenAsync(listed: false);

        vm.SaveCommand.Execute(null);

        Assert.Empty(Assert.Single(queues.Replaced).Steps);
    }

    [Fact]
    public async Task Rows_without_a_script_list_are_refused_with_the_text()
    {
        var (vm, queues) = await OpenAsync([Step()], listed: false);

        vm.SaveCommand.Execute(null);

        Assert.Equal(QueueEditorViewModel.NoScriptsText, vm.ErrorText);
        Assert.Empty(queues.Replaced);
    }

    [Fact]
    public async Task A_refused_replace_shows_the_text_and_keeps_the_window_open()
    {
        var (vm, queues) = await OpenAsync([Step()], replace: QueueResult.Refused(QueueEngine.BusyText));
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        Assert.Single(queues.Replaced);
        Assert.Equal(QueueEngine.BusyText, vm.ErrorText);
        Assert.Equal(0, closed);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task Cancel_closes_and_changes_nothing()
    {
        var (vm, queues) = await OpenAsync([Step()]);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Empty(queues.Replaced);
    }

    [Fact]
    public async Task Hostile_item_text_is_kept_in_the_row_and_cleaned_by_the_validator()
    {
        var (vm, queues) = await OpenAsync();
        vm.AddCommand.Execute(null);
        var hostile = "Bl\u0007ood\u0000 Orb" + new string('x', 200);
        Fill(vm.Rows[0], item: hostile, vm: vm);

        Assert.Equal(hostile, vm.Rows[0].GoalItem);
        vm.SaveCommand.Execute(null);

        var item = Assert.Single(queues.Replaced).Steps[0].GoalItem;
        Assert.Equal(QueueLimits.MaxItemName, item.Length);
        Assert.DoesNotContain('\u0007', item);
        Assert.DoesNotContain('\u0000', item);
    }
}
