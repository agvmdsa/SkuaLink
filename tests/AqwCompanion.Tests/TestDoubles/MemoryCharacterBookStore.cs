using AqwCompanion.Phone.Characters;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class MemoryCharacterBookStore : ICharacterBookStore
{
    public List<CharacterRecord> Stored { get; set; } = [];
    public int Saves { get; private set; }
    public int Deletes { get; private set; }
    public bool FailSave { get; set; }

    /// <summary>Runs inside <see cref="Load"/>, before the stored records are returned (something happening during a load).</summary>
    public Action? OnLoad { get; set; }

    public IReadOnlyList<CharacterRecord> Load()
    {
        var stored = Stored;
        OnLoad?.Invoke();
        return [.. stored];
    }

    public bool Save(IReadOnlyList<CharacterRecord> records)
    {
        Saves++;
        if (FailSave)
        {
            return false;
        }

        Stored = [.. records];
        return true;
    }

    public void Delete()
    {
        Deletes++;
        Stored = [];
    }
}
