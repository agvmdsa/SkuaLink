using AqwCompanion.App;
using AqwCompanion.App.Boosts;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Phone;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using AqwCompanion.Skua;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class CompositionRootBoostTests
{
    private static AppPaths TempPaths() => new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    private static void Cleanup(AppPaths paths)
    {
        if (Directory.Exists(paths.Root))
        {
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    private sealed class NullConnection : ISkuaConnection
    {
        public Task SendAsync(SkuaMessage message, CancellationToken ct) => Task.CompletedTask;
    }

    private static void Feed(SkuaHub hub, IDisposable attachment, int gold) =>
        hub.Accept(attachment, new StatusMessage(new SkuaStatus(
            true, "Hero", "Artix", "map", null, false, null, [], [], "idle",
            Boosts: new SkuaBoosts(gold, 0, 0, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))));

    private static IDisposable Connect(SkuaHub hub)
    {
        var attachment = hub.Attach(new NullConnection()) ?? throw new InvalidOperationException("refused");
        hub.Accept(attachment, new HelloMessage(new SkuaHello("0.9.0", "1.4.5.0", "a1")));
        return attachment;
    }

    [Fact]
    public void The_boost_service_is_one_singleton_behind_the_seam()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        Assert.Same(services.GetRequiredService<SkuaBoostService>(), services.GetRequiredService<ISkuaBoosts>());
    }

    [Fact]
    public void Building_the_container_and_resolving_the_service_creates_nothing_under_the_data_folder()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        Assert.NotNull(services.GetRequiredService<SkuaBoostService>());
        Assert.NotNull(services.GetRequiredService<PhoneHub>());
        Assert.NotNull(services.GetRequiredService<SkuaApi>());
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void The_service_is_not_a_forget_participant_and_the_phone_controller_is_still_the_last()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        var participants = CompositionRoot.ForgetParticipants(services).ToList();

        Assert.DoesNotContain(participants, p => ReferenceEquals(p, services.GetRequiredService<SkuaBoostService>()));
        Assert.Same(services.GetRequiredService<PhoneController>(), participants[^1]);
    }

    [Fact]
    public void The_hub_state_has_a_boosts_field_from_the_real_service()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        Assert.Contains("\"boosts\":[]", services.GetRequiredService<PhoneHub>().CurrentJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Forget_of_the_hub_drops_the_boosts()
    {
        var paths = TempPaths();
        try
        {
            using var services = CompositionRoot.Build(paths);
            var skuaHub = services.GetRequiredService<SkuaHub>();
            var phone = services.GetRequiredService<PhoneHub>();
            var service = services.GetRequiredService<SkuaBoostService>();
            var attachment = Connect(skuaHub);
            service.Start();
            Feed(skuaHub, attachment, 840);
            service.Pump();
            Assert.NotEmpty(service.Views);
            Assert.DoesNotContain("\"boosts\":[]", phone.CurrentJson, StringComparison.Ordinal);

            var coordinator = services.GetRequiredService<ForgetCoordinator>();
            coordinator.Forget();
            service.Pump();

            Assert.Empty(service.Views);
            Assert.Contains("\"boosts\":[]", phone.CurrentJson, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(paths);
        }
    }

    [Fact]
    public void A_boost_ended_notice_reaches_the_hub_and_the_push_notifier()
    {
        var paths = TempPaths();
        try
        {
            using var services = CompositionRoot.Build(paths);
            var skuaHub = services.GetRequiredService<SkuaHub>();
            var phone = services.GetRequiredService<PhoneHub>();
            var service = services.GetRequiredService<SkuaBoostService>();
            var attachment = Connect(skuaHub);
            service.Start();
            Feed(skuaHub, attachment, 840);
            service.Pump();

            Feed(skuaHub, attachment, 0);
            service.Pump();

            Assert.Contains("BoostEnded", phone.CurrentJson, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(paths);
        }
    }
}
