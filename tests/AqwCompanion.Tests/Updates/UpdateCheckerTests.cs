using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Updates;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class UpdateCheckerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public FakeGitHubReleases Releases { get; } = new();
        public bool Enabled { get; set; } = true;
        public string Announced { get; set; } = string.Empty;
        public List<Notice> Notices { get; } = [];
        public List<Exception> Errors { get; } = [];
        public DateTimeOffset Now { get; set; } = T0;

        public UpdateChecker Checker(string app = "0.7.0", IDelay? delay = null, bool announce = true, Action<Notice>? announcer = null) =>
            new(Releases, app, () => Enabled, delay ?? new FakeDelay(), () => Now, () => Announced, v => Announced = v,
                announce ? announcer ?? Notices.Add : null, Errors.Add);
    }

    private sealed class LimitedDelay(int free) : IDelay
    {
        private int _calls;

        public List<TimeSpan> Delays { get; } = [];

        public async Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            lock (Delays)
            {
                Delays.Add(delay);
            }

            if (Interlocked.Increment(ref _calls) > free)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
    }

    [Fact]
    public void A_new_checker_has_not_checked_and_asks_nothing()
    {
        var rig = new Rig();

        var checker = rig.Checker();

        Assert.Equal(UpdateState.Initial, checker.Current);
        Assert.Empty(rig.Releases.Etags);
    }

    [Fact]
    public async Task With_the_switch_off_no_request_is_made_and_the_state_is_off()
    {
        var rig = new Rig { Enabled = false };
        var checker = rig.Checker();

        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.Off, checker.Current.Status);
        Assert.Empty(rig.Releases.Etags);
    }

    [Fact]
    public async Task A_newer_release_is_available_and_announced_once_per_version()
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found("0.8.0");
        var checker = rig.Checker();

        await checker.CheckNowAsync();
        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.Available, checker.Current.Status);
        Assert.Equal(new ReleaseVersion(0, 8, 0), checker.Current.Available!.Version);
        Assert.Equal(new ReleaseVersion(0, 8, 0), checker.Current.Latest);
        Assert.Equal(T0, checker.Current.LastCheckUtc);
        var notice = Assert.Single(rig.Notices);
        Assert.Equal("update:0.8.0", notice.Id);
        Assert.Equal(NoticeKind.UpdateAvailable, notice.Kind);
        Assert.Equal("0.8.0", rig.Announced);
    }

    [Fact]
    public async Task A_version_announced_before_a_restart_is_not_announced_again()
    {
        var rig = new Rig { Announced = "0.8.0" };
        rig.Releases.Default = FakeGitHubReleases.Found("0.8.0");

        await rig.Checker().CheckNowAsync();

        Assert.Empty(rig.Notices);
    }

    [Fact]
    public async Task A_still_newer_release_is_announced_again()
    {
        var rig = new Rig { Announced = "0.8.0" };
        rig.Releases.Default = FakeGitHubReleases.Found("0.9.0");

        await rig.Checker().CheckNowAsync();

        Assert.Equal("update:0.9.0", Assert.Single(rig.Notices).Id);
        Assert.Equal("0.9.0", rig.Announced);
    }

    [Theory]
    [InlineData("0.7.0", "0.7.0")]
    [InlineData("0.7.0", "0.6.9")]
    [InlineData("0.7.0+abcdef", "0.7.0")]
    [InlineData("0.8.0-dev.3+abcdef", "0.8.0")]
    public async Task The_same_an_older_or_the_numerically_equal_release_is_never_offered(string app, string released)
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found(released);
        var checker = rig.Checker(app);

        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.UpToDate, checker.Current.Status);
        Assert.Null(checker.Current.Available);
        Assert.Equal(ReleaseVersion.TryParseTag("v" + released, out var v) ? v : default, checker.Current.Latest);
        Assert.Empty(rig.Notices);
    }

    [Fact]
    public async Task An_unreadable_app_version_never_offers_anything()
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found("0.8.0");
        var checker = rig.Checker("garbage");

        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.UpToDate, checker.Current.Status);
        Assert.Empty(rig.Notices);
    }

    [Fact]
    public async Task The_etag_of_the_last_answer_is_sent_and_a_304_keeps_the_known_update()
    {
        var rig = new Rig();
        rig.Releases.Answers.Enqueue(FakeGitHubReleases.Found("0.8.0", "\"first\""));
        rig.Releases.Answers.Enqueue(new ReleaseCheck(ReleaseCheckStatus.NotModified));
        var checker = rig.Checker();

        await checker.CheckNowAsync();
        rig.Now = T0.AddHours(6);
        await checker.CheckNowAsync();

        Assert.Equal([null, "\"first\""], rig.Releases.Etags);
        Assert.Equal(UpdateStatus.Available, checker.Current.Status);
        Assert.Equal(T0.AddHours(6), checker.Current.LastCheckUtc);
        Assert.Single(rig.Notices);
    }

    [Fact]
    public async Task A_failed_check_is_quiet_keeps_a_known_update_and_is_retried_later()
    {
        var rig = new Rig();
        rig.Releases.Answers.Enqueue(FakeGitHubReleases.Found("0.8.0"));
        rig.Releases.Answers.Enqueue(new ReleaseCheck(ReleaseCheckStatus.Failed));
        var checker = rig.Checker();

        await checker.CheckNowAsync();
        rig.Now = T0.AddHours(6);
        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.CouldNotCheck, checker.Current.Status);
        Assert.NotNull(checker.Current.Available);
        Assert.Equal(T0.AddHours(6), checker.Current.LastCheckUtc);
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public async Task A_release_that_is_not_ready_is_up_to_date_and_clears_an_earlier_offer()
    {
        var rig = new Rig();
        rig.Releases.Answers.Enqueue(FakeGitHubReleases.Found("0.8.0"));
        rig.Releases.Answers.Enqueue(new ReleaseCheck(ReleaseCheckStatus.NotReady, null, "\"n\""));
        var checker = rig.Checker();

        await checker.CheckNowAsync();
        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.UpToDate, checker.Current.Status);
        Assert.Null(checker.Current.Available);
        Assert.Null(checker.Current.Latest);
    }

    [Fact]
    public async Task A_check_that_throws_is_reported_and_shown_as_could_not_check()
    {
        var rig = new Rig();
        rig.Releases.Gate = () => throw new InvalidOperationException("boom");
        var checker = rig.Checker();

        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.CouldNotCheck, checker.Current.Status);
        Assert.Single(rig.Errors);
    }

    [Fact]
    public async Task A_second_check_while_one_runs_returns_at_once()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource();
        rig.Releases.Gate = () => gate.Task;
        rig.Releases.Default = FakeGitHubReleases.Found("0.8.0");
        var checker = rig.Checker();

        var first = checker.CheckNowAsync();
        await checker.CheckNowAsync();
        Assert.Equal(UpdateStatus.Checking, checker.Current.Status);
        gate.SetResult();
        await first;

        Assert.Single(rig.Releases.Etags);
        Assert.Equal(UpdateStatus.Available, checker.Current.Status);
    }

    [Fact]
    public async Task A_failing_announcer_is_reported_the_state_is_set_and_the_announcement_is_retried()
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found("0.8.0");
        var calls = 0;
        var checker = rig.Checker(announcer: _ =>
        {
            calls++;
            throw new InvalidOperationException("hub");
        });

        await checker.CheckNowAsync();
        await checker.CheckNowAsync();

        Assert.Equal(UpdateStatus.Available, checker.Current.Status);
        Assert.Equal(2, calls);
        Assert.Equal(2, rig.Errors.Count);
        Assert.Equal(string.Empty, rig.Announced);
    }

    [Fact]
    public async Task Turning_the_switch_off_clears_the_state_without_a_request_and_on_checks_at_once()
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found("0.8.0");
        var checker = rig.Checker();
        await checker.CheckNowAsync();

        rig.Enabled = false;
        await checker.ReconfigureAsync();

        Assert.Equal(UpdateStatus.Off, checker.Current.Status);
        Assert.Null(checker.Current.Available);
        Assert.Single(rig.Releases.Etags);

        rig.Enabled = true;
        await checker.ReconfigureAsync();

        Assert.Equal(UpdateStatus.Available, checker.Current.Status);
        Assert.Equal([null, null], rig.Releases.Etags);
    }

    [Fact]
    public async Task Changed_is_raised_for_each_state_change()
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found("0.7.0");
        var checker = rig.Checker();
        var seen = new List<UpdateStatus>();
        checker.Changed += (_, _) => seen.Add(checker.Current.Status);

        await checker.CheckNowAsync();

        Assert.Equal([UpdateStatus.Checking, UpdateStatus.UpToDate], seen);
    }

    [Fact]
    public void The_timing_is_30_seconds_then_every_6_hours()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), UpdateChecker.FirstCheckDelay);
        Assert.Equal(TimeSpan.FromHours(6), UpdateChecker.Interval);
    }

    [Fact]
    public void Start_checks_after_30_seconds_and_then_every_6_hours_until_disposed()
    {
        var rig = new Rig();
        rig.Releases.Default = FakeGitHubReleases.Found("0.7.0");
        var delay = new LimitedDelay(free: 2);
        var checker = rig.Checker(delay: delay);

        checker.Start();
        checker.Start();

        Assert.True(SpinWait.SpinUntil(() => delay.Delays.Count >= 3, TimeSpan.FromSeconds(10)));
        Assert.Equal([UpdateChecker.FirstCheckDelay, UpdateChecker.Interval, UpdateChecker.Interval], delay.Delays.ToArray());
        Assert.Equal(2, rig.Releases.Etags.Count);
        checker.Dispose();
    }
}
