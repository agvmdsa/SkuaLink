using AqwCompanion.App.Queue;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>An in-memory <see cref="IQueueStore"/> that records everything done to it.</summary>
public sealed class MemoryQueueStore : IQueueStore
{
    private readonly object _lock = new();

    public List<StoredQueue> Saved { get; } = [];

    public int Loads { get; private set; }

    public int Deletes { get; private set; }

    /// <summary>What <see cref="LoadAll"/> returns.</summary>
    public List<StoredQueue> Stored { get; set; } = [];

    /// <summary>Makes <see cref="Save"/> throw (a store that breaks its promise).</summary>
    public bool FailSave { get; set; }

    public bool FailLoad { get; set; }

    public IReadOnlyList<StoredQueue> LoadAll()
    {
        lock (_lock)
        {
            Loads++;
            if (FailLoad)
            {
                throw new InvalidOperationException("load failed");
            }

            return [.. Stored];
        }
    }

    public void Save(StoredQueue queue)
    {
        lock (_lock)
        {
            if (FailSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Saved.Add(queue);
        }
    }

    public void DeleteAll()
    {
        lock (_lock)
        {
            Deletes++;
            Saved.Clear();
            Stored = [];
        }
    }
}
