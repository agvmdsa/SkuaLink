using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Phone.Push;

/// <summary>Builds the JSON the service worker shows: title, body, tag, url, kind, at. Never over <see cref="MaxBytes"/> bytes.</summary>
public static partial class PushPayload
{
    public const int MaxBytes = 2000;
    public const int MaxTitle = 80;
    public const int MaxBody = 200;
    public const int MaxTag = 100;
    private const int MaxUrl = 200;

    [GeneratedRegex(@"^Skua \d+$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuaLabel();

    public static PushMessage Build(Notice notice)
    {
        var title = Cut(notice.Title ?? string.Empty, MaxTitle);
        var body = Cut(notice.Body ?? string.Empty, MaxBody);
        var tag = Cut(notice.Id ?? string.Empty, MaxTag);
        var url = UrlFor(notice);
        var kind = notice.Kind.ToString();
        var at = notice.AtUtc.ToString("O");
        var bytes = Write(title, body, tag, url, kind, at);
        while (bytes.Length > MaxBytes && (body.Length > 0 || title.Length > 0))
        {
            if (body.Length > 0)
            {
                body = Cut(body, Math.Max(0, body.Length - 10));
            }
            else
            {
                title = Cut(title, Math.Max(0, title.Length - 10));
            }

            bytes = Write(title, body, tag, url, kind, at);
        }

        return new PushMessage(bytes, TtlFor(notice.Kind), UrgencyFor(notice.Kind));
    }

    public static PushMessage Test(DateTimeOffset now) =>
        new(Write("SkuaLink test", "Notifications work on this phone.", "test", "/", "Test", now.ToString("O")), 300, PushUrgency.Normal);

    public static string UrlFor(Notice notice)
    {
        var character = notice.Character;
        if (string.IsNullOrEmpty(character) || SkuaLabel().IsMatch(character))
        {
            return "/";
        }

        var url = "/#/character/" + Uri.EscapeDataString(character) + "/bot";
        return url.Length > MaxUrl ? "/" : url;
    }

    public static int TtlFor(NoticeKind kind) => kind is NoticeKind.NoProgress or NoticeKind.DyingOften ? 300 : 3600;

    public static PushUrgency UrgencyFor(NoticeKind kind) =>
        kind is NoticeKind.ScriptError or NoticeKind.CharacterKicked or NoticeKind.SkuaDisconnected or NoticeKind.SkuaOptionsWaiting ? PushUrgency.High : PushUrgency.Normal;

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var end = max;
        if (end > 0 && char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }

    private static byte[] Write(string title, string body, string tag, string url, string kind, string at)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("title", title);
            writer.WriteString("body", body);
            writer.WriteString("tag", tag);
            writer.WriteString("url", url);
            writer.WriteString("kind", kind);
            writer.WriteString("at", at);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
