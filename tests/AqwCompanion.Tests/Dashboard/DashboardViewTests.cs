using System.Diagnostics;
using AqwCompanion.App.Characters;
using AqwCompanion.App.Dashboard;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Shell;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AqwCompanion.Tests.Dashboard;

public class DashboardViewTests
{
    private sealed record Host(MainViewModelBuilder.Built Built, MainWindow Window)
    {
        public MainViewModel Main => Built.Main;

        public T Named<T>(string name) where T : Control => Window.Named<T>(name);

        public IEnumerable<string?> Texts(string name) =>
            Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == name).Select(t => t.Text);
    }

    private static Host Build(int height = 720)
    {
        var built = MainViewModelBuilder.Create(action =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        });
        var window = new MainWindow { DataContext = built.Main, Width = 1100, Height = height };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return new Host(built, window);
    }

    private static readonly SkuaProfile HeroProfile =
        new(60, 123_456, 654_321, 9_876_543_210, 777, 10, true, "Void Highlord", 31, 302_500, "Heroes", 140, 300, 50);

    private static readonly SkuaFaction[] HeroFactions =
    [
        new(1, "Fictional Order", 3, 5000, 1400, 6400),
        new(2, "Other Order", 10, 302500, 0, 0),
        new(3, "Fresh Order", 1, 0, 0, 900),
    ];

    private static SkuaStatus Status(string name, SkuaProfile? profile, params SkuaFaction[] factions) =>
        new(true, name, null, null, null, false, null, [], factions, null, null, false, profile);

    // The characters Skua reported (in this order), then the window settles on the last one or the one it selects.
    private static Host Reported(params string[] names)
    {
        var h = Build();
        foreach (var name in names)
        {
            h.Built.Book.OnStatus(Status(name, HeroProfile, HeroFactions));
        }

        SettleUntil(h.Window, () => h.Main.Characters.HasCharacters, "the reported character");
        return h;
    }

    private static void GoLive(Host h, string name, SkuaProfile? profile = null)
    {
        var status = Status(name, profile ?? HeroProfile, HeroFactions);
        h.Built.Skua.Current = new SkuaHubView([FakeSkua.Instance("a", name, status: status)]);
        h.Built.Book.OnStatus(status);
        h.Built.Skua.Raise();
    }

    private static void SettleUntil(Window window, Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Timed out waiting for: {what}.");
            Thread.Sleep(5);
        }
    }

    // The top edge of a control in the window's coordinates, summed up the visual parents (nothing here is transformed).
    private static double TopOf(Control control, Avalonia.Visual root)
    {
        double top = 0;
        for (Avalonia.Visual? v = control; v is not null && !ReferenceEquals(v, root); v = v.GetVisualParent())
        {
            top += v.Bounds.Y;
        }

        return top;
    }

    [AvaloniaFact]
    public void The_window_has_no_tabs_and_shows_the_dashboard()
    {
        var h = Build();

        Assert.Empty(h.Window.GetVisualDescendants().OfType<TabControl>());
        Assert.Single(h.Window.GetVisualDescendants().OfType<DashboardView>());
    }

    [AvaloniaFact]
    public void Before_any_character_the_dashboard_asks_for_skua_and_hides_the_character_strip()
    {
        var h = Build();

        var empty = h.Named<TextBlock>("DashboardEmptyState");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(CharactersViewModel.EmptyText, empty.Text);
        Assert.False(h.Named<Border>("DashboardProfileCard").IsEffectivelyVisible);
        Assert.True(h.Named<TextBlock>("DashboardFactionsEmpty").IsEffectivelyVisible);
        Assert.Equal("Skua not connected.", h.Named<TextBlock>("FooterText").Text);
        Assert.False(h.Named<TextBlock>("FooterCaptured").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_reported_character_fills_the_character_strip_and_orders_the_factions_by_what_is_missing()
    {
        var h = Reported("Fictional Hero");

        Assert.False(h.Named<Border>("DashboardEmptyCard").IsEffectivelyVisible);
        Assert.Equal("Fictional Hero · Level 60 · Void Highlord rank 10", h.Named<TextBlock>("DashboardProfileHeadline").Text);
        var figures = h.Named<ItemsControl>("DashboardFigures");
        SettleUntil(h.Window, () => figures.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "9,876,543,210"), "the figures");
        var texts = figures.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Gold", texts);
        Assert.Contains("123,456 / 654,321", texts);
        Assert.Contains("140 bag, 300 bank, 50 house", texts);
        Assert.StartsWith("Last seen ", h.Named<TextBlock>("DashboardCapturedText").Text);

        SettleUntil(h.Window, () => h.Texts("DashboardFactionName").Count() == 3, "the faction rows");
        Assert.Equal(["Fresh Order", "Fictional Order", "Other Order"], h.Texts("DashboardFactionName"));
        Assert.Contains("900 to next", h.Texts("DashboardFactionToNext"));
    }

    [AvaloniaFact]
    public void A_live_character_follows_skua_without_pressing_anything()
    {
        var h = Reported("Fictional Hero");

        GoLive(h, "Fictional Hero", HeroProfile with { Gold = 9_876_544_450 });

        SettleUntil(
            h.Window,
            () => h.Named<ItemsControl>("DashboardFigures").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "9,876,544,450"),
            "the gold to follow Skua");
        Assert.Equal(CharactersViewModel.LiveText, h.Named<TextBlock>("DashboardCapturedText").Text);
        Assert.DoesNotContain(h.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "DashboardLastGain");
    }

    [AvaloniaFact]
    public void The_character_chip_waits_for_a_character_then_names_it()
    {
        var empty = Build();
        Assert.Equal(CharactersViewModel.NoCharacterText, empty.Named<TextBlock>("CharacterChipText").Text);
        Assert.Equal("?", empty.Named<TextBlock>("CharacterInitial").Text);

        var one = Reported("Fictional Hero");
        SettleUntil(one.Window, () => one.Named<TextBlock>("CharacterChipText").Text == "Fictional Hero", "the chip to name the character");
        Assert.Equal("F", one.Named<TextBlock>("CharacterInitial").Text);
        var chip = one.Named<Button>("CharacterChip");
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(chip)));
        Assert.NotNull(chip.Flyout);
    }

    [AvaloniaFact]
    public void Several_characters_are_listed_in_the_chip_flyout_and_picking_one_switches_the_dashboard()
    {
        var two = Reported("First Hero", "Second Hero");
        SettleUntil(two.Window, () => two.Main.Characters.CharacterNames.Count == 2, "both characters");
        var chip = two.Named<Button>("CharacterChip");

        chip.Flyout!.ShowAt(chip);
        SettleUntil(two.Window, () => chip.Flyout.IsOpen, "the flyout to open");
        var list = Assert.IsType<ListBox>(((StackPanel)((Flyout)chip.Flyout).Content!).Children.OfType<ListBox>().Single());
        Assert.Equal(2, list.ItemCount);

        var other = two.Main.Characters.SelectedCharacter == "First Hero" ? "Second Hero" : "First Hero";
        list.SelectedItem = other;

        SettleUntil(two.Window, () => two.Named<TextBlock>("DashboardProfileHeadline").Text?.StartsWith(other + " · Level 60") == true, "the other character's headline");
        Assert.Equal(other, two.Named<TextBlock>("CharacterChipText").Text);
        Assert.Equal(other, list.SelectedItem);
    }

    [AvaloniaFact]
    public void The_footer_follows_skua_and_says_whether_the_character_is_live_or_remembered()
    {
        var h = Reported("Fictional Hero");
        var captured = h.Named<TextBlock>("FooterCaptured");
        Assert.Equal("Skua not connected.", h.Named<TextBlock>("FooterText").Text);
        Assert.True(captured.IsEffectivelyVisible);
        Assert.StartsWith("Last seen ", captured.Text);
        Assert.DoesNotContain("live", h.Named<TextBlock>("FooterDot").Classes);

        GoLive(h, "Fictional Hero");

        SettleUntil(h.Window, () => h.Named<TextBlock>("FooterText").Text == "1 Skua connected.", "the footer to see Skua");
        Assert.Equal(CharactersViewModel.LiveText, captured.Text);
        Assert.Equal(captured.Text, AutomationProperties.GetName(captured));
        Assert.Contains("live", h.Named<TextBlock>("FooterDot").Classes);
    }

    [AvaloniaFact]
    public void The_settings_button_opens_the_settings_dialog()
    {
        var h = Build();
        var button = h.Named<Button>("SettingsButton");

        button.Command!.Execute(null);

        Assert.Same(h.Main.Settings, Assert.Single(h.Built.Dialog.Shown));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
        Assert.IsType<Avalonia.Controls.Shapes.Path>(button.Content);
    }

    [AvaloniaFact]
    public void The_hard_farms_card_shows_the_hard_farms_view_model()
    {
        var h = Build();

        var view = h.Named<HardFarmsView>("DashboardHardFarms");

        Assert.Same(h.Main.HardFarms, view.DataContext);
        Assert.True(h.Named<Border>("DashboardHardFarmsCard").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_cards_stay_inside_the_window_at_the_smallest_height()
    {
        var h = Reported("Fictional Hero");
        h.Window.Height = 600;
        SettleUntil(h.Window, () => h.Window.Bounds.Height <= 600.5, "the window to shrink");
        h.Window.UpdateLayout();

        var footerTop = TopOf(h.Named<Border>("Footer"), h.Window);
        foreach (var name in new[] { "DashboardProfileCard", "DashboardReputationCard", "DashboardHardFarmsCard" })
        {
            var card = h.Named<Border>(name);
            var bottom = TopOf(card, h.Window) + card.Bounds.Height;
            Assert.True(bottom <= footerTop + 0.5, $"{name} reaches {bottom} but the footer starts at {footerTop}");
        }
    }
}
