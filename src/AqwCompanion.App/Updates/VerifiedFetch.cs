namespace AqwCompanion.App.Updates;

/// <summary>Downloads the checksum file and one asset of a release into a folder and checks the asset's SHA-256 against the line of its exact name.</summary>
internal static class VerifiedFetch
{
    public static async Task<UpdateFailure> RunAsync(
        GuardedDownloader downloader, string folder, ReleaseAsset sums, ReleaseAsset file, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        try
        {
            var sumsPath = Path.Combine(folder, sums.Name);
            var status = await downloader.DownloadAsync(sums.Url, sumsPath, sums.Size, null, ct);
            if (status != DownloadStatus.Ok)
            {
                return UpdateFailures.From(status);
            }

            // The line for the file must exist before the file is downloaded at all.
            var text = await File.ReadAllTextAsync(sumsPath, ct);
            if (Sha256Sums.TryFind(text, file.Name, out var expected) != SumsLookup.Found)
            {
                return UpdateFailure.HashMissing;
            }

            var filePath = Path.Combine(folder, file.Name);
            status = await downloader.DownloadAsync(file.Url, filePath, file.Size, progress, ct);
            if (status != DownloadStatus.Ok)
            {
                return UpdateFailures.From(status);
            }

            return Sha256Sums.Matches(expected, Sha256Sums.HashFile(filePath)) ? UpdateFailure.None : UpdateFailure.HashMismatch;
        }
        catch (OperationCanceledException)
        {
            return UpdateFailure.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateFailure.Disk;
        }
    }
}
