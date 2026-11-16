using AqwCompanion.App;
using AqwCompanion.App.Characters;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Forgetting;

/// <summary>Proves by effect, on a real folder and a real database file, what Forgetting leaves behind.</summary>
public sealed class ForgettingEffectTests : IDisposable
{
    private static readonly DateTimeOffset ReadAt = new(2026, 11, 6, 12, 30, 0, TimeSpan.Zero);

    private readonly AppPaths _paths =
        new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Forgetting_leaves_nothing_but_logs_and_exports()
    {
        var world = await BuildWorldAsync(new LiteDbStoredDataStore(_paths.DatabasePath), gateFails: false);
        Assert.Contains("data/companion.db", FilesUnderRoot());
        Assert.Contains("reputation/reputation.json", FilesUnderRoot());

        var result = world.Coordinator.Forget();

        Assert.True(result.IsComplete);
        Assert.Equal(["exports/y.zip", "logs/x.txt"], FilesUnderRoot());
        Assert.Empty(world.HardFarms.Rows);
        Assert.True(world.HardFarms.ShowEmptyState);
        Assert.Empty(world.Book.All);
    }

    [Fact]
    public async Task A_failed_stored_data_removal_leaves_every_file_in_place()
    {
        var store = new LiteDbStoredDataStore(_paths.DatabasePath);
        var world = await BuildWorldAsync(store, gateFails: true);
        var before = SnapshotFiles();

        var result = world.Coordinator.Forget();

        Assert.False(result.StoredDataRemoved);
        Assert.Equal(before.Keys, SnapshotFiles().Keys);
        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_paths.Root, path)));
        }

        Assert.Single(world.HardFarms.Rows);
        Assert.False(world.HardFarms.ShowEmptyState);
        Assert.Equal("Fictional Hero", Assert.Single(world.Book.All).Name);
        Assert.NotNull(store.Load<string>(StoredKeys.LegacyCaptureRecord, 1));
    }

    private async Task<World> BuildWorldAsync(LiteDbStoredDataStore store, bool gateFails)
    {
        var storedData = new StoredData(store, StoredKeys.Obsolete);

        var hardFarmStore = new HardFarmStore(new StoredData(store, StoredKeys.Obsolete));
        hardFarmStore.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), ReadAt);
        hardFarmStore.SaveIndex(new WikiIndex(WikiUrls.ClassesSlug, WikiType.Class, [new WikiIndexEntry("blade-one", "Blade One")]), ReadAt);

        // The folder an older version read the exported reputation file from.
        Directory.CreateDirectory(_paths.ReputationDirectory);
        await File.WriteAllTextAsync(Path.Combine(_paths.ReputationDirectory, "reputation.json"), "{}");
        Directory.CreateDirectory(_paths.LogDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_paths.LogDirectory, "x.txt"), [1, 2, 3]);
        Directory.CreateDirectory(_paths.ExportDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_paths.ExportDirectory, "y.zip"), [4, 5, 6]);

        var hardFarms = new HardFarmsViewModel(
            hardFarmStore, new FakeHardFarmSync(), ActiveInventoryBuilder.Empty(), new FakeUrlLauncher());

        // The record the removed capture of versions before 0.6 left in the database: never read, deleted with the file.
        store.Save(StoredKeys.LegacyCaptureRecord, 1, "old capture data", ReadAt);
        new StoredCharacterBookStore(new StoredData(store, StoredKeys.Obsolete)).Save(
            [new CharacterRecord("Fictional Hero", ReadAt, Factions: [new SkuaFaction(5, "Fictional Order", 3, 5000, 1400, 6400)], FactionsAtUtc: ReadAt)]);
        var book = new CharacterBook(new StoredCharacterBookStore(new StoredData(store, StoredKeys.Obsolete)), () => ReadAt);
        book.Restore();
        Assert.Single(book.All);

        var coordinator = new ForgetCoordinator(
            () => !gateFails && storedData.Clear(),
            () => [hardFarms, new LegacyReputationFolder(_paths), book]);

        await hardFarms.RestoreAsync();
        Assert.Single(hardFarms.Rows);
        return new World(hardFarms, coordinator, book);
    }

    private List<string> FilesUnderRoot() =>
        Directory.EnumerateFiles(_paths.Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_paths.Root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    private Dictionary<string, byte[]> SnapshotFiles() =>
        FilesUnderRoot().ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(_paths.Root, f)));

    private sealed record World(HardFarmsViewModel HardFarms, ForgetCoordinator Coordinator, CharacterBook Book);
}
