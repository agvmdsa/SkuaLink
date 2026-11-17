using System.Text.Json;

namespace AqwCompanion.App.Updates;

public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <summary>A release SkuaLink may offer: all three assets present, named exactly, on allowed addresses and within their caps.</summary>
public sealed record ReleaseInfo(ReleaseVersion Version, ReleaseAsset Installer, ReleaseAsset Plugin, ReleaseAsset Sums);

public enum ReleaseParseStatus { Ready, NotReady, Invalid }

public sealed record ReleaseParseResult(ReleaseParseStatus Status, ReleaseInfo? Release = null);

public static class ReleaseParser
{
    public const long MaxInstallerBytes = 300L * 1024 * 1024;
    public const long MaxPluginBytes = 10L * 1024 * 1024;
    public const long MaxSumsBytes = 64L * 1024;
    public const string PluginFileName = "SkuaLink.SkuaBridge.dll";
    public const string SumsFileName = "SHA256SUMS.txt";

    private const int MaxAssetsRead = 200;

    private static readonly ReleaseParseResult Invalid = new(ReleaseParseStatus.Invalid);
    private static readonly ReleaseParseResult NotReady = new(ReleaseParseStatus.NotReady);

    public static string InstallerFileName(ReleaseVersion version) => $"SkuaLink-Setup-v{version}.exe";

    /// <summary>Invalid: not a release object or a bad tag. NotReady: a draft, a prerelease or a release without its three good assets.</summary>
    public static ReleaseParseResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String
                || !ReleaseVersion.TryParseTag(tagElement.GetString(), out var version)
                || !Flag(root, "draft", out var draft) || !Flag(root, "prerelease", out var prerelease))
            {
                return Invalid;
            }

            if (draft || prerelease || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return NotReady;
            }

            var installer = Find(assets, InstallerFileName(version), MaxInstallerBytes);
            var plugin = Find(assets, PluginFileName, MaxPluginBytes);
            var sums = Find(assets, SumsFileName, MaxSumsBytes);
            return installer is null || plugin is null || sums is null
                ? NotReady
                : new ReleaseParseResult(ReleaseParseStatus.Ready, new ReleaseInfo(version, installer, plugin, sums));
        }
        catch (JsonException)
        {
            return Invalid;
        }
    }

    private static bool Flag(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    /// <summary>The first asset of exactly this name; null when it is absent, its address is outside the policy or its size is not within 1..max.</summary>
    private static ReleaseAsset? Find(JsonElement assets, string name, long maxBytes)
    {
        var seen = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            if (++seen > MaxAssetsRead)
            {
                return null;
            }

            if (asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String
                || nameElement.GetString() != name)
            {
                continue;
            }

            if (!asset.TryGetProperty("browser_download_url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var url) || !UpdateUrlPolicy.IsAllowedDownload(url)
                || !asset.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var size)
                || size < 1 || size > maxBytes)
            {
                return null;
            }

            return new ReleaseAsset(name, url, size);
        }

        return null;
    }
}
