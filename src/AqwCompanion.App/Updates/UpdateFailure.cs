namespace AqwCompanion.App.Updates;

public enum UpdateFailure { None, Refused, Download, TooLarge, TimedOut, Cancelled, HashMissing, HashMismatch, Disk }

internal static class UpdateFailures
{
    public static UpdateFailure From(DownloadStatus status) => status switch
    {
        DownloadStatus.Ok => UpdateFailure.None,
        DownloadStatus.Refused => UpdateFailure.Refused,
        DownloadStatus.TooLarge => UpdateFailure.TooLarge,
        DownloadStatus.TimedOut => UpdateFailure.TimedOut,
        DownloadStatus.Cancelled => UpdateFailure.Cancelled,
        _ => UpdateFailure.Download,
    };
}
