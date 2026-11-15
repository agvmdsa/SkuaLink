namespace AqwCompanion.Phone.Characters;

/// <summary>Where the <see cref="CharacterBook"/> keeps its records between runs. Never throws.</summary>
public interface ICharacterBookStore
{
    /// <summary>The stored records; empty when nothing is stored or on any failure.</summary>
    IReadOnlyList<CharacterRecord> Load();

    /// <summary>Replaces the stored records; false on failure.</summary>
    bool Save(IReadOnlyList<CharacterRecord> records);

    void Delete();
}
