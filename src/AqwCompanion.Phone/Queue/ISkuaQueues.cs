namespace AqwCompanion.Phone.Queue;

/// <summary>
/// What the phone API and the phone hub see of the script queues. Every method is safe from any thread; <see cref="Changed"/>
/// is raised outside the service's locks.
/// </summary>
public interface ISkuaQueues
{
    IReadOnlyList<QueueView> Views { get; }

    QueueView? Find(string character);

    /// <summary>
    /// Replaces the step list. <paramref name="revision"/> is the <see cref="QueueView.Revision"/> the caller saw: when the queue has
    /// another one the call is refused (<see cref="QueueRevision.ChangedText"/>) and nothing changes.
    /// </summary>
    QueueResult Replace(string character, IReadOnlyList<QueueStep> steps, long revision);

    QueueResult Command(string character, QueueCommand command);

    event EventHandler? Changed;
}
