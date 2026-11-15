using AqwCompanion.Phone.Characters;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class MemoryCharacterBookStore : ICharacterBookStore
{
    public List<CharacterRecord> Stored { get; set; } = [];
    public int Saves { get; private set; }
    public int Deletes { get; private set; }
    public bool FailSave { get; set; }

    public IReadOnlyList<CharacterRecord> Load() => [.. Stored];

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
