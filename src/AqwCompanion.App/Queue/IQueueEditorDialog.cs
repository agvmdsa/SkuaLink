using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Queue;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace AqwCompanion.App.Queue;

/// <summary>Opens the queue editor of a character over the main window.</summary>
public interface IQueueEditorDialog
{
    void Show(string character);
}

/// <summary>Shows <see cref="QueueEditorWindow"/> as a dialog of the main window; a second request brings the open one forward.</summary>
public sealed class AvaloniaQueueEditorDialog(ISkuaQueues queues, IQueueScriptSource scripts, CharacterBook characters) : IQueueEditorDialog
{
    private QueueEditorWindow? _open;

    public void Show(string character)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }

        var view = queues.Find(character) ?? QueueView.Empty(character);
        var vm = new QueueEditorViewModel(character, view.Steps, queues, scripts, QueueItemNames.From(characters.Find(character)), view.Revision);
        var window = new QueueEditorWindow { DataContext = vm };
        void Close(object? sender, EventArgs e) => window.Close();
        vm.Closed += Close;
        window.Closed += (_, _) =>
        {
            vm.Closed -= Close;
            _open = null;
        };
        _open = window;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        _ = vm.LoadAsync();
    }
}
