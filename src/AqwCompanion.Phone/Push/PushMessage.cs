namespace AqwCompanion.Phone.Push;

public enum PushUrgency { Normal, High }

/// <summary><paramref name="Payload"/> is the plaintext JSON (at most 2,000 bytes); the sender encrypts it.</summary>
public sealed record PushMessage(byte[] Payload, int TtlSeconds, PushUrgency Urgency);

/// <summary>A browser's subscription as the PC needs it: the endpoint and the two keys as base64url text.</summary>
public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

public enum PushOutcome { Sent, Gone, Failed }

public sealed record PushResult(PushOutcome Outcome, int? Status, string? Error);
