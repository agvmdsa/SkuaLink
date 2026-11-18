using AqwCompanion.Phone.Queue;

namespace AqwCompanion.Tests.Queue;

public class QueueTextTests
{
    [Theory]
    [InlineData("  Blood Orb  ", "Blood Orb")]
    [InlineData("Blood\tOrb\r\n", "BloodOrb")]
    [InlineData("a\u0000b\u007fc", "abc")]
    public void Control_characters_are_dropped_and_the_ends_trimmed(string input, string expected)
    {
        Assert.Equal(expected, QueueText.Clean(input, 80));
    }

    [Theory]
    [InlineData("\u202A")]
    [InlineData("\u202B")]
    [InlineData("\u202C")]
    [InlineData("\u202D")]
    [InlineData("\u202E")]
    [InlineData("\u2066")]
    [InlineData("\u2067")]
    [InlineData("\u2068")]
    [InlineData("\u2069")]
    [InlineData("\u200B")]
    [InlineData("\u200C")]
    [InlineData("\u200D")]
    [InlineData("\u200E")]
    [InlineData("\u200F")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\uFEFF")]
    public void Bidi_zero_width_and_line_separator_characters_are_dropped(string hostile)
    {
        Assert.Equal("Bloodbr", QueueText.Clean("Blood" + hostile + "br", 80));
        Assert.Equal("Blood Orb", QueueText.Clean(hostile + "Blood " + hostile + "Orb" + hostile, 80));
    }

    [Fact]
    public void A_lone_surrogate_is_dropped_and_a_valid_pair_is_kept()
    {
        Assert.Equal("ab", QueueText.Clean("a\ud800b", 80));
        Assert.Equal("a\U0001F600b", QueueText.Clean("a\U0001F600b", 80));
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var cut = QueueText.Clean("ab\U0001F600", 3);

        Assert.Equal("ab", cut);
    }

    [Fact]
    public void Null_is_empty()
    {
        Assert.Equal(string.Empty, QueueText.Clean(null, 10));
    }

    [Theory]
    [InlineData("Blood Orb", "  blood orb ", true)]
    [InlineData("Blood Orb", "Blood  Orb", false)]
    [InlineData("Blood Orb", "Blood Orbs", false)]
    [InlineData(null, "x", false)]
    [InlineData("x", null, false)]
    [InlineData(null, null, false)]
    public void Names_match_trimmed_and_ignoring_case_but_otherwise_exactly(string? a, string? b, bool same)
    {
        Assert.Equal(same, QueueText.SameName(a, b));
    }
}
