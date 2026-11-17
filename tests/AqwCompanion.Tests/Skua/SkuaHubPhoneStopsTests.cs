using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaHubPhoneStopsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly SkuaHubTests.FakeStores _stores = new();
    private readonly PhoneStops _stops;
    private readonly SkuaHub _hub;

    public SkuaHubPhoneStopsTests()
    {
        _stops = new PhoneStops(() => T0);
        _hub = new SkuaHub(_stores, () => T0, (_, ct) => new TaskCompletionSource().Task.WaitAsync(ct), stops: _stops);
        var attachment = _hub.Attach(new FakeConnection()) ?? throw new InvalidOperationException("refused");
        _hub.Accept(attachment, new HelloMessage(new SkuaHello("0.5.0", "1.4.5.0", "id-a")));
    }

    [Fact]
    public void A_stop_command_is_noted_for_its_instance()
    {
        _ = _hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None);

        Assert.True(_stops.Within("id-a", T0, Window));
        Assert.False(_stops.Within("id-b", T0, Window));
    }

    [Fact]
    public void A_start_command_is_noted_too_because_the_plugin_ends_the_old_script_for_it()
    {
        _ = _hub.SendCommandAsync("id-a", new StartCommand("", "0123456789abcdef"), CancellationToken.None);

        Assert.True(_stops.Within("id-a", T0, Window));
    }

    [Fact]
    public async Task A_start_that_could_not_be_sent_is_not_noted()
    {
        var hub = new SkuaHub(_stores, () => T0, (_, ct) => new TaskCompletionSource().Task.WaitAsync(ct), stops: _stops);
        var attachment = hub.Attach(new FakeConnection { Fail = true })!;
        hub.Accept(attachment, new HelloMessage(new SkuaHello("0.5.0", "1.4.5.0", "id-y")));

        Assert.Null(await hub.SendCommandAsync("id-y", new StartCommand("", "0123456789abcdef"), CancellationToken.None));

        Assert.False(_stops.Within("id-y", T0, Window));
    }

    [Fact]
    public async Task Other_commands_and_a_disconnected_instance_are_not_noted()
    {
        _ = _hub.SendCommandAsync("id-a", new ListScriptsCommand(""), CancellationToken.None);
        _ = _hub.SendCommandAsync("id-a", new GetOptionsCommand("", "abc"), CancellationToken.None);
        Assert.False(_stops.Within("id-a", T0, Window));

        _hub.Forget();
        var attachment = _hub.Attach(new FakeConnection())!;
        _hub.Accept(attachment, new HelloMessage(new SkuaHello("0.5.0", "1.4.5.0", "id-a")));
        attachment.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None));

        Assert.False(_stops.Within("id-a", T0, Window));
    }

    [Fact]
    public async Task A_stop_that_could_not_be_sent_is_not_noted()
    {
        var hub = new SkuaHub(_stores, () => T0, (_, ct) => new TaskCompletionSource().Task.WaitAsync(ct), stops: _stops);
        var attachment = hub.Attach(new FakeConnection { Fail = true })!;
        hub.Accept(attachment, new HelloMessage(new SkuaHello("0.5.0", "1.4.5.0", "id-z")));

        Assert.Null(await hub.SendCommandAsync("id-z", new StopCommand(""), CancellationToken.None));

        Assert.False(_stops.Within("id-z", T0, Window));
    }

    [Fact]
    public void Forget_clears_the_noted_stops()
    {
        _ = _hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None);

        _hub.Forget();

        Assert.False(_stops.Within("id-a", T0, Window));
    }

    private sealed class FakeConnection : ISkuaConnection
    {
        public bool Fail { get; init; }

        public Task SendAsync(SkuaMessage message, CancellationToken ct) => Fail ? Task.FromException(new IOException("broken")) : Task.CompletedTask;
    }
}
