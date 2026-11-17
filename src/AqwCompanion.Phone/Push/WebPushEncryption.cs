using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AqwCompanion.Phone.Push;

/// <summary>RFC 8291 (Web Push message encryption) with the RFC 8188 aes128gcm content coding, one record per message. Pure.</summary>
public static class WebPushEncryption
{
    public const int PublicKeyBytes = 65;
    public const int AuthSecretBytes = 16;
    public const int SaltBytes = 16;
    public const int RecordSize = 4096;
    public const int PadBlock = 256;
    public const int HeaderBytes = SaltBytes + 4 + 1 + PublicKeyBytes;
    public const int TagBytes = 16;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] CekInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    public static bool IsValidPublicKey(ReadOnlySpan<byte> uaPublic)
    {
        if (uaPublic.Length != PublicKeyBytes || uaPublic[0] != 0x04)
        {
            return false;
        }

        try
        {
            // A real agreement against a throwaway key: OpenSSL rejects an off-curve point at import, CNG may only fail here (and reports it as PlatformNotSupportedException).
            using var key = Import(uaPublic);
            using var probe = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            return probe.DeriveRawSecretAgreement(key.PublicKey).Length == 32;
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> uaPublic, ReadOnlySpan<byte> authSecret)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, uaPublic, authSecret, ephemeral, RandomNumberGenerator.GetBytes(SaltBytes), PadBlock);
    }

    internal static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> uaPublic, ReadOnlySpan<byte> authSecret, ECDiffieHellman asKey, ReadOnlySpan<byte> salt, int padBlock)
    {
        if (authSecret.Length != AuthSecretBytes)
        {
            throw new ArgumentException("The auth secret must be 16 bytes.", nameof(authSecret));
        }

        if (salt.Length != SaltBytes)
        {
            throw new ArgumentException("The salt must be 16 bytes.", nameof(salt));
        }

        if (asKey.KeySize != 256)
        {
            throw new ArgumentException("The key must be P-256.", nameof(asKey));
        }

        if (uaPublic.Length != PublicKeyBytes || uaPublic[0] != 0x04)
        {
            throw new ArgumentException("The public key must be a 65 byte uncompressed P-256 point.", nameof(uaPublic));
        }

        var length = plaintext.Length + 1;   // the 0x02 delimiter of the last record
        if (padBlock > 0)
        {
            length = (length + padBlock - 1) / padBlock * padBlock;
        }

        if (HeaderBytes + length + TagBytes > RecordSize)
        {
            throw new ArgumentException("The payload does not fit one record.", nameof(plaintext));
        }

        var q = asKey.ExportParameters(false).Q;
        byte[] asPublic = [0x04, .. q.X!, .. q.Y!];
        byte[] secret;
        try
        {
            using var ua = Import(uaPublic);
            secret = asKey.DeriveRawSecretAgreement(ua.PublicKey);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw new ArgumentException("The public key is not a point on P-256.", nameof(uaPublic), ex);
        }

        var keyInfo = new byte[KeyInfoPrefix.Length + PublicKeyBytes * 2];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        uaPublic.CopyTo(keyInfo.AsSpan(KeyInfoPrefix.Length));
        asPublic.CopyTo(keyInfo, KeyInfoPrefix.Length + PublicKeyBytes);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, secret, authSecret.ToArray());
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt.ToArray());
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, CekInfo);
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, NonceInfo);

        var record = new byte[length];
        plaintext.CopyTo(record);
        record[plaintext.Length] = 0x02;   // the rest stays zero: padding

        var message = new byte[HeaderBytes + length + TagBytes];
        salt.CopyTo(message);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(SaltBytes, 4), RecordSize);
        message[SaltBytes + 4] = PublicKeyBytes;
        asPublic.CopyTo(message, SaltBytes + 5);
        using var aes = new AesGcm(cek, TagBytes);
        aes.Encrypt(nonce, record, message.AsSpan(HeaderBytes, length), message.AsSpan(HeaderBytes + length, TagBytes));
        return message;
    }

    private static ECDiffieHellman Import(ReadOnlySpan<byte> publicKey) => ECDiffieHellman.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = publicKey[1..33].ToArray(), Y = publicKey[33..65].ToArray() },
    });
}
