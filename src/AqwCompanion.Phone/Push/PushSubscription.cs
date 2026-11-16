using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqwCompanion.Phone.Push;

/// <summary>One paired phone's Web Push subscription. The endpoint and keys are secrets: they are never printed.</summary>
public sealed record PushSubscription(
    string DeviceId, string Endpoint, string P256dh, string Auth, IReadOnlyList<string> Kinds, DateTimeOffset CreatedUtc, DateTimeOffset? LastSuccessUtc)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("DeviceId = ").Append(DeviceId).Append(", Endpoint = <redacted>, P256dh = <redacted>, Auth = <redacted>");
        return true;
    }
}

public sealed record PushFile(IReadOnlyList<PushSubscription> Subscriptions)
{
    public static PushFile Empty { get; } = new([]);
}

public interface IPushFileStore
{
    /// <summary>The saved file; a missing or damaged file is <see cref="PushFile.Empty"/>. Never throws.</summary>
    PushFile Load();

    /// <summary>Saves the file. A failure to write is not fatal and does not throw.</summary>
    void Save(PushFile file);

    /// <summary>Deletes the saved file. A failure is not fatal and does not throw.</summary>
    void Delete();
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PushFile))]
internal sealed partial class PushFileJsonContext : JsonSerializerContext;

public static class PushFileJson
{
    public static string Serialize(PushFile file) => JsonSerializer.Serialize(file, PushFileJsonContext.Default.PushFile);

    public static bool TryParse(string json, [NotNullWhen(true)] out PushFile? file)
    {
        try
        {
            file = JsonSerializer.Deserialize(json, PushFileJsonContext.Default.PushFile);
        }
        catch (JsonException)
        {
            file = null;
        }

        if (file is { Subscriptions: null })
        {
            file = file with { Subscriptions = [] };
        }

        return file is not null;
    }
}
