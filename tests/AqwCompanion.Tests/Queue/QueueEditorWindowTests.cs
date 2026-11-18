using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueEditorWindowTests
{
    private sealed class Source : IQueueScriptSource
    {
        public Task<IReadOnlyList<SkuaScriptInfo>?> ListAsync(string character, CancellationToken ct) => Task.FromResult<IReadOnlyList<SkuaScriptInfo>?>(Scripts);
    }

    private static (QueueEditorWindow Window, QueueEditorViewModel Vm) Open()
    {
        var vm = new QueueEditorViewModel(Hero, [], new FakeQueues(), new Source(), ["Blood Orb"]);
        var window = new QueueEditorWindow { DataContext = vm };
        window.Show();
        return (window, vm);
    }

    private sealed class FixedSource(IReadOnlyList<SkuaScriptInfo>? list) : IQueueScriptSource
    {
        public Task<IReadOnlyList<SkuaScriptInfo>?> ListAsync(string character, CancellationToken ct) => Task.FromResult(list);
    }

    private static async Task<(QueueEditorWindow Window, QueueEditorViewModel Vm, FakeQueues Queues)> OpenStoredAsync(IReadOnlyList<SkuaScriptInfo>? list, params QueueStep[] steps)
    {
        var queues = new FakeQueues();
        var vm = new QueueEditorViewModel(Hero, steps, queues, new FixedSource(list), ["Blood Orb"]);
        var window = new QueueEditorWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await vm.LoadAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, vm, queues);
    }

    private static List<ComboBox> Pickers(QueueEditorWindow w) =>
        [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<ComboBox>().Where(c => c.Name == "QueueRowScript")];

    [AvaloniaFact]
    public async Task A_stored_queue_opens_with_the_listed_scripts_selected()
    {
        var (window, vm, queues) = await OpenStoredAsync(Scripts, Step("1", A, "Dailies"), Step("2", B, "Butler", "Gold"));

        var pickers = Pickers(window);
        Assert.Equal(2, pickers.Count);
        Assert.Same(vm.Scripts[0], pickers[0].SelectedItem);
        Assert.Same(vm.Scripts[1], pickers[1].SelectedItem);

        vm.SaveCommand.Execute(null);
        Assert.Equal(2, Assert.Single(queues.Replaced).Steps.Count);
    }

    [AvaloniaFact]
    public async Task A_stored_script_that_is_no_longer_listed_shows_nothing_selected_and_refuses_to_save()
    {
        var (window, vm, queues) = await OpenStoredAsync([new SkuaScriptInfo(B, "Butler", "Misc")], Step("1", A, "Dailies"));

        Assert.Null(Assert.Single(Pickers(window)).SelectedItem);
        Assert.Contains("Dailies", window.Named<TextBlock>("QueueRowMissing").Text);
        vm.SaveCommand.Execute(null);
        Assert.Equal(QueueValidator.UnknownScript, vm.ErrorText);
        Assert.Empty(queues.Replaced);

        Pickers(window)[0].SelectedIndex = 0;
        vm.SaveCommand.Execute(null);
        Assert.Equal("Butler", Assert.Single(queues.Replaced).Steps[0].ScriptName);
    }

    [AvaloniaFact]
    public async Task An_empty_or_missing_script_list_opens_without_a_crash()
    {
        var (w1, vm1, _) = await OpenStoredAsync([], Step("1", A, "Dailies"));
        Assert.Null(Assert.Single(Pickers(w1)).SelectedItem);
        Assert.Equal(QueueEditorViewModel.NoScriptsText, vm1.ErrorText);

        var (w2, vm2, _) = await OpenStoredAsync(null, Step("1", A, "Dailies"));
        Assert.Null(Assert.Single(Pickers(w2)).SelectedItem);
        Assert.False(vm2.ScriptsLoaded);
    }

    [AvaloniaFact]
    public async Task The_row_buttons_remove_and_reorder_through_the_window()
    {
        var (window, vm, _) = await OpenStoredAsync(Scripts, Step("1", item: "a"), Step("2", item: "b"), Step("3", item: "c"));
        string Order() => string.Concat(vm.Rows.Select(r => r.GoalItem));
        void Press(string name, int row)
        {
            var button = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>().Where(b => b.Name == name).ElementAt(row);
            Assert.True(button.Command!.CanExecute(button.CommandParameter));
            button.Command.Execute(button.CommandParameter);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Press("QueueRowDown", 0);
        Assert.Equal("bac", Order());
        Press("QueueRowUp", 2);
        Assert.Equal("bca", Order());
        Press("QueueRowRemove", 1);
        Assert.Equal("ba", Order());
    }

    [AvaloniaFact]
    public void The_window_has_no_raw_english_text_and_the_automation_names_are_the_constants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AqwCompanion.App", "Queue", "QueueEditorWindow.axaml");
        var xaml = File.ReadAllText(path);
        foreach (var attribute in new[] { "AutomationProperties.Name", "Text", "Content", "Title", "Watermark" })
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xaml, attribute + "=\"([^\"]*)\""))
            {
                Assert.StartsWith("{", m.Groups[1].Value);
                Assert.DoesNotContain("StringFormat", m.Groups[1].Value);
            }
        }

        var (window, _) = Open();
        Assert.Equal(QueueEditorViewModel.AddAutomation, Avalonia.Automation.AutomationProperties.GetName(window.FindControl<Button>("QueueEditorAdd")!));
        Assert.Equal(QueueEditorViewModel.SaveAutomation, Avalonia.Automation.AutomationProperties.GetName(window.FindControl<Button>("QueueEditorSave")!));
    }

    [AvaloniaFact]
    public void The_window_shows_the_title_and_its_controls()
    {
        var (window, _) = Open();

        Assert.Equal(QueueEditorViewModel.TitleText, window.Title);
        Assert.NotNull(window.FindControl<Button>("QueueEditorAdd"));
        Assert.NotNull(window.FindControl<Button>("QueueEditorSave"));
        Assert.NotNull(window.FindControl<Button>("QueueEditorCancel"));
        Assert.NotNull(window.FindControl<TextBlock>("QueueEditorError"));
        Assert.NotNull(window.FindControl<ItemsControl>("QueueEditorRows"));
    }

    [AvaloniaFact]
    public void Adding_a_row_shows_its_controls()
    {
        var (window, vm) = Open();

        vm.AddCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        foreach (var name in new[] { "QueueRowScript", "QueueRowItem", "QueueRowQuantity", "QueueRowMinutes", "QueueRowFaction", "QueueRowRank" })
        {
            Assert.True(window.Named<Control>(name).IsVisible, name);
        }
    }
}
