using AqwCompanion.App;
using AqwCompanion.App.Push;

namespace AqwCompanion.Tests.Push;

public sealed class VapidKeyStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    private VapidKeyStore Store() => new(_paths.VapidKeyPath);

    [Fact]
    public void No_file_is_made_before_the_first_use()
    {
        _ = Store();

        Assert.False(File.Exists(_paths.VapidKeyPath));
    }

    [Fact]
    public void The_first_use_creates_the_file_and_leaves_no_temp_file()
    {
        var key = Store().GetOrCreate();

        Assert.NotNull(key);
        Assert.Equal(["vapid.key"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_second_store_on_the_same_path_returns_the_same_key_and_one_store_keeps_its_key()
    {
        var store = Store();
        var first = store.GetOrCreate();

        Assert.Same(first, store.GetOrCreate());
        Assert.Equal(first.PublicKeyText, Store().GetOrCreate().PublicKeyText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    public void A_damaged_file_is_replaced_by_a_new_key(string content)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.VapidKeyPath, content);

        var key = Store().GetOrCreate();

        Assert.Equal(key.PublicKeyText, Store().GetOrCreate().PublicKeyText);
        Assert.NotEqual(content, File.ReadAllText(_paths.VapidKeyPath));
    }

    [Fact]
    public void Forget_deletes_the_files_and_the_next_call_makes_a_different_key()
    {
        var store = Store();
        var first = store.GetOrCreate().PublicKeyText;
        File.WriteAllText(_paths.VapidKeyPath + ".tmp", "x");

        store.Forget();

        Assert.Empty(Directory.GetFiles(_paths.Root));
        Assert.NotEqual(first, store.GetOrCreate().PublicKeyText);
    }

    [Fact]
    public void A_quiesced_store_still_gives_a_key_but_writes_nothing()
    {
        var store = Store();
        store.Quiesce();

        Assert.NotNull(store.GetOrCreate());
        Assert.False(File.Exists(_paths.VapidKeyPath));
    }

    [Fact]
    public void An_unwritable_path_still_returns_a_key_and_does_not_throw()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(Path.Combine(_paths.Root, "blocker"), "x");
        var store = new VapidKeyStore(Path.Combine(_paths.Root, "blocker", "vapid.key"));

        VapidKeyHolder holder = new();
        Assert.Null(Record.Exception(() => holder.Key = store.GetOrCreate()));
        Assert.NotNull(holder.Key);
        Assert.Same(holder.Key, store.GetOrCreate());
    }

    [Fact]
    public void An_unreadable_file_is_never_overwritten_and_the_next_call_returns_the_original_key()
    {
        var original = Store().GetOrCreate().PublicKeyText;
        var before = File.ReadAllText(_paths.VapidKeyPath);
        var store = Store();

        string duringLock;
        using (new FileStream(_paths.VapidKeyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            duringLock = store.GetOrCreate().PublicKeyText;
        }

        Assert.NotEqual(original, duringLock);
        Assert.Equal(before, File.ReadAllText(_paths.VapidKeyPath));
        Assert.Equal(original, store.GetOrCreate().PublicKeyText);
    }

    [Fact]
    public void A_key_made_while_quiesced_is_not_kept_and_the_first_use_after_Resume_writes_one()
    {
        var store = Store();
        store.Quiesce();
        _ = store.GetOrCreate();
        store.Resume();

        var key = store.GetOrCreate();

        Assert.Equal(key.PublicKeyText, Store().GetOrCreate().PublicKeyText);
    }

    [Fact]
    public async Task Two_concurrent_first_calls_return_the_same_key()
    {
        var store = Store();

        var keys = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(store.GetOrCreate)));

        Assert.Single(keys.Select(k => k.PublicKeyText).Distinct());
    }

    private sealed class VapidKeyHolder
    {
        public AqwCompanion.Phone.Push.VapidKey? Key { get; set; }
    }

    [Fact]
    public void Reading_the_existing_key_never_creates_the_file()
    {
        var store = Store();

        Assert.Null(store.GetExisting());
        Assert.False(File.Exists(_paths.VapidKeyPath));
    }

    [Fact]
    public void Reading_the_existing_key_returns_the_key_that_was_made()
    {
        var made = Store().GetOrCreate();

        Assert.Equal(made.PublicKeyText, Store().GetExisting()!.PublicKeyText);
    }

    [Fact]
    public void After_forget_the_existing_key_is_gone_and_a_late_send_does_not_bring_the_file_back()
    {
        var store = Store();
        _ = store.GetOrCreate();
        store.Forget();

        Assert.Null(store.GetExisting());
        Assert.False(File.Exists(_paths.VapidKeyPath));
    }

    [Fact]
    public void While_quiesced_there_is_no_existing_key()
    {
        var store = Store();
        _ = store.GetOrCreate();
        store.Quiesce();

        Assert.Null(store.GetExisting());
    }
}
