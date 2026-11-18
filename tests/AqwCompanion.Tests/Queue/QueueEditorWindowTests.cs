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
