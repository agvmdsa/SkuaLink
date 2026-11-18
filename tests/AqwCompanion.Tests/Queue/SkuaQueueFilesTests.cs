using AqwCompanion.Phone.Queue;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Tests.Queue;

public class SkuaQueueFilesTests
{
    [Fact]
    public void The_file_name_uses_the_same_slug_as_the_skua_log_with_its_own_prefix()
    {
        Assert.Equal("skua-queue.hero.json", SkuaQueueFiles.FileName("Hero"));
        Assert.Equal(SkuaLogFiles.FileName("Mr Hero!").Replace("skua-log.", "skua-queue."), SkuaQueueFiles.FileName("Mr Hero!"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    public void The_file_name_is_always_a_bare_name(string character)
    {
        var name = SkuaQueueFiles.FileName(character);

        Assert.Equal(Path.GetFileName(name), name);
        Assert.DoesNotContain("..", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_names_that_clean_to_the_same_text_do_not_share_a_file()
    {
        Assert.NotEqual(SkuaQueueFiles.FileName("a b"), SkuaQueueFiles.FileName("a_b"));
    }

    [Theory]
    [InlineData("skua-queue.hero.json", true)]
    [InlineData("skua-queue.hero.json.tmp", true)]
    [InlineData("skua-log.hero.json", false)]
    [InlineData("skua-queue.hero.txt", false)]
    [InlineData("settings.json", false)]
    public void Only_queue_files_and_their_temp_twins_are_recognised(string name, bool queue)
    {
        Assert.Equal(queue, SkuaQueueFiles.IsQueueFile(name));
    }

    [Fact]
    public void The_log_file_names_did_not_change()
    {
        Assert.Equal("skua-log.hero.json", SkuaLogFiles.FileName("Hero"));
        Assert.Equal("hero", SkuaLogFiles.Slug("Hero"));
    }
}
