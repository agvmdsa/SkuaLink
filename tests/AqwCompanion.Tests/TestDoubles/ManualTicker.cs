namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A hand-driven stand-in for the 1 s ticker: keeps the action, <see cref="Tick"/> runs it, disposing clears it.</summary>
public sealed class ManualTicker
{
    private Action? _action;

    public int Created { get; private set; }

    public bool Active => _action is not null;

    public Func<Action, IDisposable> Every => action =>
    {
        Created++;
        _action = action;
        return new Handle(this, action);
    };

    public void Tick() => _action?.Invoke();

    private sealed class Handle(ManualTicker owner, Action action) : IDisposable
    {
        public void Dispose()
        {
            if (owner._action == action)
            {
                owner._action = null;
            }
        }
    }
}
