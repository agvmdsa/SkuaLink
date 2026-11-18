namespace AqwCompanion.App.Updates;

public enum PluginRequirementReason { UpdateAvailable, VersionMismatch }

/// <summary><paramref name="Required"/> is the plugin version to install; <paramref name="Installed"/> the version a connected Skua reports (null when none is connected).</summary>
public sealed record PluginRequirement(PluginRequirementReason Reason, ReleaseVersion Required, string? Installed);

/// <summary>When "New DLL required" is shown. Pure.</summary>
public static class PluginRule
{
    /// <param name="appVersion">The app's informational version.</param>
    /// <param name="updateVersion">The version of the update on offer, or null when there is none.</param>
    /// <param name="connectedPluginVersions"><c>hello.pluginVersion</c> of every connected Skua.</param>
    public static PluginRequirement? Evaluate(string appVersion, ReleaseVersion? updateVersion, IReadOnlyList<string> connectedPluginVersions)
    {
        var plugins = connectedPluginVersions.Select(Clean).Where(p => p.Length > 0).ToList();
        if (updateVersion is { } latest)
        {
            var required = latest.ToString();
            return plugins.Count > 0 && plugins.All(p => p == required)
                ? null
                : new PluginRequirement(PluginRequirementReason.UpdateAvailable, latest, plugins.FirstOrDefault(p => p != required));
        }

        // Without an update the plugin must match the app. A development build has no release tagged for its version, so there is nothing to offer.
        if (!ReleaseVersion.TryParseApp(appVersion, out var app, out var isPreRelease) || isPreRelease)
        {
            return null;
        }

        var appText = app.ToString();
        return plugins.FirstOrDefault(p => p != appText) is { } differing
            ? new PluginRequirement(PluginRequirementReason.VersionMismatch, app, differing)
            : null;
    }

    private static string Clean(string? version)
    {
        var text = (version ?? string.Empty).Trim();
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? text[..plus] : text;
    }
}
