using System.Text.Json;
using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Queue;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public sealed class QueueFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "qk-queue-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private QueueFileStore Store() => new(_folder);

    private string PathOf(string character) => Path.Combine(_folder, SkuaQueueFiles.FileName(character));

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, name), text);
    }

    private static string Json(string character, string steps, int version = 1, int cursor = 0, bool finished = false) =>
        $$"""{"version":{{version}},"character":"{{character}}","cursor":{{cursor}},"finished":{{finished.ToString().ToLowerInvariant()}},"steps":{{steps}}}""";

    private static string StepJson(string id = "00000000000000000000000000000001", string scriptId = A, string name = "Dailies", string item = "Blood Orb") =>
        $$"""{"id":"{{id}}","scriptId":"{{scriptId}}","scriptName":"{{name}}","goalItem":"{{item}}","goalQuantity":5}""";

    [Fact]
    public void Save_then_load_round_trips_every_field()
    {
        var steps = new[]
        {
            Step("1", rep: new ReputationGoal("Yokai", 5), minutes: 30),
            Step("2", B, "Butler", "Gold Voucher", 7),
        };
        var store = Store();

        store.Save(new StoredQueue("Hero", steps, 1, false));
        store.Save(new StoredQueue("Mara", [steps[1]], 1, true));

        var loaded = Store().LoadAll().OrderBy(q => q.Character).ToList();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Hero", loaded[0].Character);
        Assert.Equal(steps, loaded[0].Steps);
        Assert.Equal(1, loaded[0].Cursor);
        Assert.False(loaded[0].Finished);
        Assert.True(loaded[1].Finished);
        Assert.Equal(1, loaded[1].Cursor);
    }

    [Fact]
    public void There_is_one_file_per_character_named_by_the_queue_file_rule_and_saving_twice_overwrites()
    {
        var store = Store();
        store.Save(new StoredQueue("Hero", [Step()], 0, false));
        store.Save(new StoredQueue("Hero", [Step(), Step("2")], 1, false));

        Assert.Equal([SkuaQueueFiles.FileName("Hero")], Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)!).ToArray());
        var loaded = Assert.Single(Store().LoadAll());
        Assert.Equal(2, loaded.Steps.Count);
        Assert.Equal(1, loaded.Cursor);
    }

    [Fact]
    public void Damaged_files_are_ignored_and_the_other_characters_still_load()
    {
        Store().Save(new StoredQueue("Hero", [Step()], 0, false));
        Write("skua-queue.corrupt.json", "{ not json");
        Write("skua-queue.version.json", Json("Old", "[" + StepJson() + "]", version: 2));
        Write("skua-queue.array.json", "[1,2,3]");
        Write("skua-queue.empty.json", string.Empty);
        Write("skua-queue.noname.json", Json("", "[" + StepJson() + "]"));

        var loaded = Assert.Single(Store().LoadAll());
        Assert.Equal("Hero", loaded.Character);
    }

    [Fact]
    public void Steps_are_validated_again_on_load()
    {
        var many = string.Join(",", Enumerable.Range(1, 25).Select(n => StepJson(id: n.ToString("x32"))));
        Write("skua-queue.hero.json", Json("Hero", "[" + many + "]"));
        Write("skua-queue.mara.json", Json("Mara", "[" + string.Join(",", StepJson(scriptId: "zzzz"), StepJson(id: "00000000000000000000000000000002", item: "  "),
            StepJson(id: "00000000000000000000000000000003", name: "Da\\u0001ilies", item: " Blood Orb ")) + ",null]", cursor: 9));

        var loaded = Store().LoadAll().ToDictionary(q => q.Character);
        Assert.Equal(QueueLimits.MaxSteps, loaded["Hero"].Steps.Count);
        var mara = Assert.Single(loaded["Mara"].Steps);
        Assert.Equal("Dailies", mara.ScriptName);
        Assert.Equal("Blood Orb", mara.GoalItem);
        Assert.Equal(1, loaded["Mara"].Cursor);
    }

    [Fact]
    public void A_file_is_loaded_by_its_content_whatever_its_name()
    {
        Write("skua-queue.whatever.json", Json("Hero", "[" + StepJson() + "]"));

        Assert.Equal("Hero", Assert.Single(Store().LoadAll()).Character);
    }

    [Fact]
    public void A_finished_flag_without_steps_is_not_finished()
    {
        Write("skua-queue.hero.json", Json("Hero", "[]", finished: true));

        Assert.False(Assert.Single(Store().LoadAll()).Finished);
    }

    [Fact]
    public void A_missing_folder_loads_empty_and_saving_creates_it()
    {
        Assert.Empty(Store().LoadAll());
        Assert.False(Directory.Exists(_folder));

        Store().Save(new StoredQueue("Hero", [Step()], 0, false));

        Assert.True(File.Exists(PathOf("Hero")));
    }

    [Fact]
    public void Delete_all_removes_only_the_queue_files_and_their_temp_twins()
    {
        Store().Save(new StoredQueue("Hero", [Step()], 0, false));
        Write("skua-queue.hero.json.tmp", "x");
        Write("skua-log.hero.json", "x");
        Write("settings.json", "x");
        Directory.CreateDirectory(Path.Combine(_folder, "skua-queue.sub.json"));

        Store().DeleteAll();

        var left = Directory.GetFileSystemEntries(_folder).Select(f => Path.GetFileName(f)!).Order().ToArray();
        Assert.Equal(["settings.json", "skua-log.hero.json", "skua-queue.sub.json"], left);
    }

    [Fact]
    public void Delete_all_on_a_missing_folder_does_not_throw()
    {
        Store().DeleteAll();
    }

    [Fact]
    public void Saving_to_an_unwritable_target_never_throws()
    {
        // A directory where the file should go cannot be replaced by a file.
        Directory.CreateDirectory(PathOf("Hero"));

        Store().Save(new StoredQueue("Hero", [Step()], 0, false));

        Assert.True(Directory.Exists(PathOf("Hero")));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void The_file_holds_steps_and_numbers_only()
    {
        Store().Save(new StoredQueue("Hero", [Step(rep: new ReputationGoal("Yokai", 5), minutes: 10)], 0, false));

        using var doc = JsonDocument.Parse(File.ReadAllText(PathOf("Hero")));
        var root = doc.RootElement;
        Assert.Equal(["version", "character", "cursor", "finished", "steps"], root.EnumerateObject().Select(p => p.Name).ToArray());
        var text = root.GetRawText();
        Assert.DoesNotContain(_folder, text);
        Assert.DoesNotContain("/", text);
        Assert.Equal(1, root.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public void Two_files_for_one_character_keep_the_first_by_name()
    {
        Write("skua-queue.a.json", Json("Hero", "[" + StepJson(item: "First") + "]"));
        Write("skua-queue.b.json", Json("hero", "[" + StepJson(item: "Second") + "]"));

        var loaded = Assert.Single(Store().LoadAll());
        Assert.Equal("First", loaded.Steps[0].GoalItem);
    }

    [Fact]
    public void A_file_over_64_kb_is_skipped_like_a_corrupt_one()
    {
        Store().Save(new StoredQueue("Hero", [Step()], 0, false));
        Write("skua-queue.big.json", Json("Big", "[" + StepJson() + "]") + new string(' ', 70_000));

        Assert.Equal("Hero", Assert.Single(Store().LoadAll()).Character);
    }
}
