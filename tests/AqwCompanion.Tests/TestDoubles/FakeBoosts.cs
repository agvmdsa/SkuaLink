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

    /// <summary>When set, <see cref="UseAsync"/> waits for it (the call is recorded first).</summary>
    public Task? UseHold { get; set; }

    /// <summary>When set, <see cref="ReloginAsync"/> waits for it (the call is recorded first).</summary>
    public Task? ReloginHold { get; set; }

    public bool ThrowOnUse { get; set; }

    public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

    public IReadOnlyList<BoostView> Views => ThrowOnViews ? throw new InvalidOperationException("views") : [.. Items];

    public event EventHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public BoostView? Find(string instanceId) => Items.FirstOrDefault(v => v.InstanceId == instanceId);

    public async Task<BoostResult> UseAsync(string instanceId, int itemId, CancellationToken ct = default)
    {
        Used.Add((instanceId, itemId));
        if (UseHold is { } hold)
        {
            await hold;
        }

        return ThrowOnUse ? throw new InvalidOperationException("use") : UseResult;
    }

    public async Task<BoostResult> ReloginAsync(string instanceId, CancellationToken ct = default)
    {
        Relogged.Add(instanceId);
        if (ReloginHold is { } hold)
        {
            await hold;
        }

        return ReloginResult;
    }

    public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
}
