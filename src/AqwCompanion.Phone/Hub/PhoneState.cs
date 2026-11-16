using System.Text.Json;
using System.Text.Json.Serialization;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Phone.Hub;

/// <summary>
/// What a phone sees: the last snapshot and the recent notices. <paramref name="Skua"/> is the first connected Skua (as
/// before several Skua were known); <paramref name="SkuaInstances"/> lists every Skua in order of arrival. Both are null
/// when no Skua feed is wired. <paramref name="Characters"/> is every character the PC knows, live or remembered, with Skua
/// instances that have no character; null when no character book is wired.
/// </summary>
public sealed record PhoneState(
    FeedSnapshot Snapshot,
    IReadOnlyList<Notice> Notices,
    SkuaView? Skua = null,
    IReadOnlyList<SkuaInstanceState>? SkuaInstances = null,
    IReadOnlyList<PhoneCharacter>? Characters = null);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PhoneState))]
[JsonSerializable(typeof(Notice))]
[JsonSerializable(typeof(FeedSnapshot))]
[JsonSerializable(typeof(SkuaView))]
[JsonSerializable(typeof(PhoneCharacter))]
internal sealed partial class PhoneJsonContext : JsonSerializerContext
{
}

public static class PhoneJson
{
    public static string Serialize(PhoneState state) => JsonSerializer.Serialize(state, PhoneJsonContext.Default.PhoneState);

    public static string SerializeNotice(Notice notice) => JsonSerializer.Serialize(notice, PhoneJsonContext.Default.Notice);

    public static string SerializeSnapshot(FeedSnapshot snapshot) => JsonSerializer.Serialize(snapshot, PhoneJsonContext.Default.FeedSnapshot);
}
