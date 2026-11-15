using System.Globalization;
using System.Reflection;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

/// <summary>
/// The bridge to Skua's own messenger, over stand-in types shaped like Skua's (<c>LoadScriptMessage</c>, the
/// <c>MessageChannels</c> enum, <c>StrongReferenceMessenger.Default.Send&lt;TMessage, TToken&gt;</c>), mapped to Skua's
/// names by the lookup the bridge is given.
/// </summary>
public class SkuaMessengerBridgeTests
{
    private static readonly Dictionary<string, Type> SkuaLike = new()
    {
        [SkuaMessengerBridge.LoadScriptMessageTypeName] = typeof(FakeLoadScriptMessage),
        [SkuaMessengerBridge.MessageChannelsTypeName] = typeof(FakeMessageChannels),
        [SkuaMessengerBridge.MessengerTypeName] = typeof(FakeMessenger),
    };

    public SkuaMessengerBridgeTests() => FakeMessenger.Reset();

    private static Type? Find(string name) => SkuaLike.GetValueOrDefault(name);

    [Fact]
    public void The_load_script_message_is_sent_on_the_script_status_channel()
    {
        var bridge = new SkuaMessengerBridge(Find);

        Assert.True(bridge.TryAnnounceLoadedScript(@"C:\Skua\Scripts\Farm\Hollowborn.cs"));

        var (message, token) = Assert.Single(FakeMessenger.Default.Sent);
        Assert.Equal(@"C:\Skua\Scripts\Farm\Hollowborn.cs", Assert.IsType<FakeLoadScriptMessage>(message).Path);
        Assert.Equal((int)FakeMessageChannels.ScriptStatus, Assert.IsType<int>(token));
    }

    [Theory]
    [InlineData(SkuaMessengerBridge.LoadScriptMessageTypeName)]
    [InlineData(SkuaMessengerBridge.MessageChannelsTypeName)]
    [InlineData(SkuaMessengerBridge.MessengerTypeName)]
    public void A_missing_type_skips_the_send(string missing)
    {
        var bridge = new SkuaMessengerBridge(name => name == missing ? null : Find(name));

        Assert.False(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
        Assert.Empty(FakeMessenger.Default.Sent);
    }

    [Fact]
    public void A_lookup_that_throws_skips_the_send()
    {
        var bridge = new SkuaMessengerBridge(_ => throw new InvalidOperationException("boom"));

        Assert.False(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
    }

    [Fact]
    public void A_messenger_without_a_two_argument_send_skips_the_send()
    {
        var bridge = new SkuaMessengerBridge(name =>
            name == SkuaMessengerBridge.MessengerTypeName ? typeof(MessengerWithoutTokenSend) : Find(name));

        Assert.False(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
        Assert.Empty(MessengerWithoutTokenSend.Default.Sent);
    }

    [Fact]
    public void A_channels_enum_without_script_status_skips_the_send()
    {
        var bridge = new SkuaMessengerBridge(name =>
            name == SkuaMessengerBridge.MessageChannelsTypeName ? typeof(OtherChannels) : Find(name));

        Assert.False(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
        Assert.Empty(FakeMessenger.Default.Sent);
    }

    [Fact]
    public void A_handler_that_throws_is_swallowed_and_the_next_send_works()
    {
        var bridge = new SkuaMessengerBridge(Find);
        FakeMessenger.Default.Throw = true;

        Assert.False(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));

        FakeMessenger.Default.Throw = false;
        Assert.True(bridge.TryAnnounceLoadedScript(@"C:\b.cs"));
        Assert.Equal(@"C:\b.cs", ((FakeLoadScriptMessage)Assert.Single(FakeMessenger.Default.Sent).Message).Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_path_sends_nothing_because_skua_would_open_its_file_dialog(string? path)
    {
        var bridge = new SkuaMessengerBridge(Find);

        Assert.False(bridge.TryAnnounceLoadedScript(path));
        Assert.Empty(FakeMessenger.Default.Sent);
    }

    [Fact]
    public void The_types_are_looked_up_once()
    {
        var lookups = 0;
        var bridge = new SkuaMessengerBridge(name =>
        {
            lookups++;
            return Find(name);
        });

        Assert.True(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
        var afterFirst = lookups;
        Assert.True(bridge.TryAnnounceLoadedScript(@"C:\b.cs"));

        Assert.Equal(afterFirst, lookups);
        Assert.Equal(2, FakeMessenger.Default.Sent.Count);
    }

    [Fact]
    public void A_failed_lookup_is_tried_again_on_the_next_send()
    {
        var available = false;
        var bridge = new SkuaMessengerBridge(name => available ? Find(name) : null);

        Assert.False(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
        available = true;

        Assert.True(bridge.TryAnnounceLoadedScript(@"C:\a.cs"));
    }

    [Fact]
    public void FindType_finds_a_type_by_full_name_and_skips_an_assembly_that_throws()
    {
        var name = typeof(FakeLoadScriptMessage).FullName!;
        Assembly[] assemblies = [new ThrowingAssembly(), typeof(SkuaMessengerBridgeTests).Assembly];

        Assert.Equal(typeof(FakeLoadScriptMessage), SkuaMessengerBridge.FindType(assemblies, name));
        Assert.Null(SkuaMessengerBridge.FindType(assemblies, "Skua.Core.Messaging.NoSuchType"));
    }

    [Fact]
    public void The_names_are_skuas()
    {
        Assert.Equal("Skua.Core.Messaging.LoadScriptMessage", SkuaMessengerBridge.LoadScriptMessageTypeName);
        Assert.Equal("Skua.Core.Messaging.MessageChannels", SkuaMessengerBridge.MessageChannelsTypeName);
        Assert.Equal("CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger", SkuaMessengerBridge.MessengerTypeName);
    }

    public sealed record FakeLoadScriptMessage(string? Path);

    public enum FakeMessageChannels
    {
        Other,
        GameEvents,
        ScriptStatus,
    }

    public enum OtherChannels
    {
        GameEvents,
    }

    /// <summary>Shaped like CommunityToolkit's <c>StrongReferenceMessenger</c>, with decoy overloads.</summary>
    public sealed class FakeMessenger
    {
        public static FakeMessenger Default { get; private set; } = new();

        public List<(object Message, object Token)> Sent { get; } = [];

        public bool Throw { get; set; }

        public static void Reset() => Default = new FakeMessenger();

        public TMessage Send<TMessage, TToken>(TMessage message, TToken token)
            where TMessage : class
            where TToken : IEquatable<TToken>
        {
            if (Throw)
            {
                throw new InvalidOperationException("handler failed");
            }

            Sent.Add((message, token));
            return message;
        }

        // Decoys: the extension-like shapes must not be picked.
        public TMessage Send<TMessage>(TMessage message)
            where TMessage : class => throw new InvalidOperationException("wrong overload");

        public TMessage Send<TMessage, TToken>(TToken token)
            where TMessage : class, new()
            where TToken : IEquatable<TToken> => throw new InvalidOperationException("wrong overload");
    }

    public sealed class MessengerWithoutTokenSend
    {
        public static MessengerWithoutTokenSend Default { get; } = new();

        public List<object> Sent { get; } = [];

        public TMessage Send<TMessage>(TMessage message)
            where TMessage : class
        {
            Sent.Add(message);
            return message;
        }
    }

    private sealed class ThrowingAssembly : Assembly
    {
        public override Type? GetType(string name, bool throwOnError) => throw new FileLoadException("unloadable");

        public override string? FullName => "Throwing, Version=1.0.0.0, Culture=" + CultureInfo.InvariantCulture.Name;
    }
}
