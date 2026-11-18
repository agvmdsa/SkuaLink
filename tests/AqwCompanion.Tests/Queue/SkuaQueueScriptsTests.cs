using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class SkuaQueueScriptsTests
{
    private static SkuaInstanceView Named(string id, string character, bool disconnected = false) =>
        FakeSkua.Instance(id, character, disconnected, Status() with { Character = character });

    private static (FakeSkua Skua, SkuaQueueScripts Source) Rig(params SkuaInstanceView[] instances)
    {
        var skua = new FakeSkua { Current = new SkuaHubView(instances) };
        skua.AnswerWith(new ResultMessage("", true, null, Scripts, null, null, false, false));
        return (skua, new SkuaQueueScripts(skua, skua));
    }

    [Fact]
    public async Task The_list_comes_from_the_connected_instance_of_the_character_ignoring_case()
    {
        var (skua, source) = Rig(Named("x", "Other"), Named("i1", "hero"));

        var list = await source.ListAsync("HERO", CancellationToken.None);

        Assert.Equal(Scripts, list);
        var sent = Assert.Single(skua.Sent);
        Assert.Equal("i1", sent.InstanceId);
        Assert.IsType<ListScriptsCommand>(sent.Command);
    }

    [Fact]
    public async Task No_connected_instance_of_the_character_gives_null_and_sends_nothing()
    {
        var (skua, source) = Rig(Named("x", "Other"), Named("i1", "Hero", disconnected: true));

        Assert.Null(await source.ListAsync("Hero", CancellationToken.None));
        Assert.Empty(skua.Sent);
    }

    [Fact]
    public async Task No_answer_a_refusal_and_a_vanished_instance_give_null()
    {
        var (skua, source) = Rig(Named("i1", "Hero"));

        skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(null);
        Assert.Null(await source.ListAsync("Hero", CancellationToken.None));

        skua.AnswerWith(new ResultMessage("", false, "nope", null, null, null, false, false));
        Assert.Null(await source.ListAsync("Hero", CancellationToken.None));

        skua.Answer = (_, _) => throw new KeyNotFoundException();
        Assert.Null(await source.ListAsync("Hero", CancellationToken.None));

        skua.Answer = (_, _) => throw new InvalidOperationException();
        Assert.Null(await source.ListAsync("Hero", CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_token_gives_null()
    {
        var (skua, source) = Rig(Named("i1", "Hero"));
        skua.Answer = (_, _) => throw new OperationCanceledException();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.Null(await source.ListAsync("Hero", cts.Token));
    }
}
