namespace AqwCompanion.Phone.Queue;

/// <summary>
/// What the phone API and the phone hub see of the script queues. Every method is safe from any thread; <see cref="Changed"/>
/// is raised outside the service's locks.
/// </summary>
public interface ISkuaQueues
{
    IReadOnlyList<QueueView> Views { get; }

    QueueView? Find(string character);

    QueueResult Replace(string character, IReadOnlyList<QueueStep> steps);

    QueueResult Command(string character, QueueCommand command);

    event EventHandler? Changed;
}
