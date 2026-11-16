using System.Globalization;
using AqwCompanion.App.Feed;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AqwCompanion.App.Characters;

/// <summary>
/// The desktop window's view of the characters Skua reported: the character chip, the selected character's numbers, class and
/// reputation, and the footer. It follows <see cref="CharacterSelection"/>, and choosing a character in the chip chooses it there.
/// The book, the Skua hub and the selection raise their change events from other threads, so the app passes <c>onUiThread</c> to run
/// the update on the UI thread (without it, inline); a burst of changes queues one update at a time. It never logs.
/// </summary>
public sealed partial class CharactersViewModel : ObservableObject
{
    public const string NoCharacterText = "No character yet";
    public const string EmptyText = "Open Skua with the SkuaLink plugin and log in; your characters appear here.";
    public const string LiveText = "Live from Skua.";
    public const string NoProfileText = "No numbers from Skua yet. Update the Skua plugin to the same release as SkuaLink.";

    private readonly CharacterBook _book;
    private readonly ISkuaFeed _skua;
    private readonly CharacterSelection _selection;
    private readonly Action<Action> _onUiThread;
    private int _refreshQueued;
    private string? _selectedCharacter;
    private SkuaProfile? _shownProfile;
    private IReadOnlyList<SkuaFaction>? _shownFactions;

    public CharactersViewModel(CharacterBook book, ISkuaFeed skua, CharacterSelection selection, Action<Action>? onUiThread = null)
    {
        _book = book;
        _skua = skua;
        _selection = selection;
        _onUiThread = onUiThread ?? (action => action());
        Refresh();
        book.Changed += (_, _) => QueueRefresh();
        skua.Changed += (_, _) => QueueRefresh();
        selection.Changed += (_, _) => QueueRefresh();
    }

    /// <summary>Every character the book knows, sorted by name.</summary>
    public IReadOnlyList<string> CharacterNames { get; private set; } = [];

    public bool HasCharacters => CharacterNames.Count > 0;

    /// <summary>The selected character's name, or null; setting a name chooses it in the <see cref="CharacterSelection"/>.</summary>
    public string? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            // The list box pushes null while its items are replaced: that is not a choice, so it never resets the selection.
            if (value is null || string.Equals(value, _selectedCharacter, StringComparison.Ordinal))
            {
                return;
            }

            _selection.Choose(value);
            QueueRefresh();
        }
    }

    /// <summary>The selected character's name for the top bar chip, or a waiting text.</summary>
    public string CharacterChipText => _selectedCharacter ?? NoCharacterText;

    /// <summary>The first letter of the selected character's name for the chip's badge, or "?".</summary>
    public string SelectedCharacterInitial => _selectedCharacter is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";

    /// <summary>Name, level and class of the selected character; a hint when Skua sent no numbers; empty without a character.</summary>
    public string ProfileHeadline { get; private set; } = string.Empty;

    /// <summary>The numbers shown across the top of the dashboard, each only when Skua reported it.</summary>
    public IReadOnlyList<ProfileFigure> ProfileFigures { get; private set; } = [];

    /// <summary>The selected character's factions, closest to the next rank first.</summary>
    public IReadOnlyList<GameFactionRow> Factions { get; private set; } = [];

    /// <summary>Says whether the selected character is live in a Skua or when it was last seen; empty without a character.</summary>
    public string UpdatedText { get; private set; } = string.Empty;

    public bool HasUpdatedText => UpdatedText.Length > 0;

    /// <summary>How many Skua with the SkuaLink plugin are connected, in words.</summary>
    public string FooterText { get; private set; } = string.Empty;

    /// <summary>Whether the selected character is logged in in a connected Skua now.</summary>
    public bool IsLive { get; private set; }

    // A burst of changes queues a single refresh: while one is waiting for the UI thread, the others add nothing, because the
    // refresh reads the newest state when it runs.
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        try
        {
            _onUiThread(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                Refresh();
            });
        }
        catch (Exception)
        {
            Volatile.Write(ref _refreshQueued, 0);
            throw;
        }
    }

    private void Refresh()
    {
        var view = _skua.Current;
        // The same list stays while the names are the same, so the chip's list box keeps its items and its selection.
        var names = _selection.Names;
        if (!names.SequenceEqual(CharacterNames, StringComparer.Ordinal))
        {
            CharacterNames = names;
        }

        _selectedCharacter = _selection.Selected;
        var record = _book.Find(_selectedCharacter);

        IsLive = record is not null && FeedSnapshotBuilder.LiveCharacters(view).Contains(record.Name);
        ProfileHeadline = record is null ? string.Empty : HeadlineFor(record);
        UpdatedText = record is null
            ? string.Empty
            : IsLive
                ? LiveText
                : $"Last seen {LocalTimeText.Format(record.LastSeenUtc)}. Open Skua and log in to refresh it.";
        FooterText = FeedSnapshotBuilder.SkuaStateText(FeedSnapshotBuilder.ConnectedCount(view));

        // The lists are rebuilt only when their source changed, so a refresh that changes nothing in them keeps their scroll position.
        var profile = record?.Profile;
        if (profile != _shownProfile)
        {
            _shownProfile = profile;
            ProfileFigures = profile is null ? [] : FiguresFor(profile);
        }

        var factions = record?.Factions;
        if (!ReferenceEquals(factions, _shownFactions))
        {
            _shownFactions = factions;
            Factions = factions is null
                ? []
                : [.. factions
                    .Select(CharacterFactions.ToStanding)
                    .OrderBy(FactionProgress.Missing)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(FactionRow)];
        }

        OnPropertyChanged(nameof(CharacterNames));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(SelectedCharacter));
        OnPropertyChanged(nameof(CharacterChipText));
        OnPropertyChanged(nameof(SelectedCharacterInitial));
        OnPropertyChanged(nameof(ProfileHeadline));
        OnPropertyChanged(nameof(ProfileFigures));
        OnPropertyChanged(nameof(Factions));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(HasUpdatedText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(IsLive));
    }

    private static string HeadlineFor(CharacterRecord record)
    {
        if (record.Profile is not { } p)
        {
            return NoProfileText;
        }

        var headline = string.Create(CultureInfo.InvariantCulture, $"{record.Name} · Level {p.Level}");
        return p.ClassName is { Length: > 0 } className
            ? headline + string.Create(CultureInfo.InvariantCulture, $" · {className} rank {p.ClassRank}")
            : headline;
    }

    private static List<ProfileFigure> FiguresFor(SkuaProfile p)
    {
        var figures = new List<ProfileFigure>
        {
            new("Gold", Format(p.Gold)),
            new("Exp", Format(p.Xp) + " / " + Format(p.XpToLevel)),
            new("AC", Format(p.Coins)),
        };
        if (p.ClassPoints is { } points)
        {
            figures.Add(new("Class points", Format(points)));
        }

        figures.Add(new("Membership", p.IsMember ? "Member" : "Not a member"));
        if (p.Guild is { Length: > 0 } guild)
        {
            figures.Add(new("Guild", guild));
        }

        var slots = new List<string>();
        if (p.BagSlots is { } bag)
        {
            slots.Add(Format(bag) + " bag");
        }

        if (p.BankSlots is { } bank)
        {
            slots.Add(Format(bank) + " bank");
        }

        if (p.HouseSlots is { } house)
        {
            slots.Add(Format(house) + " house");
        }

        if (slots.Count > 0)
        {
            figures.Add(new("Slots", string.Join(", ", slots)));
        }

        return figures;
    }

    private static GameFactionRow FactionRow(FactionStanding f)
    {
        var maxed = FactionProgress.IsMax(f);
        return new GameFactionRow(
            f.Name,
            "Rank " + Format(f.Rank),
            Format(f.Rep),
            maxed ? "Max rank" : Format(f.SpillRep) + " / " + Format(f.RepToRank),
            maxed ? Format(0) : Format(Math.Max(0, f.RepToRank - f.SpillRep)),
            FactionProgress.Fraction(f),
            maxed);
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
