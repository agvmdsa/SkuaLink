namespace AqwCompanion.Phone.Push;

/// <summary>Where the PC's VAPID key comes from. Never throws.</summary>
public interface IVapidKeySource
{
    /// <summary>The key, made on first use. Only the key route of the API may call this.</summary>
    VapidKey GetOrCreate();

    /// <summary>The key that already exists, or null. Never creates one: the send path uses this, so a send in flight cannot bring the key back after Forget.</summary>
    VapidKey? GetExisting();
}
