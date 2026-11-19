using System.Text.RegularExpressions;
using AqwCompanion.App.Boosts;
using AqwCompanion.App.Characters;
using AqwCompanion.App.Queue;
using AqwCompanion.App.Shell;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Boosts;

public class BoostsCardTests
{
    private sealed record Card(MainWindow Window, MainViewModelBuilder.Built Built, FakeBoosts Boosts, ManualTicker Ticker, FakeQueues Queues)
    {
        public T Named<T>(string name) where T : Control => Window.Named<T>(name);

        public BoostsViewModel Vm => Built.Main.Boosts!;
    }

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            a();
        }
        else
        {
            Dispatcher.UIThread.Post(a);
        }
    }

    private static Card Open(bool withBoosts = true, int queueSteps = 0, double width = 1100, double height = 800, params BoostView[] views)
    {
        var fake = new FakeBoosts();
        fake.Items.AddRange(views);
        var ticker = new ManualTicker();
        var queues = new FakeQueues();
        if (queueSteps > 0)
        {
            queues.Items.Add(new QueueView(
                Hero, [.. Enumerable.Range(1, queueSteps).Select(n => Step(n.ToString(), item: "Item " + n))], 0, QueueState.Running, QueuePhase.Running, null, 1, true));
        }

        var built = MainViewModelBuilder.Create(
            OnUi,
            queue: queueSteps > 0 ? (book, skua) => new QueueViewModel(queues, new CharacterSelection(book, skua), new FakeQueueEditorDialog()) : null,
            boosts: withBoosts ? (book, skua) => new BoostsViewModel(fake, new CharacterSelection(book, skua), OnUi, ticker.Every) : null);
        built.Book.OnStatus(new SkuaStatus(true, Hero, null, "map", null, false, null, null, null, "idle", null, false));
        var window = new MainWindow { DataContext = built.Main, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return new Card(window, built, fake, ticker, queues);
    }

    private static BoostItemView Gold(int id = 7, int qty = 33, bool canUse = true) =>
        new(id, "GOLD Boost! (1 hr)", "gold", qty, canUse);

    private static BoostView View(
        bool supported = true, bool online = true, int gold = 0, int cls = 0, int rep = 0, int xp = 0,
        IReadOnlyList<BoostItemView>? items = null, bool canRelogin = false) =>
        new("a1", Hero, supported, online, 0, gold, cls, rep, xp, items ?? [], canRelogin);

    private static string Texts(Visual root) =>
        string.Join("|", root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static void Refresh(Card c)
    {
        c.Boosts.Raise();
        Dispatcher.UIThread.RunJobs();
        c.Window.UpdateLayout();
    }

    private static double Bottom(Card c, Control control) =>
        Avalonia.VisualExtensions.TranslatePoint(control, new Point(0, control.Bounds.Height), c.Window)!.Value.Y;

    [AvaloniaFact]
    public void Without_a_boosts_view_model_the_card_is_hidden_and_the_dashboard_still_builds()
    {
        var c = Open(withBoosts: false, queueSteps: 2);

        Assert.False(c.Named<Border>("DashboardBoostsCard").IsEffectivelyVisible);
        Assert.True(c.Named<Border>("DashboardReputationCard").IsEffectivelyVisible);
        Assert.True(c.Named<Border>("DashboardHardFarmsCard").IsEffectivelyVisible);
        Assert.True(c.Named<Border>("DashboardQueueCard").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_card_shows_the_rows_the_items_and_no_log_in_button_while_logged_in()
    {
        var c = Open(views: View(gold: 840, cls: -1, items: [Gold(), new BoostItemView(8, "XP Boost! (1 hr)", "xp", 2, true)]));

        Assert.True(c.Named<Border>("DashboardBoostsCard").IsEffectivelyVisible);
        Assert.Equal("Gold: 14 min left|Class: active|Reputation: inactive|XP: inactive", string.Join("|", c.Named<ItemsControl>("BoostRows").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        var items = Texts(c.Named<ItemsControl>("BoostItems"));
        Assert.Contains("GOLD Boost! (1 hr) x33", items);
        Assert.Contains("XP Boost! (1 hr) x2", items);
        Assert.False(c.Named<Button>("BoostReloginButton").IsEffectivelyVisible);
        Assert.False(c.Named<Border>("BoostConfirmPanel").IsEffectivelyVisible);
        Assert.False(c.Named<TextBlock>("BoostNoItemsText").IsEffectivelyVisible);
        Assert.False(c.Named<TextBlock>("BoostsUpdateText").IsEffectivelyVisible);
        Assert.Equal("Boost times", AutomationProperties.GetName(c.Named<ItemsControl>("BoostRows")));
    }

    [AvaloniaFact]
    public async Task A_logged_out_character_shows_log_in_again_and_it_runs_through_the_visual_tree()
    {
        var c = Open(views: View(online: false, canRelogin: true));
        var b = c.Named<Button>("BoostReloginButton");
        Assert.True(b.IsEffectivelyVisible);
        Assert.True(b.IsEffectivelyEnabled);

        b.Command!.Execute(b.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        await c.Vm.Idle;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["a1"], c.Boosts.Relogged);
        Assert.Equal("Login started", c.Named<TextBlock>("BoostStatusText").Text);
        Assert.True(c.Named<TextBlock>("BoostStatusText").IsEffectivelyVisible);
        Assert.True(c.Named<TextBlock>("BoostNoItemsText").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Every_button_runs_its_command_through_the_visual_tree()
    {
        var c = Open(views: View(items: [Gold(), new BoostItemView(8, "XP Boost! (1 hr)", "xp", 2, false)]));
        var buttons = c.Named<ItemsControl>("BoostItems").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal("Use", buttons[0].Content);
        var panel = c.Named<Border>("BoostConfirmPanel");

        var disabled = buttons[1];
        Assert.False(disabled.IsEffectivelyEnabled);
        Assert.False(disabled.Command!.CanExecute(disabled.CommandParameter));

        buttons[0].Command!.Execute(buttons[0].CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsEffectivelyVisible);
        Assert.Equal("Use GOLD Boost! (1 hr)? You have 33.", c.Named<TextBlock>("BoostConfirmText").Text);
        Assert.Empty(c.Boosts.Used);

        var cancel = c.Named<Button>("BoostCancelButton");
        cancel.Command!.Execute(cancel.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.False(panel.IsEffectivelyVisible);
        Assert.Empty(c.Boosts.Used);

        buttons = [.. c.Named<ItemsControl>("BoostItems").GetVisualDescendants().OfType<Button>()];
        buttons[0].Command!.Execute(buttons[0].CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsEffectivelyVisible);
        var confirm = c.Named<Button>("BoostConfirmButton");
        Assert.True(confirm.IsEffectivelyEnabled);
        confirm.Command!.Execute(confirm.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        await c.Vm.Idle;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([("a1", 7)], c.Boosts.Used);
        Assert.False(panel.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_refusal_and_a_missing_answer_are_shown_in_the_status_line()
    {
        var c = Open(views: View(items: [Gold()]));
        async Task UseOnce()
        {
            var item = c.Named<ItemsControl>("BoostItems").GetVisualDescendants().OfType<Button>().First();
            item.Command!.Execute(item.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            var confirm = c.Named<Button>("BoostConfirmButton");
            confirm.Command!.Execute(confirm.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            await c.Vm.Idle;
            Dispatcher.UIThread.RunJobs();
        }

        c.Boosts.UseResult = BoostResult.Refused("The game did not activate the boost.");
        await UseOnce();
        var status = c.Named<TextBlock>("BoostStatusText");
        Assert.True(status.IsEffectivelyVisible);
        Assert.Equal("The game did not activate the boost.", status.Text);
        Assert.Equal("The game did not activate the boost.", AutomationProperties.GetName(status));

        c.Boosts.UseResult = BoostResult.Unanswered;
        await UseOnce();
        Assert.Equal(BoostTexts.NoAnswer, c.Named<TextBlock>("BoostStatusText").Text);
    }

    [AvaloniaFact]
    public void The_unsupported_view_shows_the_update_text_and_hides_the_rest()
    {
        var c = Open(views: View(supported: false, gold: 100, items: [Gold()], canRelogin: true));

        var update = c.Named<TextBlock>("BoostsUpdateText");
        Assert.True(update.IsEffectivelyVisible);
        Assert.Equal(BoostsViewModel.UpdateText, update.Text);
        Assert.False(c.Named<ItemsControl>("BoostRows").IsEffectivelyVisible);
        Assert.False(c.Named<ScrollViewer>("BoostItemsScroll").IsEffectivelyVisible);
        Assert.False(c.Named<Button>("BoostReloginButton").IsEffectivelyVisible);
        Assert.False(c.Named<TextBlock>("BoostNoItemsText").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_countdown_text_changes_when_the_ticker_ticks()
    {
        var c = Open(views: View(gold: 840));
        var rows = c.Named<ItemsControl>("BoostRows");
        Assert.Contains("Gold: 14 min left", Texts(rows));
        Assert.True(c.Ticker.Active);

        c.Boosts.Items[0] = View(gold: 780);
        c.Ticker.Tick();
        Dispatcher.UIThread.RunJobs();
        c.Window.UpdateLayout();

        Assert.Contains("Gold: 13 min left", Texts(rows));
        Assert.DoesNotContain("Gold: 14 min left", Texts(rows));
        Assert.True(c.Named<Border>("DashboardBoostsCard").Bounds.Height > 0);
    }

    [AvaloniaFact]
    public void The_boosts_card_has_no_raw_english_text()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AqwCompanion.App", "Dashboard", "DashboardView.axaml");
        var xaml = File.ReadAllText(path);
        var start = xaml.IndexOf("x:Name=\"DashboardBoostsCard\"", StringComparison.Ordinal);
        Assert.True(start > 0);
        var card = xaml[start..];
        var scanned = 0;
        foreach (var attribute in new[] { "AutomationProperties.Name", "Text", "Content", "Header", "ToolTip.Tip", "Watermark" })
        {
            foreach (Match m in Regex.Matches(card, "\\b" + Regex.Escape(attribute) + "=\"([^\"]*)\""))
            {
                scanned++;
                Assert.StartsWith("{", m.Groups[1].Value);
                Assert.DoesNotContain("StringFormat", m.Groups[1].Value);
            }
        }

        Assert.True(scanned > 8);
        Assert.DoesNotMatch(@">\s*[A-Za-z][^<{]*<", card.Replace("<!--", "").Replace("-->", ""));
    }

    private static IReadOnlyList<BoostItemView> Twelve() =>
        [.. Enumerable.Range(1, 12).Select(n => new BoostItemView(n, "GOLD Boost! (" + n + " hr)", "gold", 30 + n, true))];

    [AvaloniaFact]
    public void At_the_minimum_window_size_twelve_boosts_keep_every_button_reachable()
    {
        var c = Open(width: 900, height: 600, views: View(online: false, gold: 840, cls: 600, rep: 300, xp: 120, items: Twelve(), canRelogin: true));
        c.Window.Width = 900;
        c.Window.Height = 600;
        Refresh(c);

        var scroll = c.Named<ScrollViewer>("BoostItemsScroll");
        Assert.Equal(120, scroll.MaxHeight);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        var card = c.Named<Border>("DashboardBoostsCard");
        Assert.True(card.Bounds.Height < 300, "card " + card.Bounds.Height);
        Assert.True(Bottom(c, c.Named<Button>("BoostReloginButton")) <= c.Window.Bounds.Height);
        Assert.True(c.Named<Border>("DashboardReputationCard").Bounds.Height > 80);

        c.Boosts.Items[0] = View(cls: 600, rep: 300, xp: 120, items: Twelve());
        Refresh(c);
        var first = c.Named<ItemsControl>("BoostItems").GetVisualDescendants().OfType<Button>().First();
        Assert.True(Bottom(c, first) <= c.Window.Bounds.Height);
        first.Command!.Execute(first.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        c.Window.UpdateLayout();

        Assert.True(c.Named<Border>("BoostConfirmPanel").IsEffectivelyVisible);
        Assert.True(Bottom(c, c.Named<Button>("BoostConfirmButton")) <= c.Window.Bounds.Height);
        Assert.True(Bottom(c, c.Named<Button>("BoostCancelButton")) <= c.Window.Bounds.Height);
        Assert.True(card.Bounds.Height < 300, "card " + card.Bounds.Height);
        Assert.True(c.Named<Border>("DashboardReputationCard").Bounds.Height > 80);
    }

    [AvaloniaFact]
    public void The_two_cards_share_the_row_and_the_queue_card_keeps_its_limits()
    {
        var steps = 20;
        var c = Open(queueSteps: steps, width: 900, height: 600, views: View(gold: 840, items: Twelve()));
        c.Window.Width = 900;
        c.Window.Height = 600;
        Refresh(c);

        var queue = c.Named<Border>("DashboardQueueCard");
        var boosts = c.Named<Border>("DashboardBoostsCard");
        Assert.True(queue.IsEffectivelyVisible);
        Assert.True(boosts.IsEffectivelyVisible);
        var queueRight = Avalonia.VisualExtensions.TranslatePoint(queue, new Point(queue.Bounds.Width, 0), c.Window)!.Value.X;
        var boostsLeft = Avalonia.VisualExtensions.TranslatePoint(boosts, new Point(0, 0), c.Window)!.Value.X;
        Assert.True(boostsLeft >= queueRight, boostsLeft + " vs " + queueRight);
        Assert.Equal(140, c.Named<ScrollViewer>("QueueStepsScroll").MaxHeight);
        Assert.True(queue.Bounds.Height < 300, "queue " + queue.Bounds.Height);
        Assert.True(boosts.Bounds.Height < 300, "boosts " + boosts.Bounds.Height);
    }

    [AvaloniaFact]
    public void The_timer_stops_when_the_window_view_model_is_disposed()
    {
        var c = Open(views: View(gold: 840));
        Assert.True(c.Ticker.Active);
        Assert.Equal(1, c.Boosts.SubscriberCount);

        c.Vm.Dispose();
        Assert.False(c.Ticker.Active);
        Assert.Equal(0, c.Boosts.SubscriberCount);
        c.Window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Building_the_view_model_on_a_pool_thread_without_views_makes_no_timer()
    {
        var fake = new FakeBoosts();
        var ticker = new ManualTicker();
        var built = MainViewModelBuilder.Create(OnUi);
        var vm = await Task.Run(() => new BoostsViewModel(fake, new CharacterSelection(built.Book, built.Skua), OnUi, ticker.Every));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, ticker.Created);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task A_change_from_a_background_thread_reaches_the_card_on_the_UI_thread()
    {
        var c = Open(views: View());
        Assert.Contains("Gold: inactive", Texts(c.Named<ItemsControl>("BoostRows")));

        c.Boosts.Items[0] = View(gold: 840);
        await Task.Run(c.Boosts.Raise);
        Dispatcher.UIThread.RunJobs();
        c.Window.UpdateLayout();

        Assert.Contains("Gold: 14 min left", Texts(c.Named<ItemsControl>("BoostRows")));
        Assert.True(c.Ticker.Active);
    }
}
