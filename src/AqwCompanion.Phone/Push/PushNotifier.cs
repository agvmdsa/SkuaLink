using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Phone.Push;

/// <summary>Turns an admitted notice into one push job per subscription whose switch for its kind is on. Never throws.</summary>
public sealed class PushNotifier(PushSubscriptionStore store, PushDispatcher dispatcher) : INoticeSink
{
    public void Publish(Notice notice)
    {
        try
        {
            if (notice.Kind is NoticeKind.NewLogin or NoticeKind.SkuaReconnected)
            {
                return;
            }

            var kind = notice.Kind.ToString();
            if (!PushKinds.IsKnown(kind))
            {
                return;
            }

            PushMessage? message = null;
            foreach (var sub in store.All)
            {
                if (sub.Kinds.Contains(kind, StringComparer.Ordinal))
                {
                    message ??= PushPayload.Build(notice);
                    dispatcher.Enqueue(new PushJob(sub.DeviceId, message));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A push failure never reaches the hub.
        }
    }
}
