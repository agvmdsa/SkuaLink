using AqwCompanion.App;
using AqwCompanion.App.Push;
using AqwCompanion.Phone.Push;

namespace AqwCompanion.Tests.Push;

public sealed class PushFileStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_loads_empty()
    {
        Assert.Equal(PushFile.Empty, new PushFileStore(_paths.PushPath).Load());
    }

    [Fact]
    public void A_saved_file_round_trips()
    {
        var t = new DateTimeOffset(2026, 11, 16, 3, 4, 5, TimeSpan.Zero);
        var file = new PushFile([new PushSubscription("d1", "https://fcm.googleapis.com/fcm/send/a", "P", "A", ["RankUp"], t, t.AddMinutes(3))]);
        new PushFileStore(_paths.PushPath).Save(file);

        var loaded = new PushFileStore(_paths.PushPath).Load();

        var sub = Assert.Single(loaded.Subscriptions);
        Assert.Equal(file.Subscriptions[0].DeviceId, sub.DeviceId);
        Assert.Equal(file.Subscriptions[0].Endpoint, sub.Endpoint);
        Assert.Equal(["RankUp"], sub.Kinds);
        Assert.Equal(t.AddMinutes(3), sub.LastSuccessUtc);
        Assert.Equal(["push.json"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{}")]
    public void A_damaged_file_loads_empty(string content)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.PushPath, content);

        Assert.Empty(new PushFileStore(_paths.PushPath).Load().Subscriptions);
    }

    [Fact]
    public void Delete_removes_the_file_and_the_temp_file_and_is_fine_when_gone()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.PushPath, "{}");
        File.WriteAllText(_paths.PushPath + ".tmp", "{}");
        var store = new PushFileStore(_paths.PushPath);

        store.Delete();

        Assert.Empty(Directory.GetFiles(_paths.Root));
        Assert.Null(Record.Exception(store.Delete));
    }

    [Fact]
    public void The_saved_json_holds_only_the_known_properties()
    {
        var t = new DateTimeOffset(2026, 11, 16, 3, 4, 5, TimeSpan.Zero);
        new PushFileStore(_paths.PushPath).Save(new PushFile([new PushSubscription("d1", "https://fcm.googleapis.com/x", "P", "A", ["RankUp"], t, null)]));

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_paths.PushPath));
        var names = doc.RootElement.GetProperty("subscriptions")[0].EnumerateObject().Select(p => p.Name).Order().ToList();

        Assert.Equal(["auth", "createdUtc", "deviceId", "endpoint", "kinds", "lastSuccessUtc", "p256dh"], names);
    }

    [Fact]
    public void Saving_and_deleting_never_throw_when_the_folder_cannot_be_written()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(Path.Combine(_paths.Root, "blocker"), "x");
        var store = new PushFileStore(Path.Combine(_paths.Root, "blocker", "push.json"));

        Assert.Null(Record.Exception(() => store.Save(PushFile.Empty)));
        Assert.Null(Record.Exception(store.Delete));
        Assert.Equal(PushFile.Empty, store.Load());
    }
}
