using AqwCompanion.App.Characters;
using AqwCompanion.App.Dashboard;
using AqwCompanion.App.Queue;
using AqwCompanion.App.Shell;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueCardTests
{
    private sealed record Card(MainWindow Window, MainViewModelBuilder.Built Built, FakeQueues Queues, FakeQueueEditorDialog Dialog)
    {
        public T Named<T>(string name) where T : Control => Window.Named<T>(name);
    }

    private static Card Open(bool withQueue = true, params QueueView[] views)
    {
        var queues = new FakeQueues();
        queues.Items.AddRange(views);
        var dialog = new FakeQueueEditorDialog();
        var built = MainViewModelBuilder.Create(
            a => { if (Dispatcher.UIThread.CheckAccess()) { a(); } else { Dispatcher.UIThread.Post(a); } },
            queue: withQueue ? (book, skua) => new QueueViewModel(queues, new CharacterSelection(book, skua), dialog) : null);
        built.Book.OnStatus(new SkuaStatus(true, Hero, null, "map", null, false, null, null, null, "idle", null, false));
        var window = new MainWindow { DataContext = built.Main, Width = 1100, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return new Card(window, built, queues, dialog);
    }

    private static QueueView View(QueueState state, QueuePhase? phase = null, string? reason = null, int? have = null, bool canEdit = true, int cursor = 0) =>
        new(Hero, [Step("1", item: "Blood Orb", quantity: 100), Step("2", B, "Butler", "Gold", 5)], cursor, state, phase, reason, have, canEdit);

    private static string Enabled(Card c) =>
        string.Join(",", new[] { "Start", "Pause", "Continue", "Skip", "Stop", "Edit" }.Where(n => c.Named<Button>("Queue" + n + "Button").IsEffectivelyEnabled));

    [AvaloniaFact]
    public void Without_a_queue_view_model_the_card_is_hidden_and_the_dashboard_still_builds()
    {
        var c = Open(withQueue: false);

        Assert.False(c.Named<Border>("DashboardQueueCard").IsEffectivelyVisible);
        Assert.True(c.Named<Border>("DashboardReputationCard").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_card_shows_the_state_progress_steps_and_marks_the_current_step()
    {
        var c = Open(true, View(QueueState.Running, QueuePhase.Running, have: 37));

        Assert.True(c.Named<Border>("DashboardQueueCard").IsEffectivelyVisible);
        Assert.Equal("Running: watching the script", c.Named<TextBlock>("QueueStateText").Text);
        Assert.Equal("37 of 100 Blood Orb", c.Named<TextBlock>("QueueProgressText").Text);
        Assert.False(c.Named<TextBlock>("QueueReasonText").IsEffectivelyVisible);
        var texts = c.Named<ItemsControl>("QueueSteps").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("1. Dailies: Blood Orb x100", texts);
        Assert.Contains("2. Butler: Gold x5", texts);
        Assert.Contains(">", texts);
        Assert.Equal("Running: watching the script", AutomationProperties.GetName(c.Named<TextBlock>("QueueStateText")));
    }

    [AvaloniaFact]
    public void The_reason_is_visible_only_when_there_is_one()
    {
        var c = Open(true, View(QueueState.Paused, reason: "Script not found."));

        Assert.True(c.Named<TextBlock>("QueueReasonText").IsEffectivelyVisible);
        Assert.Equal("Script not found.", c.Named<TextBlock>("QueueReasonText").Text);

        c.Queues.Items[0] = View(QueueState.Stopped);
        c.Queues.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.False(c.Named<TextBlock>("QueueReasonText").IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(QueueState.Stopped, null, "Start,Edit")]
    [InlineData(QueueState.Running, QueuePhase.Running, "Pause,Skip,Stop,Edit")]
    [InlineData(QueueState.Running, QueuePhase.Settling, "Pause,Skip,Stop")]
    [InlineData(QueueState.Paused, null, "Continue,Skip,Stop,Edit")]
    [InlineData(QueueState.Finished, null, "Start,Edit")]
    public void The_buttons_are_enabled_per_state(QueueState state, QueuePhase? phase, string expected)
    {
        var c = Open(true, View(state, phase, canEdit: phase is null or QueuePhase.Running));

        Assert.Equal(expected, Enabled(c));
    }

    [AvaloniaFact]
    public void Each_button_runs_its_command_through_the_visual_tree()
    {
        var c = Open(true, View(QueueState.Stopped));
        void Press(string name)
        {
            var b = c.Named<Button>(name);
            Assert.True(b.IsEffectivelyEnabled, name);
            b.Command!.Execute(b.CommandParameter);
            Dispatcher.UIThread.RunJobs();
        }

        Press("QueueStartButton");
        c.Queues.Items[0] = View(QueueState.Running, QueuePhase.Running);
        c.Queues.Raise();
        Dispatcher.UIThread.RunJobs();
        Press("QueuePauseButton");
        Press("QueueSkipButton");
        Press("QueueStopButton");
        c.Queues.Items[0] = View(QueueState.Paused);
        c.Queues.Raise();
        Dispatcher.UIThread.RunJobs();
        Press("QueueContinueButton");
        Press("QueueEditButton");

        Assert.Equal(
            [QueueCommand.Start, QueueCommand.Pause, QueueCommand.Skip, QueueCommand.Stop, QueueCommand.Continue],
            c.Queues.Commands.Select(x => x.Command));
        Assert.Equal(Hero, Assert.Single(c.Dialog.Shown));
    }

    [AvaloniaFact]
    public void A_refusal_is_shown_on_the_card()
    {
        var c = Open(true, View(QueueState.Stopped));
        c.Queues.CommandResult = QueueResult.Refused("Another script is running");

        var b = c.Named<Button>("QueueStartButton");
        b.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Another script is running", c.Named<TextBlock>("QueueErrorText").Text);
        Assert.True(c.Named<TextBlock>("QueueErrorText").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_character_without_steps_shows_the_empty_text()
    {
        var c = Open(true);

        Assert.Equal(QueueViewModel.EmptyText, c.Named<TextBlock>("QueueEmptyText").Text);
        Assert.Equal("Edit", Enabled(c));
    }

    [AvaloniaFact]
    public void The_queue_card_has_no_raw_english_text()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AqwCompanion.App", "Dashboard", "DashboardView.axaml");
        var xaml = File.ReadAllText(path);
        var start = xaml.IndexOf("x:Name=\"DashboardQueueCard\"", StringComparison.Ordinal);
        Assert.True(start > 0);
        var card = xaml[start..];
        var scanned = 0;
        foreach (var attribute in new[] { "AutomationProperties.Name", "Text", "Content", "Header", "ToolTip.Tip", "Watermark" })
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(card, "\\b" + System.Text.RegularExpressions.Regex.Escape(attribute) + "=\"([^\"]*)\""))
            {
                scanned++;
                Assert.StartsWith("{", m.Groups[1].Value);
                Assert.DoesNotContain("StringFormat", m.Groups[1].Value);
            }
        }

        Assert.True(scanned > 10);
        Assert.DoesNotMatch(@">\s*[A-Za-z][^<{]*<", card.Replace("<!--", "").Replace("-->", ""));
    }
}
