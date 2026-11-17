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
            var endpoints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sub in store.All)
            {
                // One push per endpoint: the same browser subscription stored under two device ids is still one phone.
                if (sub.Kinds.Contains(kind, StringComparer.Ordinal) && endpoints.Add(sub.Endpoint))
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
