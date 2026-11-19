using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushSubscriptionStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private const string Fcm = "https://fcm.googleapis.com/fcm/send/abc";

    private readonly MemoryPushFileStore _file = new();
    private DateTimeOffset _now = T0;

    private PushSubscriptionStore Create() => new(_file, () => _now);

    [Fact]
    public void Set_creates_a_subscription_with_all_kinds_and_writes_once()
    {
        var store = Create();

        var sub = store.Set("d1", Fcm, "P", "A", null);

        Assert.Equal(PushKinds.All, sub.Kinds);
        Assert.Equal(T0, sub.CreatedUtc);
        Assert.Null(sub.LastSuccessUtc);
        Assert.Same(sub, store.Get("d1"));
        Assert.Equal(1, _file.Saves);
    }

    [Fact]
    public void Set_without_kinds_keeps_the_kinds_the_device_already_had_and_with_kinds_replaces_them()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", ["RankUp"]);

        Assert.Equal(["RankUp"], store.Set("d1", Fcm + "2", "P2", "A2", null).Kinds);
        Assert.Equal(["ScriptError", "RankUp"], store.Set("d1", Fcm, "P", "A", ["RankUp", "ScriptError"]).Kinds);
    }

    [Fact]
    public void Another_device_has_its_own_subscription()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);
        store.Set("d2", Fcm + "x", "P", "A", ["NoProgress"]);

        Assert.Equal(2, store.All.Count);
        Assert.Equal(["NoProgress"], store.Get("d2")!.Kinds);
    }

    [Fact]
    public void SetKinds_changes_the_kinds_of_an_existing_subscription_only()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);

        Assert.True(store.SetKinds("d1", ["DyingOften"]));
        Assert.False(store.SetKinds("nobody", ["DyingOften"]));
        Assert.Equal(["DyingOften"], store.Get("d1")!.Kinds);
    }

    [Fact]
    public void Remove_and_RemoveDevices_delete_and_write_only_when_something_was_removed()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);
        store.Set("d2", Fcm + "x", "P", "A", null);
        var saves = _file.Saves;

        store.RemoveDevices(["zzz"]);
        Assert.Equal(saves, _file.Saves);
        store.RemoveDevices(["d1", "zzz"]);
        Assert.Null(store.Get("d1"));
        Assert.Equal(saves + 1, _file.Saves);
        Assert.True(store.Remove("d2"));
        Assert.False(store.Remove("d2"));
    }

    [Fact]
    public void RetainDevices_drops_the_unknown_devices_and_writes_only_then()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);
        store.Set("d2", Fcm + "x", "P", "A", null);
        var saves = _file.Saves;

        store.RetainDevices(["d1", "d2"]);
        Assert.Equal(saves, _file.Saves);
        store.RetainDevices(["d2"]);

        Assert.Equal(["d2"], store.All.Select(s => s.DeviceId));
        Assert.Equal(saves + 1, _file.Saves);
    }

    [Fact]
    public void RetainDevices_does_nothing_while_quiesced()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);
        var saves = _file.Saves;

        store.Quiesce();
        store.RetainDevices([]);

        Assert.NotNull(store.Get("d1"));
        Assert.Equal(saves, _file.Saves);
    }

    [Fact]
    public void RecordSuccess_stamps_the_time_and_clears_the_error_and_RecordError_keeps_the_text_in_memory_only()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);
        var saves = _file.Saves;

        store.RecordError("d1", "push service answered 403");
        Assert.Equal("push service answered 403", store.LastError("d1"));
        Assert.Equal(saves, _file.Saves);
        _now = T0.AddMinutes(1);
        store.RecordSuccess("d1");

        Assert.Null(store.LastError("d1"));
        Assert.Equal(T0.AddMinutes(1), store.Get("d1")!.LastSuccessUtc);
        Assert.Null(Create().LastError("d1"));
    }

    [Fact]
    public void RecordError_cuts_the_text_to_200_characters()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);

        store.RecordError("d1", new string('x', 500));

        Assert.Equal(200, store.LastError("d1")!.Length);
    }

    [Fact]
    public void A_stored_file_is_cleaned_on_load()
    {
        _file.Saved = new PushFile(
        [
            new PushSubscription("ok", Fcm, "P", "A", ["RankUp", "Bogus"], T0, null),
            new PushSubscription("evil", "https://evil.example/x", "P", "A", PushKinds.All, T0, null),
            new PushSubscription("", Fcm, "P", "A", PushKinds.All, T0, null),
            new PushSubscription("dup", Fcm, "P", "A", PushKinds.All, T0, null),
            new PushSubscription("dup", Fcm + "2", "P2", "A2", ["NoProgress"], T0, null),
            new PushSubscription("onlybogus", Fcm + "3", "P", "A", ["Bogus"], T0, null),
        ]);

        var store = Create();

        Assert.Equal(["dup", "ok", "onlybogus"], store.All.Select(s => s.DeviceId).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["RankUp"], store.Get("ok")!.Kinds);
        Assert.Equal(Fcm + "2", store.Get("dup")!.Endpoint);
        Assert.Equal(PushKinds.All, store.Get("onlybogus")!.Kinds);
    }

    [Fact]
    public void An_empty_kinds_list_stays_empty_on_load_and_Set_and_SetKinds_store_it_as_given()
    {
        _file.Saved = new PushFile([new PushSubscription("d0", Fcm, "P", "A", [], T0, null)]);
        var store = Create();

        Assert.Empty(store.Get("d0")!.Kinds);
        Assert.Empty(store.Set("d1", Fcm + "1", "P", "A", []).Kinds);
        store.Set("d2", Fcm + "2", "P", "A", null);
        Assert.True(store.SetKinds("d2", []));
        Assert.Empty(store.Get("d2")!.Kinds);
    }

    [Fact]
    public void Set_with_invalid_kinds_falls_back_to_the_existing_kinds_else_all()
    {
        var store = Create();

        Assert.Equal(PushKinds.All, store.Set("d1", Fcm, "P", "A", ["Bogus"]).Kinds);
        store.Set("d2", Fcm + "2", "P", "A", ["RankUp"]);
        Assert.Equal(["RankUp"], store.Set("d2", Fcm + "2", "P", "A", ["Bogus"]).Kinds);
    }

    [Fact]
    public void Entries_with_an_empty_key_or_auth_are_dropped_on_load()
    {
        _file.Saved = new PushFile(
        [
            new PushSubscription("nokey", Fcm, "", "A", PushKinds.All, T0, null),
            new PushSubscription("noauth", Fcm + "2", "P", "", PushKinds.All, T0, null),
        ]);

        Assert.Empty(Create().All);
    }

    [Fact]
    public void Quiesce_stops_writes_and_changes_and_Resume_restores_them()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);
        var saves = _file.Saves;

        store.Quiesce();
        store.Set("d2", Fcm + "x", "P", "A", null);
        store.RecordSuccess("d1");
        store.Remove("d1");
        store.SetKinds("d1", ["RankUp"]);
        Assert.Null(store.Get("d2"));
        Assert.NotNull(store.Get("d1"));
        Assert.Null(store.Get("d1")!.LastSuccessUtc);
        Assert.Equal(PushKinds.All, store.Get("d1")!.Kinds);
        Assert.Equal(saves, _file.Saves);

        store.Resume();
        store.Set("d2", Fcm + "x", "P", "A", null);
        Assert.NotNull(store.Get("d2"));
    }

    [Fact]
    public void Forget_clears_memory_deletes_the_file_and_a_late_mutation_writes_nothing()
    {
        var store = Create();
        store.Set("d1", Fcm, "P", "A", null);

        store.Forget();
        var saves = _file.Saves;
        store.RemoveDevices(["d1"]);
        store.RecordSuccess("d1");

        Assert.Empty(store.All);
        Assert.Equal(1, _file.Deletes);
        Assert.Equal(saves, _file.Saves);
    }

    [Fact]
    public void The_store_is_usable_again_after_Forget_even_when_it_was_quiesced()
    {
        var store = Create();
        store.Quiesce();
        store.Forget();

        store.Set("d1", Fcm, "P", "A", null);

        Assert.NotNull(store.Get("d1"));
    }

    [Fact]
    public void The_subscription_text_does_not_show_the_endpoint_or_keys()
    {
        var text = Create().Set("d1", Fcm, "KEYP", "KEYA", null).ToString();

        Assert.DoesNotContain("fcm.googleapis.com", text);
        Assert.DoesNotContain("KEYP", text);
        Assert.DoesNotContain("KEYA", text);
    }

    [Theory]
    [InlineData(new[] { "RankUp", "RankUp" }, new[] { "RankUp" })]
    [InlineData(new[] { "RankUp", "ScriptStopped" }, new[] { "ScriptStopped", "RankUp" })]
    public void Normalize_dedupes_and_orders(string[] input, string[] expected) => Assert.Equal(expected, PushKinds.Normalize(input));

    [Theory]
    [InlineData("Bogus")]
    [InlineData("")]
    public void Normalize_refuses_an_unknown_or_empty_kind(string kind) => Assert.Null(PushKinds.Normalize([kind]));

    [Fact]
    public void Normalize_of_null_is_null_and_of_an_empty_list_is_an_empty_list()
    {
        Assert.Null(PushKinds.Normalize(null));
        Assert.Empty(PushKinds.Normalize([])!);
        Assert.Null(PushKinds.Normalize([null]));
    }

    [Fact]
    public void All_is_the_thirteen_kinds_in_order()
    {
        Assert.Equal(
            ["ScriptStopped", "ScriptError", "SkuaDisconnected", "CharacterKicked", "SkuaOptionsWaiting", "RankUp", "HardFarmDrop", "NoProgress", "DyingOften", "UpdateAvailable", "QueueAttention", "QueueFinished", "BoostEnded"],
            PushKinds.All);
    }

    [Fact]
    public void RemoveIfEndpoint_removes_only_while_the_endpoint_is_the_same()
    {
        var file = new MemoryPushFileStore();
        var store = new PushSubscriptionStore(file, () => DateTimeOffset.UnixEpoch);
        store.Set("d", "https://fcm.googleapis.com/a", "p", "a", null);

        Assert.False(store.RemoveIfEndpoint("d", "https://fcm.googleapis.com/other"));
        Assert.NotNull(store.Get("d"));
        Assert.True(store.RemoveIfEndpoint("d", "https://fcm.googleapis.com/a"));
        Assert.Null(store.Get("d"));
        Assert.Empty(store.All);
    }

    [Fact]
    public void All_is_a_snapshot_that_follows_every_change_and_does_not_wait_for_the_store_lock()
    {
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => DateTimeOffset.UnixEpoch);
        store.Set("d", "https://fcm.googleapis.com/a", "p", "a", null);
        var before = store.All;

        store.SetKinds("d", ["RankUp"]);

        Assert.Equal(PushKinds.All, before.Single().Kinds);
        Assert.Equal(["RankUp"], store.All.Single().Kinds);
        store.Forget();
        Assert.Empty(store.All);
    }
}
