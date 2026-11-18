using System.Collections.ObjectModel;
using System.Globalization;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Queue;

/// <summary>One editable step of the editor; every field is the text the owner typed, checked only on Save.</summary>
public sealed partial class QueueEditorRow : ObservableObject
{
    /// <summary>What the picker shows. Only the picker and Save write it; nothing here sets another property from it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MissingText), nameof(HasMissing))]
    private SkuaScriptInfo? _script;

    [ObservableProperty]
    private string _goalItem;

    [ObservableProperty]
    private string _quantityText;

    [ObservableProperty]
    private string _minutesText;

    [ObservableProperty]
    private string _faction;

    [ObservableProperty]
    private string _rankText;

    public QueueEditorRow(string? id, IReadOnlyList<string> suggestions, SkuaScriptInfo? script, string goalItem, string quantityText, string minutesText, string faction, string rankText, string? storedScriptId = null, string? storedScriptName = null)
    {
        Id = id;
        StoredScriptId = storedScriptId;
        StoredScriptName = storedScriptName;
        Suggestions = suggestions;
        _script = script;
        _goalItem = goalItem;
        _quantityText = quantityText;
        _minutesText = minutesText;
        _faction = faction;
        _rankText = rankText;
    }

    /// <summary>The stored step's id, null for a row added here.</summary>
    public string? Id { get; }

    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>The script id and name of the stored step; kept while the picker shows nothing (the script is not listed).</summary>
    public string? StoredScriptId { get; }

    public string? StoredScriptName { get; }

    public bool HasMissing => Script is null && StoredScriptId is not null;

    public string MissingText => HasMissing ? QueueEditorViewModel.MissingScriptText + " " + StoredScriptName : string.Empty;

    /// <summary>The list the script picker offers (set by the editor once Skua answered).</summary>
    [ObservableProperty]
    private IReadOnlyList<SkuaScriptInfo> _scripts = [];
}

/// <summary>The PC editor of one character's queue. Save checks the rows, then replaces the queue; nothing is sent by itself.</summary>
public sealed partial class QueueEditorViewModel : ObservableObject
{
    public const string TitleText = "Script queue";
    public const string AddText = "Add step";
    public const string RemoveText = "Remove";
    public const string MoveUpText = "Up";
    public const string MoveDownText = "Down";
    public const string SaveText = "Save";
    public const string CancelText = "Cancel";
    public const string ScriptLabel = "Script";
    public const string ItemLabel = "Item to obtain";
    public const string QuantityLabel = "How many";
    public const string MinutesLabel = "Time limit (minutes, optional)";
    public const string FactionLabel = "Faction (optional)";
    public const string RankLabel = "Rank (1 to 10)";
    public const string LoadingScriptsText = "Asking Skua for its scripts...";
    public const string NoScriptsText = "Skua did not list its scripts. Open Skua with this character logged in, then reopen the editor.";
    public const string NoScriptChosenText = "Choose a script for every step.";
    public const string BadQuantityText = "The quantity must be a whole number from 1 to 1000000.";
    public const string BadMinutesText = "The time limit must be a whole number from 1 to 1440, or empty.";
    public const string MissingScriptText = "Skua does not list this script now:";
    public const string AddAutomation = "Add a step";
    public const string SaveAutomation = "Save the queue";
    public const string CancelAutomation = "Close without saving";
    public const string ScriptAutomation = "Script of this step";
    public const string ItemAutomation = "Item this step must obtain";
    public const string QuantityAutomation = "Quantity";
    public const string MinutesAutomation = "Time limit in minutes";
    public const string FactionAutomation = "Faction";
    public const string RankAutomation = "Reputation rank";
    public const string MoveUpAutomation = "Move this step up";
    public const string MoveDownAutomation = "Move this step down";
    public const string RemoveAutomation = "Remove this step";
    public const string BadRankText = "The rank must be a whole number from 1 to 10.";

    private readonly long _revision;
    private readonly ISkuaQueues _queues;
    private readonly IQueueScriptSource _source;
    private readonly IReadOnlyList<string> _suggestions;

    [ObservableProperty]
    private IReadOnlyList<SkuaScriptInfo> _scripts = [];

    [ObservableProperty]
    private bool _scriptsLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    [ObservableProperty]
    private bool _isLoading = true;

    public QueueEditorViewModel(string character, IReadOnlyList<QueueStep> steps, ISkuaQueues queues, IQueueScriptSource scripts, IReadOnlyList<string> itemSuggestions, long revision = 0)
    {
        _revision = revision;
        Character = character;
        _queues = queues;
        _source = scripts;
        _suggestions = itemSuggestions;
        foreach (var step in steps.Take(QueueLimits.MaxSteps))
        {
            Rows.Add(new QueueEditorRow(
                step.Id, _suggestions, null, step.GoalItem,
                step.GoalQuantity.ToString(CultureInfo.InvariantCulture), step.MaxMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                step.Reputation?.Faction ?? string.Empty, step.Reputation?.Rank.ToString(CultureInfo.InvariantCulture) ?? string.Empty, step.ScriptId, step.ScriptName));
        }
    }

    public string Character { get; }

    public string HeaderText => TitleText + ": " + Character;

    public ObservableCollection<QueueEditorRow> Rows { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public event EventHandler? Closed;

    /// <summary>Asks the source for the script list and matches each stored row by id. Never throws.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IReadOnlyList<SkuaScriptInfo>? list = null;
        try
        {
            list = await _source.ListAsync(Character, ct).ConfigureAwait(true);
        }
        catch (Exception)
        {
            list = null;
        }

        // What the owner picked so far (a reload must not replace it with the stored script); taken before the lists change,
        // because the picker may clear its selection when its list does.
        var picks = Rows.Select(row => row.Script?.Id).ToList();
        Scripts = list ?? [];
        ScriptsLoaded = list is not null;
        // The list first (the picker may clear its selection when the list changes), then the selections, one after the other.
        foreach (var row in Rows)
        {
            row.Scripts = Scripts;
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var match = (i < picks.Count && picks[i] is { } picked ? Scripts.FirstOrDefault(s => s.Id == picked) : null)
                ?? (row.StoredScriptId is { } id ? Scripts.FirstOrDefault(s => s.Id == id) : null);
            if (match is not null)
            {
                row.Script = match;
            }
        }

        IsLoading = false;
        if (list is null || list.Count == 0)
        {
            ErrorText = NoScriptsText;
        }
    }

    [RelayCommand]
    private void Add()
    {
        if (Rows.Count >= QueueLimits.MaxSteps)
        {
            return;
        }

        Rows.Add(new QueueEditorRow(null, _suggestions, null, string.Empty, "1", string.Empty, string.Empty, string.Empty) { Scripts = Scripts });
    }

    [RelayCommand]
    private void Remove(QueueEditorRow? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    [RelayCommand]
    private void MoveUp(QueueEditorRow? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(QueueEditorRow? row) => Move(row, 1);

    private void Move(QueueEditorRow? row, int by)
    {
        var index = row is null ? -1 : Rows.IndexOf(row);
        var target = index + by;
        if (index >= 0 && target >= 0 && target < Rows.Count)
        {
            Rows.Move(index, target);
        }
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Save()
    {
        var inputs = new List<QueueStepInput>();
        foreach (var row in Rows)
        {
            var scriptId = row.Script?.Id ?? row.StoredScriptId;
            if (scriptId is null)
            {
                Fail(NoScriptChosenText);
                return;
            }

            if (!TryWhole(row.QuantityText, out var quantity) || quantity is < QueueLimits.MinQuantity or > QueueLimits.MaxQuantity)
            {
                Fail(BadQuantityText);
                return;
            }

            int? minutes = null;
            if (row.MinutesText.Trim().Length > 0)
            {
                if (!TryWhole(row.MinutesText, out var m) || m is < QueueLimits.MinMinutes or > QueueLimits.MaxMinutes)
                {
                    Fail(BadMinutesText);
                    return;
                }

                minutes = m;
            }

            ReputationGoal? reputation = null;
            if (row.Faction.Trim().Length > 0 || row.RankText.Trim().Length > 0)
            {
                var rankTyped = row.RankText.Trim().Length > 0;
                var rankOk = TryWhole(row.RankText, out var rank);
                if (rankTyped && !rankOk)
                {
                    Fail(BadRankText);
                    return;
                }

                reputation = new ReputationGoal(row.Faction, rankOk ? rank : 0);
            }

            inputs.Add(new QueueStepInput(row.Id, scriptId, row.GoalItem, quantity, reputation, minutes));
        }

        if (inputs.Count > 0 && !ScriptsLoaded)
        {
            Fail(NoScriptsText);
            return;
        }

        var checkedSteps = QueueValidator.Validate(inputs, Scripts, () => Guid.NewGuid().ToString("N"), allowEmpty: true);
        if (!checkedSteps.Ok)
        {
            Fail(checkedSteps.Error!);
            return;
        }

        var result = _queues.Replace(Character, checkedSteps.Steps, _revision);
        if (!result.Ok)
        {
            Fail(result.Error ?? QueueValidator.BadCount);
            return;
        }

        ErrorText = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string text) => ErrorText = text;

    private static bool TryWhole(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
