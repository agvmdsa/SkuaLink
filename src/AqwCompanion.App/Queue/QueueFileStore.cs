using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using AqwCompanion.Phone.Queue;
using Serilog;

namespace AqwCompanion.App.Queue;

/// <summary>The file of one queue (R12): steps and numbers only, no path and no process data.</summary>
internal sealed record QueueFile(int Version, string Character, int Cursor, bool Finished, List<QueueStep?> Steps);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false)]
[JsonSerializable(typeof(QueueFile))]
internal sealed partial class QueueFileJsonContext : JsonSerializerContext;

/// <summary>
/// One <c>skua-queue.&lt;character&gt;.json</c> per character in <paramref name="folder"/>, written whole to a temp file and moved into place.
/// A file that cannot be read is no queue; nothing here throws.
/// </summary>
public sealed class QueueFileStore(string folder) : IQueueStore
{
    private const int CurrentVersion = 1;

    /// <summary>A queue file is at most about 20 steps of a few hundred bytes; anything bigger is not ours.</summary>
    public const long MaxFileBytes = 64 * 1024;
    private readonly object _lock = new();

    public IReadOnlyList<StoredQueue> LoadAll()
    {
        lock (_lock)
        {
            var result = new List<StoredQueue>();
            try
            {
                if (!Directory.Exists(folder))
                {
                    return result;
                }

                var files = Directory.EnumerateFiles(folder, SkuaQueueFiles.Prefix + "*")
                    .Where(f => SkuaQueueFiles.IsQueueFile(Path.GetFileName(f)) && !f.EndsWith(".tmp", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    if (Read(file) is { } queue && seen.Add(queue.Character))
                    {
                        result.Add(queue);
                    }
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                Log.Error("Queue load failed: {ExceptionType}", ex.GetType().Name);
            }

            return result;
        }
    }

    public void Save(StoredQueue queue)
    {
        lock (_lock)
        {
            string path;
            string temp;
            try
            {
                path = Path.Combine(folder, SkuaQueueFiles.FileName(queue.Character));
                temp = path + ".tmp";
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                Log.Error("Queue save failed: {ExceptionType}", ex.GetType().Name);
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                var file = new QueueFile(CurrentVersion, queue.Character, queue.Cursor, queue.Finished, [.. queue.Steps]);
                File.WriteAllText(temp, JsonSerializer.Serialize(file, QueueFileJsonContext.Default.QueueFile));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                Log.Error("Queue save failed: {ExceptionType}", ex.GetType().Name);
                try
                {
                    File.Delete(temp);
                }
                catch (Exception cleanup) when (IsExpected(cleanup))
                {
                    // The temp file is harmless; the next save overwrites it.
                }
            }
        }
    }

    public void DeleteAll()
    {
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(folder, SkuaQueueFiles.Prefix + "*").ToList())
                {
                    if (!SkuaQueueFiles.IsQueueFile(Path.GetFileName(file)))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (IsExpected(ex))
                    {
                        Log.Error("Queue delete failed: {ExceptionType}", ex.GetType().Name);
                    }
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                Log.Error("Queue delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private static StoredQueue? Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return null;
            }

            var file = JsonSerializer.Deserialize(File.ReadAllText(path), QueueFileJsonContext.Default.QueueFile);
            if (file is null || file.Version != CurrentVersion)
            {
                return null;
            }

            var character = QueueText.Clean(file.Character, 100);
            if (character.Length == 0)
            {
                return null;
            }

            var steps = QueueValidator.Normalize(file.Steps);
            return new StoredQueue(character, steps, Math.Clamp(file.Cursor, 0, steps.Count), file.Finished && steps.Count > 0);
        }
        catch (Exception ex) when (ex is JsonException || IsExpected(ex))
        {
            Log.Error("Queue load failed: {ExceptionType}", ex.GetType().Name);
            return null;
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or SecurityException;
}
