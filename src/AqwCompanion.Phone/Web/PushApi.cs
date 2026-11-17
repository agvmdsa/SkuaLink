using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AqwCompanion.Phone.Push;

namespace AqwCompanion.Phone.Web;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PushKeyBody))]
[JsonSerializable(typeof(PushStateBody))]
[JsonSerializable(typeof(PushOkBody))]
internal sealed partial class PushApiJson : JsonSerializerContext;

public sealed record PushKeyBody(string PublicKey);

public sealed record PushStateBody(bool Subscribed, IReadOnlyList<string> Kinds, string? LastError);

public sealed record PushOkBody(bool Ok);

/// <summary>
/// The <c>/api/push/*</c> routes of one paired phone (the device id comes from its cookie, never from the request). Check order:
/// route (404), method (405), content type (415, bodies only), body size (413), JSON (400), then the rules.
/// The state never contains the endpoint or the keys.
/// </summary>
public sealed class PushApi(PushSubscriptionStore store, IVapidKeySource vapid, PushSender sender, Func<DateTimeOffset> now)
{
    public const string Prefix = "/api/push";
    public const int MaxBody = 4096;
    public static readonly TimeSpan TestEvery = TimeSpan.FromSeconds(10);

    public const string NoSubscription = "not subscribed";
    public const string EndpointRefused = "endpoint not allowed";
    public const string InvalidKeys = "invalid keys";
    public const string UnknownKind = "unknown kind";
    public const string InvalidBody = "invalid body";
    public const string NotJson = "body must be json";
    public const string TooLarge = "body too large";
    public const string WaitText = "wait a few seconds";
    public const string Expired = "subscription expired";
    public const string Unavailable = "try again in a moment";

    private const int MaxText = 512;
    private static readonly JsonDocumentOptions BodyOptions = new() { MaxDepth = 8 };

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _lastTest = new(StringComparer.Ordinal);

    public static bool Handles(string path) => path == Prefix || path.StartsWith(Prefix + "/", StringComparison.Ordinal);

    public async Task<PhoneResponse> HandleAsync(PhoneRequest request, string path, string deviceId, CancellationToken ct)
    {
        var method = request.Method.ToUpperInvariant();
        switch (path)
        {
            case Prefix + "/key":
                return method == "GET" ? PhoneHandler.Json(200, JsonSerializer.Serialize(new PushKeyBody(vapid.GetOrCreate().PublicKeyText), PushApiJson.Default.PushKeyBody)) : NotAllowed();
            case Prefix + "/state":
                return method == "GET" ? State(deviceId) : NotAllowed();
            case Prefix + "/subscription":
                if (method == "DELETE")
                {
                    store.Remove(deviceId);
                    lock (_lock)
                    {
                        _lastTest.Remove(deviceId);
                    }

                    return State(deviceId);
                }

                return method == "PUT" ? Subscribe(request, deviceId) : NotAllowed();
            case Prefix + "/kinds":
                return method == "PUT" ? SetKinds(request, deviceId) : NotAllowed();
            case Prefix + "/test":
                return method == "POST" ? await TestAsync(deviceId, ct).ConfigureAwait(false) : NotAllowed();
            default:
                return PhoneHandler.Error(404, "not found");
        }
    }

    private PhoneResponse State(string deviceId)
    {
        var sub = store.Get(deviceId);
        return StateResponse(sub, store.LastError(deviceId));
    }

    private static PhoneResponse StateResponse(PushSubscription? sub, string? lastError) =>
        PhoneHandler.Json(200, JsonSerializer.Serialize(new PushStateBody(sub is not null, sub?.Kinds ?? [], lastError), PushApiJson.Default.PushStateBody));

    private PhoneResponse Subscribe(PhoneRequest request, string deviceId)
    {
        if (ReadBody(request) is { } refused)
        {
            return refused;
        }

        using var doc = JsonDocument.Parse(request.Body, BodyOptions);
        var root = doc.RootElement;
        var endpoint = Text(root, "endpoint");
        if (endpoint is null || endpoint.Length > PushEndpointPolicy.MaxLength || !PushEndpointPolicy.IsAllowed(endpoint))
        {
            return PhoneHandler.Error(400, EndpointRefused);
        }

        string? p256dh = null;
        string? auth = null;
        if (root.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
        {
            p256dh = Text(keys, "p256dh");
            auth = Text(keys, "auth");
        }

        if (!ValidKeys(p256dh, auth))
        {
            return PhoneHandler.Error(400, InvalidKeys);
        }

        IReadOnlyList<string>? kinds = null;
        if (root.TryGetProperty("kinds", out var kindsElement) && kindsElement.ValueKind != JsonValueKind.Null)
        {
            if (kindsElement.ValueKind != JsonValueKind.Array)
            {
                return PhoneHandler.Error(400, InvalidBody);
            }

            kinds = ParseKinds(kindsElement);
            if (kinds is null)
            {
                return PhoneHandler.Error(400, UnknownKind);
            }
        }

        store.Set(deviceId, endpoint, p256dh!, auth!, kinds);
        // A store that is forgetting answers Set without keeping anything: that is not a subscription.
        if (store.Get(deviceId) is not { } stored || stored.Endpoint != endpoint || stored.P256dh != p256dh || stored.Auth != auth)
        {
            return PhoneHandler.Error(503, Unavailable);
        }

        return StateResponse(stored, null);
    }

    private PhoneResponse SetKinds(PhoneRequest request, string deviceId)
    {
        if (ReadBody(request) is { } refused)
        {
            return refused;
        }

        using var doc = JsonDocument.Parse(request.Body, BodyOptions);
        if (!doc.RootElement.TryGetProperty("kinds", out var kindsElement) || kindsElement.ValueKind != JsonValueKind.Array)
        {
            return PhoneHandler.Error(400, InvalidBody);
        }

        var kinds = ParseKinds(kindsElement);
        if (kinds is null)
        {
            return PhoneHandler.Error(400, UnknownKind);
        }

        return store.SetKinds(deviceId, kinds) ? State(deviceId) : PhoneHandler.Error(409, NoSubscription);
    }

    private async Task<PhoneResponse> TestAsync(string deviceId, CancellationToken ct)
    {
        var sub = store.Get(deviceId);
        if (sub is null)
        {
            return PhoneHandler.Error(409, NoSubscription);
        }

        var at = now();
        lock (_lock)
        {
            foreach (var stale in _lastTest.Where(e => at - e.Value >= TestEvery || at < e.Value).Select(e => e.Key).ToList())
            {
                _lastTest.Remove(stale);   // a revoked phone leaves no entry behind, and a clock moved backwards blocks nobody
            }

            if (_lastTest.ContainsKey(deviceId))
            {
                var wait = PhoneHandler.Error(429, WaitText);
                return wait with { Headers = [.. wait.Headers, new KeyValuePair<string, string>("Retry-After", ((int)TestEvery.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture))] };
            }

            _lastTest[deviceId] = at;
        }

        var result = await sender.SendAsync(new PushTarget(sub.Endpoint, sub.P256dh, sub.Auth), PushPayload.Test(at), ct).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case PushOutcome.Sent:
                store.RecordSuccess(deviceId);
                return PhoneHandler.Json(200, JsonSerializer.Serialize(new PushOkBody(true), PushApiJson.Default.PushOkBody));
            case PushOutcome.Gone:
                store.RemoveIfEndpoint(deviceId, sub.Endpoint);
                return PhoneHandler.Error(410, Expired);
            default:
                var text = result.Error ?? "push failed";
                store.RecordError(deviceId, result.Status is 401 or 403 ? PushDispatcher.Refused : text);
                return PhoneHandler.Error(502, text);
        }
    }

    private static PhoneResponse NotAllowed() => PhoneHandler.Error(405, "method not allowed");

    /// <summary>Null when the body is a JSON object within the cap; else the 415, 413 or 400.</summary>
    private static PhoneResponse? ReadBody(PhoneRequest request)
    {
        var type = request.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        if (type is null || !type.Split(';', 2)[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return PhoneHandler.Error(415, NotJson);
        }

        if (Encoding.UTF8.GetByteCount(request.Body) > MaxBody)
        {
            return PhoneHandler.Error(413, TooLarge);
        }

        try
        {
            using var doc = JsonDocument.Parse(request.Body, BodyOptions);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? null : PhoneHandler.Error(400, InvalidBody);
        }
        catch (JsonException)
        {
            return PhoneHandler.Error(400, InvalidBody);
        }
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The kinds as given, or null when one is not a string or not a known name.</summary>
    private static IReadOnlyList<string>? ParseKinds(JsonElement array)
    {
        var list = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } name || !PushKinds.All.Contains(name, StringComparer.Ordinal))
            {
                return null;
            }

            list.Add(name);
        }

        return PushKinds.Normalize(list);
    }

    private static bool ValidKeys(string? p256dh, string? auth)
    {
        if (p256dh is null || auth is null || p256dh.Length > MaxText || auth.Length > MaxText)
        {
            return false;
        }

        try
        {
            return Base64Url.DecodeFromChars(auth).Length == WebPushEncryption.AuthSecretBytes
                && WebPushEncryption.IsValidPublicKey(Base64Url.DecodeFromChars(p256dh));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
