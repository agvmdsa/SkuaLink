using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Push;

public class VapidKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_public_key_is_an_uncompressed_p256_point_in_base64url()
    {
        var key = VapidKey.Create();

        Assert.Equal(65, key.PublicKey.Length);
        Assert.Equal(0x04, key.PublicKey[0]);
        Assert.Equal(key.PublicKey, Base64Url.DecodeFromChars(key.PublicKeyText));
        Assert.DoesNotContain('=', key.PublicKeyText);
    }

    [Fact]
    public void The_jwt_has_the_es256_header_the_claims_and_a_signature_that_verifies_with_the_public_key()
    {
        var key = VapidKey.Create();

        var jwt = key.Jwt("https://fcm.googleapis.com", Now);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(Now.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal("https://github.com/agvmdsa/SkuaLink", claims.RootElement.GetProperty("sub").GetString());
        var signature = Base64Url.DecodeFromChars(parts[2]);
        Assert.Equal(64, signature.Length);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = key.PublicKey[1..33], Y = key.PublicKey[33..65] },
        });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.False(verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1] + "x"), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void The_audience_is_the_origin_of_the_endpoint_without_path_or_default_port()
    {
        Assert.Equal("https://web.push.apple.com", VapidKey.Audience(new Uri("https://web.push.apple.com:443/wpush/v2/abc?x=1")));
        Assert.Equal("https://fcm.googleapis.com", VapidKey.Audience(new Uri("https://fcm.googleapis.com/fcm/send/abc")));
    }

    [Fact]
    public void The_authorization_header_is_vapid_t_comma_k()
    {
        var key = VapidKey.Create();

        var header = key.AuthorizationHeader(new Uri("https://fcm.googleapis.com/fcm/send/abc"), Now);

        Assert.StartsWith("vapid t=", header, StringComparison.Ordinal);
        Assert.EndsWith(", k=" + key.PublicKeyText, header, StringComparison.Ordinal);
        Assert.Equal(3, header["vapid t=".Length..header.IndexOf(',')].Split('.').Length);
    }

    [Fact]
    public void An_exported_key_parses_back_to_the_same_public_key()
    {
        var key = VapidKey.Create();

        var again = VapidKey.TryParse(key.Export());

        Assert.NotNull(again);
        Assert.Equal(key.PublicKeyText, again!.PublicKeyText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!!")]
    [InlineData("AAAA")]
    public void Damaged_text_is_not_a_key(string? text) => Assert.Null(VapidKey.TryParse(text));

    [Fact]
    public void The_token_never_lives_longer_than_24_hours()
    {
        Assert.True(VapidKey.TokenLife <= TimeSpan.FromHours(24));
    }

    [Fact]
    public void The_public_key_is_a_copy_that_callers_cannot_change()
    {
        var key = VapidKey.Create();

        key.PublicKey[1] ^= 0xFF;

        Assert.Equal(key.PublicKeyText, System.Buffers.Text.Base64Url.EncodeToString(key.PublicKey));
    }

    [Fact]
    public void A_der_that_is_not_an_ec_key_is_not_a_key()
    {
        Assert.Null(VapidKey.TryParse(System.Buffers.Text.Base64Url.EncodeToString(new byte[40])));
    }

    [Fact]
    public void A_key_on_another_curve_is_not_a_key()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(VapidKey.TryParse(Base64Url.EncodeToString(p384.ExportECPrivateKey())));

        // secp256k1 has 32-byte coordinates like P-256: only the curve check tells them apart (some platforms cannot even import it).
        ECDsa? k1 = null;
        try
        {
            k1 = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException or ArgumentException)
        {
        }

        using (k1)
        {
            if (k1 is not null)
            {
                Assert.Null(VapidKey.TryParse(Base64Url.EncodeToString(k1.ExportECPrivateKey())));
            }
        }
    }
}
