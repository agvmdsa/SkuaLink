using System.Globalization;
using System.Text;
using AqwCompanion.App.Characters;
using AqwCompanion.Phone.Boosts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Boosts;

/// <summary>
/// The dashboard card of the selected character's boosts: the four types with the time left, the boosts in the bag with Use (it
/// asks first, inline) and Log in again. It shows what <see cref="ISkuaBoosts"/> reports and sends commands; the service and the
/// phone API decide. Events arrive on other threads, so the refresh runs through <c>onUiThread</c> (one queued at a time); the
/// 1 s ticker (<c>every</c>) exists only while a row counts down and is created in <c>Refresh</c>, never in the constructor.
/// Commands are single flight. It unsubscribes and stops the ticker on <see cref="Dispose"/> and never throws into the UI.
/// </summary>
public sealed partial class BoostsViewModel : ObservableObject, IDisposable
{
    public const string TitleText = "Boosts";
    public const string UpdateText = "Update the plugin DLL to see boosts.";
    public const string UseText = "Use";
    public const string ReloginText = "Log in again";
    public const string ConfirmText = "Confirm";
    public const string CancelText = "Cancel";
    public const string NoItemsText = "No boosts in the bag.";
    public const string PausedSuffix = " (paused)";
    public const string ItemsAutomation = "Boosts in the bag";
    public const string RowsAutomation = "Boost times";
    public const string InactiveFormat = "{0}: inactive";
    public const string ActiveFormat = "{0}: active";
    public const string LeftFormat = "{0}: {1} min left";
    public const string ItemFormat = "{0} x{1}";
    public const string ConfirmFormat = "Use {0}? You have {1}.";

    private sealed record Pending(string InstanceId, int ItemId, string Name, int Quantity);

    private readonly ISkuaBoosts _boosts;
    private readonly CharacterSelection _selection;
    private readonly Action<Action> _onUiThread;
    private readonly Func<Action, IDisposable>? _every;
    private readonly RelayCommand _relogin;
    private readonly RelayCommand _confirm;
    private readonly RelayCommand _cancel;
    private IDisposable? _ticker;
    private Pending? _pending;
    private bool _busy;
    private Task _last = Task.CompletedTask;
    private string _owner = string.Empty;
    private string _signature = string.Empty;
    private int _refreshQueued;
    private bool _disposed;

    public BoostsViewModel(
        ISkuaBoosts boosts, CharacterSelection selection, Action<Action>? onUiThread = null, Func<Action, IDisposable>? every = null)
    {
        _boosts = boosts;
        _selection = selection;
        _onUiThread = onUiThread ?? (action => action());
        _every = every;
        _relogin = new RelayCommand(Relogin, () => CanRelogin && !_busy);
        _confirm = new RelayCommand(Confirm, () => Confirming && !_busy);
        _cancel = new RelayCommand(Cancel);
        boosts.Changed += OnChanged;
        selection.Changed += OnChanged;

        // The first refresh goes through the UI thread too: it may create the ticker, which must be made there.
        OnChanged(this, EventArgs.Empty);
    }

    public bool HasView { get; private set; }

    public bool Supported { get; private set; }

    public bool ShowBody => HasView && Supported;

    public bool ShowUpdate => HasView && !Supported;

    public IReadOnlyList<BoostRow> Rows { get; private set; } = [];

    public IReadOnlyList<BoostItemRow> Items { get; private set; } = [];

    public bool HasItems => Items.Count > 0;

    public bool ShowNoItems => ShowBody && Items.Count == 0;

    public bool CanRelogin { get; private set; }

    public IRelayCommand ReloginCommand => _relogin;

    public bool Confirming => _pending is not null;

    public string ConfirmQuestion { get; private set; } = string.Empty;

    public IRelayCommand ConfirmCommand => _confirm;

    public IRelayCommand CancelCommand => _cancel;

    public string StatusText { get; private set; } = string.Empty;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>Completes when no command is in flight (for tests).</summary>
    public Task Idle => _last;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _boosts.Changed -= OnChanged;
        _selection.Changed -= OnChanged;
        StopTicker();
    }

    private void OpenConfirmation(string instanceId, BoostItemView item)
    {
        if (_busy || _disposed)
        {
            return;
        }

        _pending = new Pending(instanceId, item.ItemId, item.Name, item.Quantity);
        Refresh();
    }

    private void Cancel()
    {
        if (_pending is null || _disposed)
        {
            return;
        }

        _pending = null;
        Refresh();
    }

    private void Confirm()
    {
        if (_busy || _disposed || _pending is not { } pending)
        {
            return;
        }

        // The view is checked again: the item may be gone or its type active since the question was opened.
        var current = CurrentView();
        var refusal = BoostGuards.CheckUse(current, pending.ItemId)
            ?? (current?.InstanceId == pending.InstanceId ? null : BoostTexts.UnknownSkua);
        if (refusal is not null)
        {
            _pending = null;
            _owner = OwnerOf(current);
            StatusText = refusal;
            Refresh();
            return;
        }

        _pending = null;
        _busy = true;
        StatusText = string.Empty;
        var owner = _owner;
        Refresh();
        _last = Run(owner, ct => _boosts.UseAsync(pending.InstanceId, pending.ItemId, ct), clearOnSuccess: true);
    }

    private void Relogin()
    {
        if (_busy || _disposed || !CanRelogin || CurrentView() is not { } view)
        {
            return;
        }

        _busy = true;
        StatusText = string.Empty;
        var owner = _owner;
        Refresh();
        _last = Run(owner, ct => _boosts.ReloginAsync(view.InstanceId, ct), clearOnSuccess: false);
    }

    private async Task Run(string owner, Func<CancellationToken, Task<BoostResult>> send, bool clearOnSuccess)
    {
        BoostResult result;
        try
        {
            result = await send(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            result = BoostResult.Refused(BoostTexts.Refused);
        }

        try
        {
            _onUiThread(() =>
            {
                _busy = false;
                if (_disposed)
                {
                    return;
                }

                if (string.Equals(_owner, owner, StringComparison.Ordinal))
                {
                    StatusText = result.Ok ? (clearOnSuccess ? string.Empty : BoostTexts.LoginStarted) : result.Error ?? BoostTexts.Refused;
                }

                Refresh();
            });
        }
        catch (Exception)
        {
            _busy = false;
        }
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

    private void OnTick()
    {
        if (!_disposed)
        {
            Refresh();
        }
    }

    private BoostView? CurrentView()
    {
        try
        {
            var character = _selection.Selected;
            return character is null
                ? null
                : _boosts.Views.FirstOrDefault(v => v is not null && string.Equals(v.Character, character, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string OwnerOf(BoostView? view) => view is null ? string.Empty : view.InstanceId + "|" + view.Character;

    private void Refresh()
    {
        var view = CurrentView();
        var owner = OwnerOf(view);
        if (!string.Equals(owner, _owner, StringComparison.Ordinal))
        {
            _owner = owner;
            StatusText = string.Empty;
        }

        if (_pending is { } pending && (view is null || view.InstanceId != pending.InstanceId || BoostGuards.CheckUse(view, pending.ItemId) is not null))
        {
            _pending = null;
        }

        var supported = view?.Supported ?? false;
        var body = view is not null && supported;
        var rows = body ? RowsOf(view!) : [];
        var items = body ? view!.Items.Where(i => i is not null).ToList() : [];
        var canRelogin = body && BoostGuards.CheckRelogin(view) is null;
        var question = _pending is { } p
            ? string.Format(CultureInfo.InvariantCulture, ConfirmFormat, p.Name, p.Quantity)
            : string.Empty;

        var counting = body && view!.Online && BoostTracker.Types.Any(t => view.ValueOf(t) > 0);
        if (counting)
        {
            _ticker ??= StartTicker();
        }
        else
        {
            StopTicker();
        }

        var signature = Signature(view, rows, items, canRelogin, question, StatusText, _busy);
        if (string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            return;
        }

        _signature = signature;
        HasView = view is not null;
        Supported = supported;
        Rows = rows;
        CanRelogin = canRelogin;
        ConfirmQuestion = question;
        var instanceId = view?.InstanceId ?? string.Empty;
        Items = [.. items.Select(item => ItemRow(instanceId, item))];
        OnPropertyChanged(string.Empty);
        _relogin.NotifyCanExecuteChanged();
        _confirm.NotifyCanExecuteChanged();
        foreach (var row in Items)
        {
            row.UseCommand.NotifyCanExecuteChanged();
        }
    }

    private BoostItemRow ItemRow(string instanceId, BoostItemView item)
    {
        var command = new RelayCommand(() => OpenConfirmation(instanceId, item), () => item.CanUse && !_busy);
        var text = string.Format(CultureInfo.InvariantCulture, ItemFormat, item.Name, item.Quantity);
        return new BoostItemRow(item.ItemId, text, item.CanUse, command);
    }

    private static List<BoostRow> RowsOf(BoostView view) =>
        [.. BoostTracker.Types.Select(type => new BoostRow(RowText(view, type)))];

    private static string RowText(BoostView view, string type)
    {
        var heading = BoostTypes.Heading(type);
        var value = view.ValueOf(type);
        if (value == 0)
        {
            return string.Format(CultureInfo.InvariantCulture, InactiveFormat, heading);
        }

        var text = value < 0
            ? string.Format(CultureInfo.InvariantCulture, ActiveFormat, heading)
            : string.Format(CultureInfo.InvariantCulture, LeftFormat, heading, (value + 59L) / 60);
        return view.Online ? text : text + PausedSuffix;
    }

    private static string Signature(BoostView? view, List<BoostRow> rows, List<BoostItemView> items, bool canRelogin, string question, string status, bool busy)
    {
        var sb = new StringBuilder();
        sb.Append(view is null ? "-" : view.InstanceId).Append('\u001f').Append(view?.Character).Append('\u001f')
            .Append(view?.Supported).Append('\u001f').Append(canRelogin);
        foreach (var row in rows)
        {
            sb.Append('\u001f').Append(row.Text);
        }

        foreach (var item in items)
        {
            sb.Append('\u001e').Append(item.ItemId).Append('\u001f').Append(item.Name).Append('\u001f').Append(item.Quantity)
                .Append('\u001f').Append(item.CanUse);
        }

        return sb.Append('\u001d').Append(question).Append('\u001d').Append(status).Append('\u001d').Append(busy).ToString();
    }

    private IDisposable? StartTicker()
    {
        try
        {
            return _every?.Invoke(OnTick);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void StopTicker()
    {
        var ticker = _ticker;
        _ticker = null;
        try
        {
            ticker?.Dispose();
        }
        catch (Exception)
        {
            // A timer that cannot be stopped has nothing more to tell the UI.
        }
    }
}
