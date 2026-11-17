using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Push;
using Serilog;

namespace AqwCompanion.App.Push;

/// <summary>
/// The PC's VAPID key, kept as text in <c>vapid.key</c> in the data folder and in memory once read. A missing or damaged file makes a new key.
/// Never throws: when the file cannot be written the key stays in memory only. The key is a secret and is never logged.
/// </summary>
public sealed class VapidKeyStore(string path) : IVapidKeySource, IForgettable
{
    private readonly object _lock = new();
    private VapidKey? _key;
    private bool _quiesced;

    public string Name => "Push notification key";

    public VapidKey GetOrCreate()
    {
        lock (_lock)
        {
            if (_key is not null)
            {
                return _key;
            }

            var key = Read(out var unreadable);
            if (key is not null)
            {
                return _key = key;
            }

            // A file that exists but cannot be read right now (antivirus, backup) is not a missing one: hand out a key for this call
            // only, keep the file and retry the read next time. The same goes while quiesced: nothing is written, so nothing is kept.
            var fresh = VapidKey.Create();
            if (unreadable || _quiesced)
            {
                return fresh;
            }

            Write(fresh);
            return _key = fresh;
        }
    }

    public VapidKey? GetExisting()
    {
        lock (_lock)
        {
            if (_quiesced)
            {
                return null;
            }

            if (_key is not null)
            {
                return _key;
            }

            var key = Read(out _);
            return key is null ? null : _key = key;
        }
    }

    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
        }
    }

    public void Forget()
    {
        lock (_lock)
        {
            _key = null;
            foreach (var file in new[] { path, path + ".tmp" })
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error("Vapid key delete failed: {ExceptionType}", ex.GetType().Name);
                }
            }

            _quiesced = false;
        }
    }

    private VapidKey? Read(out bool unreadable)
    {
        unreadable = false;
        try
        {
            return File.Exists(path) ? VapidKey.TryParse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Vapid key load failed: {ExceptionType}", ex.GetType().Name);
            unreadable = true;
            return null;
        }
    }

    private void Write(VapidKey key)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, key.Export());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Vapid key save failed: {ExceptionType}", ex.GetType().Name);
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temp file is harmless; the next write overwrites it.
            }
        }
    }
}
