using System.Net;

namespace AqwCompanion.App.Updates;

public enum DownloadStatus { Ok, Refused, Failed, TooLarge, TimedOut, Cancelled }

public sealed record DownloadProgress(long Received, long Total);

/// <summary>
/// Downloads one file from GitHub: https only, redirects followed by hand (at most <see cref="UpdateUrlPolicy.MaxRedirects"/>) with the
/// address policy applied to every hop, a 200 with a content length within the cap, the cap enforced while reading, a deadline, and
/// cancellation. Never throws; on anything but <see cref="DownloadStatus.Ok"/> the destination file does not exist afterwards.
/// </summary>
public sealed class GuardedDownloader(HttpClient http, TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private const int BufferBytes = 81920;

    public async Task<DownloadStatus> DownloadAsync(Uri url, string destination, long maxBytes, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? DefaultTimeout);
        var created = false;
        try
        {
            var status = await RunAsync(url, destination, maxBytes, progress, deadline.Token, () => created = true);
            if (status != DownloadStatus.Ok && created)
            {
                TryDelete(destination);
            }

            return status;
        }
        catch (OperationCanceledException)
        {
            if (created)
            {
                TryDelete(destination);
            }

            return ct.IsCancellationRequested ? DownloadStatus.Cancelled : DownloadStatus.TimedOut;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (created)
            {
                TryDelete(destination);
            }

            return DownloadStatus.Failed;
        }
    }

    private async Task<DownloadStatus> RunAsync(
        Uri url, string destination, long maxBytes, IProgress<DownloadProgress>? progress, CancellationToken ct, Action markCreated)
    {
        var current = url;
        for (var redirects = 0; ; redirects++)
        {
            if (!UpdateUrlPolicy.IsAllowedDownload(current))
            {
                return DownloadStatus.Refused;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects >= UpdateUrlPolicy.MaxRedirects)
                {
                    return DownloadStatus.Refused;
                }

                if (response.Headers.Location is not { } location)
                {
                    return DownloadStatus.Failed;
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength is not { } length || length < 0)
            {
                return DownloadStatus.Failed;
            }

            if (length > maxBytes)
            {
                return DownloadStatus.TooLarge;
            }

            return await CopyAsync(response.Content, length, maxBytes, destination, progress, ct, markCreated);
        }
    }

    private static async Task<DownloadStatus> CopyAsync(
        HttpContent content, long length, long maxBytes, string destination, IProgress<DownloadProgress>? progress, CancellationToken ct, Action markCreated)
    {
        await using var source = await content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true);
        markCreated();
        var buffer = new byte[BufferBytes];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            received += read;
            if (received > length || received > maxBytes)
            {
                return DownloadStatus.TooLarge;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report(new DownloadProgress(received, length));
        }

        return received == length ? DownloadStatus.Ok : DownloadStatus.Failed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover partial file is removed with its folder by the caller.
        }
    }
}
