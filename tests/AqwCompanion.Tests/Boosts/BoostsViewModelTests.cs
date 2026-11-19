using System.Reflection;
using AqwCompanion.App.Boosts;
using AqwCompanion.App.Characters;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Boosts;

public class BoostsViewModelTests
{
    private const string Hero = "Hero";

    private sealed class Rig
    {
        public FakeBoosts Boosts { get; } = new();
        public FakeSkua Skua { get; } = new();
        public CharacterBook Book { get; } = new(new MemoryCharacterBookStore(), () => DateTimeOffset.UtcNow);
        public CharacterSelection Selection { get; }
        public ManualTicker Ticker { get; } = new();
        public List<Action> Posted { get; } = [];

        public Rig()
        {
            Selection = new CharacterSelection(Book, Skua);
            Know(Hero);
        }

        public BoostsViewModel Build(bool inline = true) =>
            new(Boosts, Selection, inline ? a => a() : Posted.Add, Ticker.Every);

        public void Know(string name) =>
            Book.OnStatus(new SkuaStatus(true, name, null, "map", null, false, null, null, null, "idle", null, false));
    }

    private static BoostItemView Gold(int id = 7, int qty = 33, bool canUse = true) =>
        new(id, "GOLD Boost! (1 hr)", "gold", qty, canUse);

    private static BoostView View(
        string? character = Hero, string id = "i1", bool supported = true, bool online = true, int gold = 0, int cls = 0,
        int rep = 0, int xp = 0, IReadOnlyList<BoostItemView>? items = null, bool canRelogin = false) =>
        new(id, character, supported, online, 0, gold, cls, rep, xp, items ?? [], canRelogin);

    [Fact]
    public void Without_a_view_for_the_selected_character_the_card_is_hidden()
    {
        var rig = new Rig();
        var vm = rig.Build();
        Assert.False(vm.HasView);
        Assert.False(vm.ShowBody || vm.ShowUpdate);

        rig.Boosts.Items.Add(View("Other"));
        rig.Boosts.Raise();
        Assert.False(rig.Build().HasView);

        var none = new Rig();
        none.Book.Forget();
        none.Boosts.Items.Add(View(Hero));
        Assert.False(none.Build().HasView);
    }

    [Fact]
    public void An_unsupported_view_shows_only_the_update_text()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(supported: false, gold: 100, items: [Gold()], canRelogin: true));
        var vm = rig.Build();

        Assert.True(vm.HasView);
        Assert.True(vm.ShowUpdate);
        Assert.False(vm.ShowBody);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Items);
        Assert.False(vm.CanRelogin);
        Assert.Equal("Update the plugin DLL to see boosts.", BoostsViewModel.UpdateText);
    }

    [Fact]
    public void The_rows_say_inactive_active_or_the_minutes_left()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(gold: 840, cls: -1, rep: 0, xp: 3600));
        var vm = rig.Build();

        Assert.Equal(["Gold: 14 min left", "Class: active", "Reputation: inactive", "XP: 60 min left"], vm.Rows.Select(r => r.Text));

        rig.Boosts.Items[0] = View(gold: 1, cls: 61, rep: 60, xp: 0);
        rig.Boosts.Raise();
        vm = rig.Build();
        Assert.Equal(["Gold: 1 min left", "Class: 2 min left", "Reputation: 1 min left", "XP: inactive"], vm.Rows.Select(r => r.Text));
    }

    [Fact]
    public void A_paused_view_adds_the_paused_suffix_to_the_active_rows_only()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(online: false, gold: 840, cls: -1));
        var vm = rig.Build();

        Assert.Equal(
            ["Gold: 14 min left (paused)", "Class: active (paused)", "Reputation: inactive", "XP: inactive"],
            vm.Rows.Select(r => r.Text));
    }

    [Fact]
    public void The_ticker_runs_only_while_a_row_counts_down_and_refreshes_the_rows_each_second()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View());
        var vm = rig.Build();
        Assert.Equal(0, rig.Ticker.Created);

        rig.Boosts.Items[0] = View(supported: false, gold: 100);
        rig.Boosts.Raise();
        rig.Boosts.Items[0] = View(online: false, gold: 100);
        rig.Boosts.Raise();
        rig.Boosts.Items[0] = View(cls: -1);
        rig.Boosts.Raise();
        Assert.Equal(0, rig.Ticker.Created);

        var clock = 0;
        BoostView Make() => View(gold: 840 - clock);
        rig.Boosts.Items[0] = Make();
        rig.Boosts.Raise();
        rig.Boosts.Raise();
        Assert.Equal(1, rig.Ticker.Created);
        Assert.Equal("Gold: 14 min left", vm.Rows[0].Text);

        var events = 0;
        vm.PropertyChanged += (_, _) => events++;
        for (var i = 0; i < 10; i++)
        {
            rig.Ticker.Tick();
        }

        Assert.Equal(0, events);

        clock = 60;
        rig.Boosts.Items[0] = Make();
        rig.Ticker.Tick();
        Assert.Equal("Gold: 13 min left", vm.Rows[0].Text);
        Assert.True(events > 0);

        rig.Boosts.Items[0] = View();
        rig.Ticker.Tick();
        Assert.False(rig.Ticker.Active);

        rig.Boosts.Items[0] = View(gold: 600);
        rig.Boosts.Raise();
        Assert.True(rig.Ticker.Active);
        vm.Dispose();
        Assert.False(rig.Ticker.Active);
        events = 0;
        rig.Ticker.Tick();
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task A_view_model_built_off_the_UI_thread_with_no_views_creates_no_timer()
    {
        var rig = new Rig();
        await Task.Run(() => rig.Build().Dispose());
        Assert.Equal(0, rig.Ticker.Created);
    }

    [Fact]
    public void The_items_list_names_each_boost_with_its_quantity_and_enables_Use_per_the_service()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold(), new BoostItemView(8, "XP Boost! (1 hr)", "xp", 2, false)]));
        var vm = rig.Build();

        Assert.True(vm.HasItems);
        Assert.False(vm.ShowNoItems);
        Assert.Equal(["GOLD Boost! (1 hr) x33", "XP Boost! (1 hr) x2"], vm.Items.Select(i => i.Text));
        Assert.True(vm.Items[0].CanUse);
        Assert.True(vm.Items[0].UseCommand.CanExecute(null));
        Assert.False(vm.Items[1].CanUse);
        Assert.False(vm.Items[1].UseCommand.CanExecute(null));

        rig.Boosts.Items[0] = View();
        rig.Boosts.Raise();
        Assert.True(vm.ShowNoItems);
    }

    [Fact]
    public async Task Use_asks_first_naming_the_item_and_the_quantity()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold()]));
        var vm = rig.Build();

        vm.Items[0].UseCommand.Execute(null);
        Assert.True(vm.Confirming);
        Assert.Equal("Use GOLD Boost! (1 hr)? You have 33.", vm.ConfirmQuestion);
        Assert.Empty(rig.Boosts.Used);

        vm.CancelCommand.Execute(null);
        Assert.False(vm.Confirming);
        Assert.Empty(rig.Boosts.Used);

        vm.Items[0].UseCommand.Execute(null);
        var gate = new TaskCompletionSource();
        rig.Boosts.UseHold = gate.Task;
        vm.ConfirmCommand.Execute(null);
        Assert.Equal([("i1", 7)], rig.Boosts.Used);
        Assert.False(vm.Confirming);
        Assert.False(vm.Items[0].UseCommand.CanExecute(null));
        Assert.False(vm.ReloginCommand.CanExecute(null));
        gate.SetResult();
        await vm.Idle;
        Assert.True(vm.Items[0].UseCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.StatusText);

        rig.Boosts.UseHold = null;
        rig.Boosts.UseResult = BoostResult.Refused("Nope.");
        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        await vm.Idle;
        Assert.Equal("Nope.", vm.StatusText);
        Assert.True(vm.HasStatus);

        rig.Boosts.UseResult = BoostResult.Unanswered;
        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        await vm.Idle;
        Assert.Equal(BoostTexts.NoAnswer, vm.StatusText);

        rig.Boosts.UseResult = BoostResult.Done;
        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        await vm.Idle;
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task A_second_confirm_while_one_is_running_does_not_send_twice()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold()]));
        var vm = rig.Build();
        var gate = new TaskCompletionSource();
        rig.Boosts.UseHold = gate.Task;

        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        Assert.Single(rig.Boosts.Used);
        gate.SetResult();
        await vm.Idle;
    }

    [Fact]
    public void Confirm_sends_the_item_captured_when_the_confirmation_opened()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold(7), new BoostItemView(9, "Other Gold", "gold", 1, true)]));
        var vm = rig.Build();
        vm.Items[0].UseCommand.Execute(null);

        rig.Boosts.Items[0] = View(items: [new BoostItemView(9, "Other Gold", "gold", 1, true), Gold(7)]);
        rig.Boosts.Raise();
        Assert.True(vm.Confirming);
        vm.ConfirmCommand.Execute(null);
        Assert.Equal([("i1", 7)], rig.Boosts.Used);
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("active")]
    [InlineData("logout")]
    [InlineData("other")]
    public void The_confirmation_is_dropped_when_the_view_changes_under_it(string change)
    {
        var rig = new Rig();
        rig.Know("Zed");
        rig.Selection.Choose(Hero);
        rig.Boosts.Items.Add(View(items: [Gold()]));
        rig.Boosts.Items.Add(View("Zed", "i2"));
        var vm = rig.Build();
        vm.Items[0].UseCommand.Execute(null);
        Assert.True(vm.Confirming);

        switch (change)
        {
            case "gone": rig.Boosts.Items[0] = View(items: []); break;
            case "active": rig.Boosts.Items[0] = View(gold: 100, items: [Gold()]); break;
            case "logout": rig.Boosts.Items[0] = View(online: false, items: [Gold()]); break;
            default: rig.Selection.Choose("Zed"); break;
        }

        rig.Boosts.Raise();
        Assert.False(vm.Confirming);
        vm.ConfirmCommand.Execute(null);
        Assert.Empty(rig.Boosts.Used);
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("zero")]
    [InlineData("active")]
    [InlineData("logout")]
    public void Confirm_rechecks_the_view_even_when_the_refresh_has_not_run_yet(string change)
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold()]));
        var vm = rig.Build();
        vm.Items[0].UseCommand.Execute(null);
        Assert.True(vm.Confirming);

        // No Raise: the queued refresh has not run, only the guard at Confirm can stop the send.
        rig.Boosts.Items[0] = change switch
        {
            "gone" => View(items: []),
            "zero" => View(items: [Gold(qty: 0)]),
            "active" => View(gold: 100, items: [Gold()]),
            _ => View(online: false, items: [Gold()]),
        };
        vm.ConfirmCommand.Execute(null);

        Assert.Empty(rig.Boosts.Used);
        Assert.False(vm.Confirming);
        Assert.True(vm.HasStatus);
        Assert.NotEqual(string.Empty, vm.StatusText);
    }

    [Fact]
    public void Confirm_refuses_when_the_skua_instance_changed_without_a_refresh()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold()]));
        var vm = rig.Build();
        vm.Items[0].UseCommand.Execute(null);
        rig.Boosts.Items[0] = View(id: "i2", items: [Gold()]);
        vm.ConfirmCommand.Execute(null);

        Assert.Empty(rig.Boosts.Used);
        Assert.Equal(BoostTexts.UnknownSkua, vm.StatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_result_for_a_deselected_character_is_dropped_and_the_commands_work_again(bool relogin)
    {
        var rig = new Rig();
        rig.Know("Zed");
        rig.Selection.Choose(Hero);
        rig.Boosts.Items.Add(View(online: !relogin, canRelogin: relogin, items: [Gold()]));
        rig.Boosts.Items.Add(View("Zed", "i2", items: [Gold()]));
        var vm = rig.Build();
        var gate = new TaskCompletionSource();
        rig.Boosts.UseHold = gate.Task;
        rig.Boosts.ReloginHold = gate.Task;
        rig.Boosts.UseResult = BoostResult.Refused("Late.");
        rig.Boosts.ReloginResult = BoostResult.Refused("Late.");
        if (relogin)
        {
            vm.ReloginCommand.Execute(null);
        }
        else
        {
            vm.Items[0].UseCommand.Execute(null);
            vm.ConfirmCommand.Execute(null);
        }

        rig.Selection.Choose("Zed");
        rig.Boosts.Raise();
        gate.SetResult();
        await vm.Idle;

        Assert.Equal(string.Empty, vm.StatusText);
        Assert.True(vm.Items[0].UseCommand.CanExecute(null));
    }

    [Fact]
    public void A_huge_remaining_time_never_shows_a_negative_or_wrapped_number()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(gold: int.MaxValue));
        var vm = rig.Build();
        Assert.Equal("Gold: 35791395 min left", vm.Rows[0].Text);
    }

    [Fact]
    public void Cancel_after_Dispose_does_not_bring_the_ticker_back()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(cls: 600, items: [Gold()]));
        var vm = rig.Build();
        vm.Items[0].UseCommand.Execute(null);
        Assert.True(vm.Confirming);
        vm.Dispose();
        Assert.False(rig.Ticker.Active);
        vm.CancelCommand.Execute(null);
        Assert.False(rig.Ticker.Active);
        Assert.Equal(1, rig.Ticker.Created);
    }

    [Fact]
    public void An_event_raised_right_after_construction_is_not_lost_and_the_first_refresh_is_marshalled()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(gold: 600));
        var vm = rig.Build(inline: false);

        Assert.Equal(1, rig.Boosts.SubscriberCount);
        Assert.Equal(0, rig.Ticker.Created);
        rig.Boosts.Raise();
        Assert.Single(rig.Posted);
        rig.Posted[0]();
        Assert.Equal("Gold: 10 min left", vm.Rows[0].Text);
        Assert.Equal(1, rig.Ticker.Created);
    }

    [Fact]
    public async Task The_error_line_clears_when_the_character_changes()
    {
        var rig = new Rig();
        rig.Know("Zed");
        rig.Selection.Choose(Hero);
        rig.Boosts.Items.Add(View(items: [Gold()]));
        rig.Boosts.Items.Add(View("Zed", "i2"));
        rig.Boosts.UseResult = BoostResult.Refused("Nope.");
        var vm = rig.Build();
        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        await vm.Idle;
        Assert.Equal("Nope.", vm.StatusText);

        rig.Selection.Choose("Zed");
        rig.Boosts.Raise();
        Assert.Equal(string.Empty, vm.StatusText);
    }

    [Fact]
    public async Task Log_in_again_is_visible_only_when_the_view_allows_it_and_sends_once()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View());
        var vm = rig.Build();
        Assert.False(vm.CanRelogin);
        Assert.False(vm.ReloginCommand.CanExecute(null));

        rig.Boosts.Items[0] = View(online: false, canRelogin: true);
        rig.Boosts.Raise();
        Assert.True(vm.CanRelogin);
        var gate = new TaskCompletionSource();
        rig.Boosts.ReloginHold = gate.Task;
        vm.ReloginCommand.Execute(null);
        vm.ReloginCommand.Execute(null);
        Assert.False(vm.ReloginCommand.CanExecute(null));
        gate.SetResult();
        await vm.Idle;
        Assert.Equal(["i1"], rig.Boosts.Relogged);
        Assert.Equal(BoostTexts.LoginStarted, vm.StatusText);

        rig.Boosts.ReloginHold = null;
        rig.Boosts.ReloginResult = BoostResult.Refused("Too soon.");
        vm.ReloginCommand.Execute(null);
        await vm.Idle;
        Assert.Equal("Too soon.", vm.StatusText);
    }

    [Fact]
    public async Task The_view_model_marshals_events_to_the_UI_thread_and_unsubscribes_on_Dispose()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View());
        var vm = rig.Build(inline: false);
        Assert.Equal(1, rig.Boosts.SubscriberCount);
        Assert.Single(rig.Posted);
        Assert.False(vm.HasView);
        rig.Posted[0]();
        rig.Posted.Clear();
        Assert.True(vm.HasView);

        rig.Boosts.Items[0] = View(gold: -1);
        await Task.Run(() =>
        {
            rig.Boosts.Raise();
            rig.Boosts.Raise();
        });
        Assert.Single(rig.Posted);
        Assert.Equal("Gold: inactive", vm.Rows[0].Text);
        rig.Posted[0]();
        Assert.Equal("Gold: active", vm.Rows[0].Text);

        rig.Posted.Clear();
        rig.Know("Zed");
        rig.Selection.Choose("Zed");
        Assert.Single(rig.Posted);

        vm.Dispose();
        Assert.Equal(0, rig.Boosts.SubscriberCount);
        rig.Boosts.Items[0] = View(gold: 600);
        rig.Posted[0]();
        Assert.False(rig.Ticker.Active);
        Assert.Equal("Gold: active", vm.Rows[0].Text);
    }

    [Fact]
    public async Task A_throwing_service_never_reaches_the_UI()
    {
        var rig = new Rig();
        rig.Boosts.Items.Add(View(items: [Gold()]));
        var vm = rig.Build();
        rig.Boosts.ThrowOnViews = true;
        rig.Boosts.Raise();
        Assert.False(vm.HasView);

        rig.Boosts.ThrowOnViews = false;
        rig.Boosts.Raise();
        Assert.True(vm.HasView);
        rig.Boosts.ThrowOnUse = true;
        vm.Items[0].UseCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        await vm.Idle;
        Assert.Equal(BoostTexts.Refused, vm.StatusText);
        Assert.True(vm.Items[0].UseCommand.CanExecute(null));
    }

    [Fact]
    public void Every_text_is_a_constant_on_the_view_model()
    {
        string[] names =
        [
            "TitleText", "UpdateText", "UseText", "ReloginText", "ConfirmText", "CancelText", "NoItemsText", "PausedSuffix",
            "ItemsAutomation", "RowsAutomation", "InactiveFormat", "ActiveFormat", "LeftFormat", "ItemFormat", "ConfirmFormat",
        ];
        foreach (var name in names)
        {
            var field = typeof(BoostsViewModel).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            Assert.True(field!.IsLiteral);
            Assert.Equal(typeof(string), field.FieldType);
        }
    }
}
