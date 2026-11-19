using AqwCompanion.App.Boosts;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Boosts;

public sealed class SkuaBoostServiceHubTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static FeedSnapshot Snapshot() =>
        new(1, new FeedCapture("Listening.", true), "Alpha", [new FeedCharacter("Alpha", 60, true, T0, T0, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null)], [], null);

    private static SkuaHubView World(int gold, int cls = 0) =>
        new([new SkuaInstanceView(
            "a1", "Hero", SkuaConnection.Connected, new SkuaHello("0.9.0", "1.4.5.0", "a1"),
            new SkuaStatus(true, "Hero", "Artix", "map", null, false, null, [], [], "idle", Boosts: new SkuaBoosts(gold, cls, 0, 0, T0.ToUnixTimeMilliseconds())), [], false)]);

    private static (PhoneHub Hub, SkuaBoostService Service, FakeSkua Skua) Build()
    {
        var skua = new FakeSkua();
        PhoneHub? hub = null;
        var service = new SkuaBoostService(skua, skua, () => T0, n => hub!.Announce(n), "0.9.0");
        hub = new PhoneHub(new FakeFeedSource(Snapshot()), new NoticeEngine(), new NoticeLog(50), () => T0, skua: skua, boosts: service);
        return (hub, service, skua);
    }

    [Fact]
    public async Task Concurrent_feed_changes_hub_reads_and_service_reads_never_deadlock()
    {
        var (hub, service, skua) = Build();
        using var hubScope = hub;
        using var serviceScope = service;
        service.Start();
        skua.Current = World(1000);

        var tasks = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                skua.Current = World(1000 + t * 1000 + i, i % 3 == 0 ? 600 : 0);
                skua.Raise();
                _ = hub.Current;
                _ = service.Views;
                _ = service.Find("a1");
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        skua.Current = World(777);
        skua.Raise();
        service.Pump();

        Assert.Contains("\"gold\":777", hub.CurrentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_boost_ending_while_the_hub_is_read_never_deadlocks_and_announces_once()
    {
        var (hub, service, skua) = Build();
        using var hubScope = hub;
        using var serviceScope = service;
        using var phone = hub.Subscribe();
        skua.Current = World(900);
        service.Pump();

        var readers = Enumerable.Range(0, 3).Select(reader => Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                _ = hub.Current;
                _ = service.Views;
            }
        })).ToArray();
        skua.Current = World(0);
        var pump = Task.Run(service.Pump);

        await Task.WhenAll([.. readers, pump]).WaitAsync(TimeSpan.FromSeconds(10));

        var notices = 0;
        while (phone.Reader.TryRead(out var message))
        {
            notices += message.Event == "notice" ? 1 : 0;
        }

        Assert.Equal(1, notices);
    }

    [Fact]
    public void The_hub_state_follows_a_boost_change()
    {
        var (hub, service, skua) = Build();
        using var hubScope = hub;
        using var serviceScope = service;
        using var phone = hub.Subscribe();
        while (phone.Reader.TryRead(out _))
        {
        }

        skua.Current = World(840);
        service.Pump();

        var states = new List<HubMessage>();
        while (phone.Reader.TryRead(out var message))
        {
            states.Add(message);
        }

        var state = Assert.Single(states, m => m.Event == "state");
        Assert.Contains("\"gold\":840", state.Json, StringComparison.Ordinal);
    }
}
