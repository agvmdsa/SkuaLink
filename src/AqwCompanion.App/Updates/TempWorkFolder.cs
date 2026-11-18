using System.Security.Cryptography;

namespace AqwCompanion.App.Updates;

/// <summary>A new, empty, randomly named folder for one download; the update code only ever executes or copies a file it verified inside such a folder.</summary>
public static class TempWorkFolder
{
    public static string Create(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "SkuaLink-update-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant());
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException("The work folder already exists.");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes the folder and what is in it; never throws (a file another program holds is left for the operating system's temp cleanup).</summary>
    public static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else to do.
        }
    }
}
