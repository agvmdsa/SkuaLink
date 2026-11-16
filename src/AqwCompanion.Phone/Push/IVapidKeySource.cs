namespace AqwCompanion.Phone.Push;

/// <summary>Where the sender gets the PC's VAPID key (made on first use). Never throws.</summary>
public interface IVapidKeySource
{
    VapidKey GetOrCreate();
}
