using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class MemoryPushFileStore(PushFile? initial = null) : IPushFileStore
{
    public PushFile Saved { get; set; } = initial ?? PushFile.Empty;
    public int Saves;
    public int Deletes;

    public PushFile Load() => Saved;

    public void Save(PushFile file)
    {
        Saved = file;
        Saves++;
    }

    public void Delete()
    {
        Saved = PushFile.Empty;
        Deletes++;
    }
}
