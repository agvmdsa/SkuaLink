using AqwCompanion.Phone.Boosts;

namespace AqwCompanion.Tests.Boosts;

public class BoostGuardsTests
{
    private static BoostView View(bool supported = true, bool online = true, int gold = 0, bool canRelogin = false, params BoostItemView[] items) =>
        new("a1", "Hero", supported, online, 1763481234567, gold, 0, 0, 0, items, canRelogin);

    private static readonly BoostItemView GoldItem = new(17439, "GOLD Boost! (1 hr)", "gold", 33, true);

    [Fact]
    public void Use_without_a_view_is_disconnected() => Assert.Equal(BoostTexts.Disconnected, BoostGuards.CheckUse(null, 1));

    [Fact]
    public void Use_on_an_unsupported_view_says_so() => Assert.Equal(BoostTexts.NotSupported, BoostGuards.CheckUse(View(supported: false, items: GoldItem), 17439));

    [Fact]
    public void Use_while_logged_out_is_refused() => Assert.Equal(BoostTexts.NotLoggedIn, BoostGuards.CheckUse(View(online: false, items: GoldItem), 17439));

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public void An_item_not_in_the_bag_or_with_no_quantity_is_refused(int quantity)
    {
        var view = View(items: GoldItem with { Quantity = quantity });

        Assert.Equal(BoostTexts.NotInBag, BoostGuards.CheckUse(view, quantity == 0 ? 17439 : 99));
    }

    [Fact]
    public void An_unknown_type_is_refused() =>
        Assert.Equal(BoostTexts.UnknownType, BoostGuards.CheckUse(View(items: GoldItem with { Type = "unknown" }), 17439));

    [Theory]
    [InlineData(600)]
    [InlineData(-1)]
    public void An_active_type_is_refused(int value) =>
        Assert.Equal(BoostTexts.AlreadyActive("gold"), BoostGuards.CheckUse(View(gold: value, items: GoldItem), 17439));

    [Fact]
    public void A_good_item_is_allowed() => Assert.Null(BoostGuards.CheckUse(View(items: GoldItem), 17439));

    [Fact]
    public void Relogin_without_a_view_is_disconnected() => Assert.Equal(BoostTexts.Disconnected, BoostGuards.CheckRelogin(null));

    [Fact]
    public void Relogin_on_an_unsupported_view_says_so() => Assert.Equal(BoostTexts.NotSupported, BoostGuards.CheckRelogin(View(supported: false, online: false)));

    [Fact]
    public void Relogin_while_logged_in_is_refused() => Assert.Equal(BoostTexts.AlreadyLoggedIn, BoostGuards.CheckRelogin(View(online: true, canRelogin: true)));

    [Fact]
    public void Relogin_without_a_plugin_connection_is_disconnected() => Assert.Equal(BoostTexts.Disconnected, BoostGuards.CheckRelogin(View(online: false, canRelogin: false)));

    [Fact]
    public void Relogin_when_possible_is_allowed() => Assert.Null(BoostGuards.CheckRelogin(View(online: false, canRelogin: true)));

    [Fact]
    public void Use_while_logged_out_with_the_item_missing_says_not_logged_in() =>
        Assert.Equal(BoostTexts.NotLoggedIn, BoostGuards.CheckUse(View(online: false, items: GoldItem), 99));

    [Fact]
    public void Relogin_on_an_unsupported_view_that_is_online_says_not_supported() =>
        Assert.Equal(BoostTexts.NotSupported, BoostGuards.CheckRelogin(View(supported: false, online: true, canRelogin: true)));
}
