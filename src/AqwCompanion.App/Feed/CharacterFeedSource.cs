using System.ComponentModel;
using AqwCompanion.App.Characters;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.App.Feed;

/// <summary>
/// The feed's source over the characters Skua reported. The snapshot is rebuilt when something it shows may have changed (the
/// character book, the Skua connections, the selected character, the Hard Farms summary) and cached until then.
/// <see cref="Changed"/> may be raised from any thread.
/// </summary>
public sealed class CharacterFeedSource : IFeedSource, IDisposable
{
    private readonly CharacterBook _book;
    private readonly ISkuaFeed _skua;
    private readonly CharacterSelection _selection;
    private readonly HardFarmsViewModel _hardFarms;
    private readonly object _lock = new();
    private FeedSnapshot? _cached;

    public CharacterFeedSource(CharacterBook book, ISkuaFeed skua, CharacterSelection selection, HardFarmsViewModel hardFarms)
    {
        _book = book;
        _skua = skua;
        _selection = selection;
        _hardFarms = hardFarms;
        book.Changed += OnChanged;
        skua.Changed += OnChanged;
        selection.Changed += OnChanged;
        hardFarms.PropertyChanged += OnHardFarmsChanged;
    }

    public event EventHandler? Changed;

    public FeedSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return _cached ??= FeedSnapshotBuilder.Build(_book.All, _skua.Current, _selection.Selected, _hardFarms.Summary);
            }
        }
    }

    public void Dispose()
    {
        _book.Changed -= OnChanged;
        _skua.Changed -= OnChanged;
        _selection.Changed -= OnChanged;
        _hardFarms.PropertyChanged -= OnHardFarmsChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Invalidate();

    private void OnHardFarmsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HardFarmsViewModel.Summary))
        {
            Invalidate();
        }
    }

    private void Invalidate()
    {
        lock (_lock)
        {
            _cached = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
