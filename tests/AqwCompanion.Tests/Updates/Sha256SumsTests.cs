using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class Sha256SumsTests : IDisposable
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));

    public Sha256SumsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_line_of_the_exact_file_name_is_found_and_other_lines_are_ignored()
    {
        var text = $"{A}  SkuaLink-Setup-v0.8.0.exe\n{B}  SkuaLink.SkuaBridge.dll\n";

        Assert.Equal(SumsLookup.Found, Sha256Sums.TryFind(text, "SkuaLink.SkuaBridge.dll", out var hash));
        Assert.Equal(B, hash);
    }

    [Fact]
    public void Windows_line_endings_a_byte_order_mark_and_the_binary_marker_are_accepted()
    {
        var text = $"\uFEFF{A} *SkuaLink-Setup-v0.8.0.exe\r\n{B}  SHA256SUMS.txt\r\n";

        Assert.Equal(SumsLookup.Found, Sha256Sums.TryFind(text, "SkuaLink-Setup-v0.8.0.exe", out var hash));
        Assert.Equal(A, hash);
    }

    [Fact]
    public void Upper_case_hex_is_accepted_and_compared_ignoring_case()
    {
        var text = $"{A.ToUpperInvariant()}  x.exe\n";

        Assert.Equal(SumsLookup.Found, Sha256Sums.TryFind(text, "x.exe", out var hash));
        Assert.True(Sha256Sums.Matches(hash, A));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    public void An_empty_file_has_no_line(string text)
    {
        Assert.Equal(SumsLookup.Missing, Sha256Sums.TryFind(text, "x.exe", out _));
    }

    [Fact]
    public void A_different_file_name_is_missing_even_when_it_ends_the_same_way_in_a_folder()
    {
        var text = $"{A}  other/x.exe\n{B}  y.exe\n";

        Assert.Equal(SumsLookup.Missing, Sha256Sums.TryFind(text, "x.exe", out _));
    }

    [Theory]
    [InlineData("abc  x.exe")]
    [InlineData("zzzz  x.exe")]
    [InlineData("x.exe")]
    [InlineData("{0}x.exe")]
    [InlineData("{0} x.exe extra")]
    public void A_line_for_the_file_that_is_not_a_valid_hash_line_is_malformed(string template)
    {
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, A) + "\n";

        Assert.Equal(SumsLookup.Malformed, Sha256Sums.TryFind(text, "x.exe", out _));
    }

    [Fact]
    public void Two_different_hashes_for_one_file_are_malformed_two_equal_ones_are_fine()
    {
        Assert.Equal(SumsLookup.Malformed, Sha256Sums.TryFind($"{A}  x.exe\n{B}  x.exe\n", "x.exe", out _));
        Assert.Equal(SumsLookup.Found, Sha256Sums.TryFind($"{A}  x.exe\n{A}  x.exe\n", "x.exe", out _));
    }

    [Fact]
    public void A_file_is_hashed_to_lower_case_hex()
    {
        var path = Path.Combine(_dir, "abc.bin");
        File.WriteAllBytes(path, "abc"u8.ToArray());

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256Sums.HashFile(path));
    }

    [Fact]
    public void Matches_is_false_for_a_different_hash()
    {
        Assert.False(Sha256Sums.Matches(A, B));
        Assert.False(Sha256Sums.Matches(A, A + "0"));
    }
}
