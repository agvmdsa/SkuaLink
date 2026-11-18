using AqwCompanion.Phone.Queue;

namespace AqwCompanion.App.Queue;

/// <summary>One queue as stored: the steps, the cursor and whether it had finished. Whatever the state was, a loaded queue is stopped.</summary>
public sealed record StoredQueue(string Character, IReadOnlyList<QueueStep> Steps, int Cursor, bool Finished);

/// <summary>Where the queues are kept between runs. Never throws.</summary>
public interface IQueueStore
{
    IReadOnlyList<StoredQueue> LoadAll();

    void Save(StoredQueue queue);

    /// <summary>Removes every stored queue (and nothing else).</summary>
    void DeleteAll();
}
