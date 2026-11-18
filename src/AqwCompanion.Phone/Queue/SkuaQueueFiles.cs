using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Phone.Queue;

/// <summary>The file names of the per-character queues in the app data folder.</summary>
public static class SkuaQueueFiles
{
    public const string Prefix = "skua-queue.";
    public const string Suffix = ".json";

    public static string FileName(string character) => Prefix + SkuaLogFiles.Slug(character) + Suffix;

    /// <summary>True for <c>skua-queue.*.json</c> and its <c>.tmp</c> twin; nothing else.</summary>
    public static bool IsQueueFile(string fileName) =>
        fileName.StartsWith(Prefix, StringComparison.Ordinal)
        && (fileName.EndsWith(Suffix, StringComparison.Ordinal) || fileName.EndsWith(Suffix + ".tmp", StringComparison.Ordinal));
}
