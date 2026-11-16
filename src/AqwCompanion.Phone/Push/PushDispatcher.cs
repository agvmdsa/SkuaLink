using System.Threading.Channels;

namespace AqwCompanion.Phone.Push;

public sealed record PushJob(string DeviceId, PushMessage Message);

/// <summary>
/// Sends push jobs one at a time on a background worker through a bounded queue that drops the oldest job when full.
/// Enqueue never blocks and never throws; the worker never throws. Endpoints and keys are never logged.
/// </summary>
public sealed class PushDispatcher(PushSender sender, PushSubscriptionStore store, Action<Exception>? onError = null) : IDisposable
{
    public const int Capacity = 64;
    private const int MinTtl = 60;
    private const int MaxTtl = 86400;
    internal const string Refused = "The push service refused the key; subscribe again";
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly Channel<PushJob> _channel = Channel.CreateBounded<PushJob>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private Task? _worker;
    private bool _disposed;

    public bool Enqueue(PushJob job)
    {
        try
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return false;
                }

                var token = _cts.Token;
                _worker ??= Task.Run(() => RunAsync(token));
            }

            return _channel.Writer.TryWrite(job);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return false;
        }
    }

    /// <summary>Completes the queue and waits until the queued jobs were sent. For tests and shutdown.</summary>
    public async Task StopAsync()
    {
        Task? worker;
        lock (_lock)
        {
            worker = _worker;
        }

        _channel.Writer.TryComplete();
        if (worker is not null)
        {
            await worker.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        Task? worker;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            worker = _worker;
        }

        try
        {
            _cts.Cancel();
            _channel.Writer.TryComplete();
            worker?.Wait(DisposeWait);
            if (worker is null || worker.IsCompleted)
            {
                _cts.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Disposal is best effort: the worker is cancelled and nothing waits for it any longer.
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await SendAsync(job, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Report(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
        }
    }

    private async Task SendAsync(PushJob job, CancellationToken token)
    {
        var sub = store.Get(job.DeviceId);
        if (sub is null)
        {
            return;
        }

        // The sender reports a bad payload or TTL as "could not reach the push service": check first.
        var payload = job.Message.Payload;
        if (payload is null || payload.Length is 0 or > PushPayload.MaxBytes)
        {
            store.RecordError(job.DeviceId, "The notification was too large to send");
            return;
        }

        var message = job.Message with { TtlSeconds = Math.Clamp(job.Message.TtlSeconds, MinTtl, MaxTtl) };
        var result = await sender.SendAsync(new PushTarget(sub.Endpoint, sub.P256dh, sub.Auth), message, token).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case PushOutcome.Sent:
                store.RecordSuccess(job.DeviceId);
                break;
            case PushOutcome.Gone:
                // Only the subscription that was sent to: a re-subscribe with a new endpoint meanwhile stays.
                store.RemoveIfEndpoint(job.DeviceId, sub.Endpoint);
                break;
            default:
                store.RecordError(job.DeviceId, result.Status is 401 or 403 ? Refused : result.Error ?? "push failed");
                break;
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            onError?.Invoke(ex);
        }
        catch
        {
            // The reporter must not throw.
        }
    }
}
