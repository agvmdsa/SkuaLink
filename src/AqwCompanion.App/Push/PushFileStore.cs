using AqwCompanion.Phone.Push;
using Serilog;

namespace AqwCompanion.App.Push;

/// <summary>Keeps the push subscriptions as one JSON file, written whole and moved into place. Never throws.</summary>
public sealed class PushFileStore(string path) : IPushFileStore
{
    public PushFile Load()
    {
        try
        {
            return File.Exists(path) && PushFileJson.TryParse(File.ReadAllText(path), out var file) ? file : PushFile.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Push file load failed: {ExceptionType}", ex.GetType().Name);
            return PushFile.Empty;
        }
    }

    public void Save(PushFile file)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, PushFileJson.Serialize(file));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Push file save failed: {ExceptionType}", ex.GetType().Name);
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temp file is harmless; the next save overwrites it.
            }
        }
    }

    public void Delete()
    {
        // A crashed save can leave the temp file behind, and it holds the same subscription secrets.
        foreach (var file in new[] { path, path + ".tmp" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Push file delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
