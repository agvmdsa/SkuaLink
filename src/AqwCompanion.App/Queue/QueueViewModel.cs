using System.Globalization;
using AqwCompanion.App.Characters;
using AqwCompanion.Phone.Queue;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Queue;

/// <summary>One line of the queue card: its text, whether it is the current step and the marker shown before it.</summary>
public sealed record QueueStepRow(string Text, bool IsCurrent, string Marker);

/// <summary>
/// The dashboard card of the selected character's script queue: state, progress, steps and the controls. It only shows what
/// <see cref="ISkuaQueues"/> reports and sends commands; the engine decides. The queues, the selection and the book raise their
/// events from other threads, so the refresh runs through <c>onUiThread</c> (one queued at a time); it unsubscribes on
/// <see cref="Dispose"/>. It never throws into the UI.
/// </summary>
public sealed partial class QueueViewModel : ObservableObject, IDisposable
{
    public const string TitleText = "Script queue";
    public const string StartText = "Start";
    public const string PauseText = "Pause";
    public const string ContinueText = "Continue";
    public const string SkipText = "Skip step";
    public const string StopText = "Stop queue";
    public const string EditText = "Edit queue";
    public const string NoCharacterText = "Choose a character first.";
    public const string EmptyText = "No steps yet. Press Edit queue to add some.";
    public const string StoppedText = "Stopped";
    public const string RunningText = "Running";
    public const string PausedText = "Paused";
    public const string FinishedText = "Finished";
    public const string StartingText = "starting the script";
    public const string WatchingText = "watching the script";
    public const string StoppingText = "stopping the script";
    public const string SettlingText = "waiting for Skua to settle";
    public const string LastNoticePrefix = "Last notice: ";
    public const string StepsAutomation = "Steps of the queue";

    private readonly ISkuaQueues _queues;
    private readonly CharacterSelection _selection;
    private readonly IQueueEditorDialog _editor;
    private readonly Action<Action> _onUiThread;
    private int _refreshQueued;
    private bool _disposed;

    public QueueViewModel(ISkuaQueues queues, CharacterSelection selection, IQueueEditorDialog editor, Action<Action>? onUiThread = null)
    {
        _queues = queues;
        _selection = selection;
        _editor = editor;
        _onUiThread = onUiThread ?? (action => action());
        Refresh();
        queues.Changed += OnChanged;
        selection.Changed += OnChanged;
    }

    public bool HasCharacter { get; private set; }

    public string? Character { get; private set; }

    public string StateText { get; private set; } = StoppedText;

    public string ProgressText { get; private set; } = string.Empty;

    public bool HasProgress => ProgressText.Length > 0;

    public string ReasonText { get; private set; } = string.Empty;

    public bool HasReason => ReasonText.Length > 0;

    public string NoticeText { get; private set; } = string.Empty;

    public bool HasNotice => NoticeText.Length > 0;

    public string ErrorText { get; private set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    public IReadOnlyList<QueueStepRow> Steps { get; private set; } = [];

    public bool HasSteps => Steps.Count > 0;

    /// <summary>What the list shows while it is empty: asks for a character, or for steps; empty when there are steps.</summary>
    public string EmptyStateText { get; private set; } = NoCharacterText;

    public bool HasEmptyState => EmptyStateText.Length > 0;

    public bool CanStart { get; private set; }

    public bool CanPause { get; private set; }

    public bool CanContinue { get; private set; }

    public bool CanSkip { get; private set; }

    public bool CanStop { get; private set; }

    public bool CanEdit { get; private set; }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Send(QueueCommand.Start);

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => Send(QueueCommand.Pause);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => Send(QueueCommand.Continue);

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void Skip() => Send(QueueCommand.Skip);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Send(QueueCommand.Stop);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (Character is { } character && CanEdit)
        {
            try
            {
                _editor.Show(character);
            }
            catch (Exception)
            {
                // The dialog is a convenience; a failure to open it must not reach the UI.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queues.Changed -= OnChanged;
        _selection.Changed -= OnChanged;
    }

    private void Send(QueueCommand command)
    {
        if (Character is not { } character)
        {
            return;
        }

        QueueResult result;
        try
        {
            result = _queues.Command(character, command);
        }
        catch (Exception)
        {
            return;
        }

        ErrorText = result.Ok ? string.Empty : result.Error ?? string.Empty;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        try
        {
            _onUiThread(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                if (!_disposed)
                {
                    Refresh();
                }
            });
        }
        catch (Exception)
        {
            Volatile.Write(ref _refreshQueued, 0);
        }
    }

    private void Refresh()
    {
        var character = SafeSelected();
        QueueView? view = null;
        try
        {
            view = character is null ? null : _queues.Find(character);
        }
        catch (Exception)
        {
            // A queue that cannot be read shows as an empty one.
        }

        var steps = view?.Steps ?? [];
        var state = view?.State ?? QueueState.Stopped;
        var marked = state is QueueState.Running or QueueState.Paused;
        if (!string.Equals(Character, character, StringComparison.Ordinal))
        {
            ErrorText = string.Empty;
        }

        HasCharacter = character is not null;
        Character = character;
        Steps = [.. steps.Select((s, i) => Row(s, i, marked && i == view!.Cursor))];
        StateText = StateOf(state, view?.Phase);
        ReasonText = view?.Reason ?? string.Empty;
        NoticeText = view?.LastNotice is { Length: > 0 } notice ? LastNoticePrefix + notice : string.Empty;
        ProgressText = marked && view is { Have: { } have } && view.Cursor >= 0 && view.Cursor < steps.Count
            ? $"{have.ToString(CultureInfo.InvariantCulture)} of {steps[view.Cursor].GoalQuantity.ToString(CultureInfo.InvariantCulture)} {steps[view.Cursor].GoalItem}"
            : string.Empty;
        EmptyStateText = character is null ? NoCharacterText : steps.Count == 0 ? EmptyText : string.Empty;
        CanStart = HasCharacter && state is QueueState.Stopped or QueueState.Finished && steps.Count > 0;
        CanPause = HasCharacter && state == QueueState.Running;
        CanContinue = HasCharacter && state == QueueState.Paused;
        CanSkip = HasCharacter && state is QueueState.Running or QueueState.Paused;
        CanStop = CanSkip;
        CanEdit = HasCharacter && (view?.CanEdit ?? true);
        OnPropertyChanged(string.Empty);
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
    }

    private string? SafeSelected()
    {
        try
        {
            return _selection.Selected;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static QueueStepRow Row(QueueStep step, int index, bool current)
    {
        var text = $"{(index + 1).ToString(CultureInfo.InvariantCulture)}. {step.ScriptName}: {step.GoalItem} x{step.GoalQuantity.ToString(CultureInfo.InvariantCulture)}";
        if (step.Reputation is { } rep)
        {
            text += $" / {rep.Faction} rank {rep.Rank.ToString(CultureInfo.InvariantCulture)}";
        }

        if (step.MaxMinutes is { } minutes)
        {
            text += $" / limit {minutes.ToString(CultureInfo.InvariantCulture)} min";
        }

        return new QueueStepRow(text, current, current ? ">" : string.Empty);
    }

    private static string StateOf(QueueState state, QueuePhase? phase) => state switch
    {
        QueueState.Running => phase switch
        {
            QueuePhase.Starting => RunningText + ": " + StartingText,
            QueuePhase.Running => RunningText + ": " + WatchingText,
            QueuePhase.Stopping => RunningText + ": " + StoppingText,
            QueuePhase.Settling => RunningText + ": " + SettlingText,
            _ => RunningText,
        },
        QueueState.Paused => PausedText,
        QueueState.Finished => FinishedText,
        _ => StoppedText,
    };
}
