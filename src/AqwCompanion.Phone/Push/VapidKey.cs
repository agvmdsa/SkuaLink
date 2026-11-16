using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AqwCompanion.Phone.Push;

/// <summary>The PC's VAPID key pair (RFC 8292): an ECDSA P-256 key that signs one ES256 JWT per push request. Safe from any thread.</summary>
public sealed class VapidKey
{
    public const string Subject = "https://github.com/agvmdsa/SkuaLink";
    public static readonly TimeSpan TokenLife = TimeSpan.FromHours(12);

    private readonly ECDsa _key;
    private readonly object _lock = new();

    private VapidKey(ECDsa key)
    {
        _key = key;
        var q = key.ExportParameters(false).Q;
        PublicKey = [0x04, .. q.X!, .. q.Y!];
        PublicKeyText = Base64Url.EncodeToString(PublicKey);
    }

    public byte[] PublicKey { get; }

    public string PublicKeyText { get; }

    public static VapidKey Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>The key a previous <see cref="Export"/> wrote; null for anything that is not a P-256 private key.</summary>
    public static VapidKey? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var key = ECDsa.Create();
            key.ImportECPrivateKey(Base64Url.DecodeFromChars(text.Trim()), out _);
            var q = key.ExportParameters(false).Q;
            if (q.X is not { Length: 32 } || q.Y is not { Length: 32 })
            {
                key.Dispose();
                return null;
            }

            return new VapidKey(key);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The private key as text (base64url of its SEC1 DER); only the key store writes it, only to the user's data folder.</summary>
    public string Export()
    {
        lock (_lock)
        {
            return Base64Url.EncodeToString(_key.ExportECPrivateKey());
        }
    }

    public static string Audience(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);

    public string AuthorizationHeader(Uri endpoint, DateTimeOffset now) => $"vapid t={Jwt(Audience(endpoint), now)}, k={PublicKeyText}";

    public string Jwt(string audience, DateTimeOffset now)
    {
        var header = Base64Url.EncodeToString("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("aud", audience);
            writer.WriteNumber("exp", now.Add(TokenLife).ToUnixTimeSeconds());
            writer.WriteString("sub", Subject);
            writer.WriteEndObject();
        }

        var signing = header + "." + Base64Url.EncodeToString(buffer.ToArray());
        byte[] signature;
        lock (_lock)
        {
            signature = _key.SignData(Encoding.ASCII.GetBytes(signing), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        return signing + "." + Base64Url.EncodeToString(signature);
    }
}
