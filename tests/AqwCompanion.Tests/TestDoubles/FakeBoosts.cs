using AqwCompanion.Phone.Boosts;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeBoosts : ISkuaBoosts
{
    private EventHandler? _changed;

    public List<BoostView> Items { get; } = [];

    public List<(string InstanceId, int ItemId)> Used { get; } = [];

    public List<string> Relogged { get; } = [];

    public BoostResult UseResult { get; set; } = BoostResult.Done;

    public BoostResult ReloginResult { get; set; } = BoostResult.Done;

    public bool ThrowOnViews { get; set; }

    public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

    public IReadOnlyList<BoostView> Views => ThrowOnViews ? throw new InvalidOperationException("views") : [.. Items];

    public event EventHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public BoostView? Find(string instanceId) => Items.FirstOrDefault(v => v.InstanceId == instanceId);

    public Task<BoostResult> UseAsync(string instanceId, int itemId, CancellationToken ct = default)
    {
        Used.Add((instanceId, itemId));
        return Task.FromResult(UseResult);
    }

    public Task<BoostResult> ReloginAsync(string instanceId, CancellationToken ct = default)
    {
        Relogged.Add(instanceId);
        return Task.FromResult(ReloginResult);
    }

    public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
}
