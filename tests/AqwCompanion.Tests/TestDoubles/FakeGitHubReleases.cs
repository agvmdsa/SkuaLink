using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeGitHubReleases : IGitHubReleases
{
    public Queue<ReleaseCheck> Answers { get; } = new();

    public ReleaseCheck Default { get; set; } = new(ReleaseCheckStatus.Failed);

    public List<string?> Etags { get; } = [];

    public Func<Task>? Gate { get; set; }

    public Func<ReleaseVersion, ReleaseCheck>? ByVersion { get; set; }

    public List<ReleaseVersion> ByVersionAsked { get; } = [];

    public static ReleaseCheck Found(string version, string etag = "\"e1\"") =>
        new(ReleaseCheckStatus.Found, TestReleases.Info(version), etag);

    public async Task<ReleaseCheck> GetLatestAsync(string? etag, CancellationToken ct)
    {
        Etags.Add(etag);
        if (Gate is not null)
        {
            await Gate();
        }

        return Answers.Count > 0 ? Answers.Dequeue() : Default;
    }

    public Task<ReleaseCheck> GetByVersionAsync(ReleaseVersion version, CancellationToken ct)
    {
        ByVersionAsked.Add(version);
        return Task.FromResult(ByVersion?.Invoke(version) ?? Default);
    }
}
