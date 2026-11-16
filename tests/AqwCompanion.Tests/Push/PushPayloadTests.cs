using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Push;

public class PushPayloadTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private const string Emoji = "\U0001F600";

    private static Notice N(string character = "Hero Two", string title = "Script stopped", string body = "It stopped.", string id = "n-1", NoticeKind kind = NoticeKind.ScriptStopped) =>
        new(id, kind, character, title, body, T0);

    private static JsonElement Parse(PushMessage m) => JsonDocument.Parse(m.Payload).RootElement;

    [Fact]
    public void Build_writes_the_six_fields_and_the_url_of_the_characters_bot_section()
    {
        var json = Parse(PushPayload.Build(N()));

        Assert.Equal("Script stopped", json.GetProperty("title").GetString());
        Assert.Equal("It stopped.", json.GetProperty("body").GetString());
        Assert.Equal("n-1", json.GetProperty("tag").GetString());
        Assert.Equal("/#/character/Hero%20Two/bot", json.GetProperty("url").GetString());
        Assert.Equal("ScriptStopped", json.GetProperty("kind").GetString());
        Assert.Equal(T0.ToString("O"), json.GetProperty("at").GetString());
        Assert.Equal(6, json.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Skua 2")]
    public void A_notice_without_a_character_or_for_a_Skua_label_opens_the_list(string character)
    {
        Assert.Equal("/", PushPayload.UrlFor(N(character)));
    }

    [Fact]
    public void Title_and_body_are_cut_to_80_and_200_characters_without_splitting_a_surrogate_pair()
    {
        var long300 = string.Concat(Enumerable.Repeat(Emoji, 300));
        var json = Parse(PushPayload.Build(N(title: long300, body: long300)));

        var title = json.GetProperty("title").GetString()!;
        var body = json.GetProperty("body").GetString()!;
        Assert.InRange(title.Length, 1, 80);
        Assert.InRange(body.Length, 1, 200);
        foreach (var text in new[] { title, body })
        {
            Assert.Equal(text, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)));
            Assert.True(text.Length % 2 == 0);
        }
    }

    [Fact]
    public void The_payload_is_at_most_2000_bytes_even_for_four_byte_characters_in_every_field()
    {
        var e = (int n) => string.Concat(Enumerable.Repeat(Emoji, n));

        var message = PushPayload.Build(N(character: e(300), title: e(80), body: e(200), id: e(100)));

        Assert.InRange(message.Payload.Length, 1, 2000);
        JsonDocument.Parse(message.Payload);
    }

    [Fact]
    public void Non_ascii_text_is_written_as_utf8_not_as_escapes()
    {
        var message = PushPayload.Build(N(body: "Ação"));

        Assert.True(message.Payload.AsSpan().IndexOf("Ação"u8) >= 0);
        Assert.Equal("Ação", Parse(message).GetProperty("body").GetString());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Ttl_is_300_for_no_progress_and_dying_and_3600_for_the_rest(NoticeKind kind)
    {
        var expected = kind is NoticeKind.NoProgress or NoticeKind.DyingOften ? 300 : 3600;

        Assert.Equal(expected, PushPayload.TtlFor(kind));
        Assert.Equal(expected, PushPayload.Build(N(kind: kind)).TtlSeconds);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Urgency_is_high_for_errors_kicks_and_options_waiting(NoticeKind kind)
    {
        var high = kind is NoticeKind.ScriptError or NoticeKind.CharacterKicked or NoticeKind.SkuaDisconnected or NoticeKind.SkuaOptionsWaiting
            ? PushUrgency.High : PushUrgency.Normal;

        Assert.Equal(high, PushPayload.UrgencyFor(kind));
        Assert.Equal(high, PushPayload.Build(N(kind: kind)).Urgency);
    }

    public static TheoryData<NoticeKind> Kinds() => new(Enum.GetValues<NoticeKind>());

    [Fact]
    public void Test_is_a_fixed_small_message()
    {
        var message = PushPayload.Test(T0);

        var json = Parse(message);
        Assert.Equal("SkuaLink test", json.GetProperty("title").GetString());
        Assert.Equal("Notifications work on this phone.", json.GetProperty("body").GetString());
        Assert.Equal("test", json.GetProperty("tag").GetString());
        Assert.Equal("/", json.GetProperty("url").GetString());
        Assert.Equal("Test", json.GetProperty("kind").GetString());
        Assert.Equal(T0.ToString("O"), json.GetProperty("at").GetString());
        Assert.Equal(300, message.TtlSeconds);
        Assert.Equal(PushUrgency.Normal, message.Urgency);
    }
}
