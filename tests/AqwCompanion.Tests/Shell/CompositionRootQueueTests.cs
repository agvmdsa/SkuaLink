using AqwCompanion.App;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Phone;
using AqwCompanion.App.Queue;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class CompositionRootQueueTests
{
    private static AppPaths TempPaths() => new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    private static void Cleanup(AppPaths paths)
    {
        if (Directory.Exists(paths.Root))
        {
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    [Fact]
    public void The_queue_service_is_shared_with_the_seam()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        Assert.Same(services.GetRequiredService<SkuaQueueService>(), services.GetRequiredService<ISkuaQueues>());
        Assert.IsType<QueueFileStore>(services.GetRequiredService<IQueueStore>());
    }

    [Fact]
    public void Building_the_container_creates_no_queue_file_and_loads_nothing()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        Assert.NotNull(services.GetRequiredService<SkuaQueueService>());
        Assert.NotNull(services.GetRequiredService<PhoneHub>());
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void The_queue_service_forgets_before_the_character_book_the_skua_hub_and_the_phone_controller()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        var participants = CompositionRoot.ForgetParticipants(services).ToList();
        var queue = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<SkuaQueueService>()));
        var book = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<CharacterBook>()));
        var hub = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<SkuaHub>()));
        Assert.True(queue >= 0);
        const string why = "a book or hub Forget raises Changed and would pause the queues and announce notices";
        Assert.True(queue < book, why);
        Assert.True(queue < hub, why);
        Assert.True(book < hub);
        Assert.Same(services.GetRequiredService<PhoneController>(), participants[^1]);
    }

    [Fact]
    public void The_phone_hub_and_the_phone_api_were_built_with_the_queue_feed()
    {
        var paths = TempPaths();
        try
        {
            using var services = CompositionRoot.Build(paths);
            var hub = services.GetRequiredService<PhoneHub>();
            Assert.NotNull(services.GetRequiredService<SkuaApi>());

            var result = services.GetRequiredService<SkuaQueueService>().Replace(
                "Fictional Hero", [new QueueStep("00000000000000000000000000000001", "0123456789abcdef", "Farm", "Blood Orb", 5)]);

            Assert.True(result.Ok);
            Assert.Contains("\"queues\":[{", hub.CurrentJson, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(paths);
        }
    }

    [Fact]
    public void Forgetting_removes_the_queue_files()
    {
        var paths = TempPaths();
        try
        {
            using var services = CompositionRoot.Build(paths);
            Directory.CreateDirectory(paths.Root);
            File.WriteAllText(paths.SettingsPath, "{}");
            var service = services.GetRequiredService<SkuaQueueService>();
            Assert.True(service.Replace(
                "Fictional Hero", [new QueueStep("00000000000000000000000000000001", "0123456789abcdef", "Farm", "Blood Orb", 5)]).Ok);
            Assert.NotEmpty(Directory.GetFiles(paths.Root, "skua-queue.*"));

            var participants = CompositionRoot.ForgetParticipants(services).Where(p => p is not PhoneController).ToList();
            foreach (var p in participants)
            {
                p.Quiesce();
            }

            foreach (var p in participants)
            {
                p.Forget();
            }

            Assert.Empty(Directory.GetFiles(paths.Root, "skua-queue.*"));
            Assert.True(File.Exists(paths.SettingsPath));
        }
        finally
        {
            Cleanup(paths);
        }
    }

    [Fact]
    public void The_queue_card_pieces_resolve_and_the_main_view_model_carries_the_queue()
    {
        var paths = TempPaths();
        using var services = CompositionRoot.Build(paths);

        Assert.IsType<SkuaQueueScripts>(services.GetRequiredService<IQueueScriptSource>());
        Assert.IsType<AvaloniaQueueEditorDialog>(services.GetRequiredService<IQueueEditorDialog>());
        var queue = services.GetRequiredService<QueueViewModel>();
        Assert.Same(queue, services.GetRequiredService<MainViewModel>().Queue);
        Assert.False(Directory.Exists(paths.Root));
    }
}
