using AqwCompanion.Phone.Queue;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeQueues : ISkuaQueues
{
    public List<QueueView> Items { get; } = [];

    public List<(string Character, IReadOnlyList<QueueStep> Steps)> Replaced { get; } = [];

    public List<(string Character, QueueCommand Command)> Commands { get; } = [];

    public QueueResult ReplaceResult { get; set; } = QueueResult.Done;

    public QueueResult CommandResult { get; set; } = QueueResult.Done;

    public bool ThrowOnViews { get; set; }

    public IReadOnlyList<QueueView> Views => ThrowOnViews ? throw new InvalidOperationException("views") : [.. Items];

    public event EventHandler? Changed;

    public QueueView? Find(string character) => Items.FirstOrDefault(v => string.Equals(v.Character, character, StringComparison.OrdinalIgnoreCase));

    public QueueResult Replace(string character, IReadOnlyList<QueueStep> steps)
    {
        Replaced.Add((character, steps));
        return ReplaceResult;
    }

    public QueueResult Command(string character, QueueCommand command)
    {
        Commands.Add((character, command));
        return CommandResult;
    }

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
