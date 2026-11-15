using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaProfileReadTests
{
    private static readonly SkuaProfileReading Reading = new(60, 340, 5000, 1200, 777, true, " Heroes ", 10);

    [Fact]
    public void A_reading_becomes_a_profile_with_the_class_and_slots()
    {
        var profile = SkuaProfileRead.Next(null, Reading, new SkuaClassFacts(31, "Void Highlord", 302500), new SkuaSlotFacts(140, 300, 50));
        Assert.Equal(new SkuaProfile(60, 340, 5000, 1200, 777, 10, true, "Void Highlord", 31, 302500, "Heroes", 140, 300, 50), profile);
    }

    [Fact]
    public void A_failed_reading_keeps_the_last_profile()
    {
        var last = new SkuaProfile(1, 2, 3, 4, 5, 6, false);
        Assert.Same(last, SkuaProfileRead.Next(last, null, null, null));
        Assert.Null(SkuaProfileRead.Next(null, null, null, null));
    }

    [Fact]
    public void Without_class_or_slots_those_parts_are_not_reported()
    {
        var p = SkuaProfileRead.Next(null, Reading, null, null)!;
        Assert.Null(p.ClassName);
        Assert.Null(p.ClassId);
        Assert.Null(p.ClassPoints);
        Assert.Null(p.BagSlots);
    }

    [Fact]
    public void Negative_numbers_become_zero_and_zero_slots_or_class_id_are_not_reported()
    {
        var p = SkuaProfileRead.Next(null, new SkuaProfileReading(-1, -2, -3, -4, -5, false, "  ", -6), new SkuaClassFacts(0, "X", -9), new SkuaSlotFacts(0, -1, 5))!;
        Assert.Equal(new SkuaProfile(0, 0, 0, 0, 0, 0, false, "X", null, 0, null, null, null, 5), p);
    }
}
