using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Push;

public class WebPushEncryptionTests
{
    private static JsonElement Vector() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Push", "fixtures", "rfc8291-appendix-a.json"))).RootElement;

    private static byte[] B(JsonElement v, string name) => Base64Url.DecodeFromChars(v.GetProperty(name).GetString()!);

    private static ECDiffieHellman FromPrivate(byte[] d, byte[] publicKey) => ECDiffieHellman.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = d,
        Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] },
    });

    [Fact]
    public void The_rfc_8291_appendix_a_message_is_reproduced_byte_for_byte()
    {
        var v = Vector();
        using var asKey = FromPrivate(B(v, "asPrivate"), B(v, "asPublic"));

        var message = WebPushEncryption.Encrypt(B(v, "plaintext"), B(v, "uaPublic"), B(v, "authSecret"), asKey, B(v, "salt"), padBlock: 0);

        Assert.Equal(B(v, "message"), message);
        Assert.Equal(86, WebPushEncryption.HeaderBytes);
    }

    [Fact]
    public void The_plaintext_of_the_vector_is_the_watermelon_sentence()
    {
        Assert.Equal("When I grow up, I want to be a watermelon", Encoding.UTF8.GetString(B(Vector(), "plaintext")));
    }

    [Fact]
    public void A_message_round_trips_through_a_user_agent_key_and_is_padded_to_blocks_of_256()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);
        var plaintext = Encoding.UTF8.GetBytes("{\"title\":\"Hero keeps dying\"}");

        var message = WebPushEncryption.Encrypt(plaintext, Public(ua), auth);

        Assert.Equal(plaintext, Decrypt(message, ua, auth));
        Assert.Equal(WebPushEncryption.HeaderBytes + 256 + WebPushEncryption.TagBytes, message.Length);
        Assert.Equal([0x00, 0x00, 0x10, 0x00], message[16..20]);   // record size 4096, big endian
        Assert.Equal(65, message[20]);
    }

    [Fact]
    public void Two_messages_of_the_same_text_differ_because_salt_and_key_are_fresh()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);

        Assert.NotEqual(WebPushEncryption.Encrypt("a"u8, Public(ua), auth), WebPushEncryption.Encrypt("a"u8, Public(ua), auth));
    }

    [Fact]
    public void A_tampered_message_does_not_decrypt()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);
        var message = WebPushEncryption.Encrypt("hello"u8, Public(ua), auth);
        message[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => Decrypt(message, ua, auth));
    }

    [Fact]
    public void A_payload_that_cannot_fit_one_record_is_refused()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() => WebPushEncryption.Encrypt(new byte[4096], Public(ua), new byte[16]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(66)]
    public void A_public_key_of_the_wrong_length_is_refused(int length)
    {
        Assert.False(WebPushEncryption.IsValidPublicKey(new byte[length]));
        Assert.Throws<ArgumentException>(() => WebPushEncryption.Encrypt("x"u8, new byte[length], new byte[16]));
    }

    [Fact]
    public void A_key_that_is_not_a_point_on_the_curve_or_has_the_wrong_prefix_is_refused()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var good = Public(ua);
        var wrongPrefix = (byte[])good.Clone();
        wrongPrefix[0] = 0x02;
        var offCurve = (byte[])good.Clone();
        offCurve[64] ^= 1;

        Assert.True(WebPushEncryption.IsValidPublicKey(good));
        Assert.False(WebPushEncryption.IsValidPublicKey(wrongPrefix));
        Assert.False(WebPushEncryption.IsValidPublicKey(offCurve));
    }

    [Fact]
    public void An_auth_secret_that_is_not_16_bytes_is_refused()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() => WebPushEncryption.Encrypt("x"u8, Public(ua), new byte[15]));
    }

    private static byte[] Public(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        return [0x04, .. q.X!, .. q.Y!];
    }

    /// <summary>The receiving side of RFC 8291, only for these tests.</summary>
    private static byte[] Decrypt(byte[] message, ECDiffieHellman ua, byte[] authSecret)
    {
        var salt = message[..16];
        var idLength = message[20];
        var asPublic = message[21..(21 + idLength)];
        var cipher = message[(21 + idLength)..];
        using var asKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = asPublic[1..33], Y = asPublic[33..65] },
        });
        var secret = ua.DeriveRawSecretAgreement(asKey.PublicKey);
        var info = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(Public(ua)).Concat(asPublic).ToArray();
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, secret, authSecret), 32, info);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var plain = new byte[cipher.Length - 16];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
        var end = plain.Length;
        while (end > 0 && plain[end - 1] == 0)
        {
            end--;
        }

        Assert.Equal(0x02, plain[end - 1]);
        return plain[..(end - 1)];
    }
}
