using System.Reflection;

namespace AqwCompanion.Skua;

/// <summary>
/// Tells Skua's own script loader view which script the plugin is about to load, so the Skua window shows the script a
/// phone start runs and not the one loaded before. The loader view keeps its own file name and status text, updated
/// only from Skua's messenger; the plugin loads and starts through <c>IScriptManager</c> directly, which never reaches the
/// view. So, before that load, this sends Skua's own <c>LoadScriptMessage(path)</c> on the <c>ScriptStatus</c> channel,
/// exactly as Skua's script repository does
/// (<c>StrongReferenceMessenger.Default.Send&lt;LoadScriptMessage, int&gt;(new(path), (int)MessageChannels.ScriptStatus)</c>).
/// The view's handler sets the loaded path (the same call the plugin makes next), the file name, and "[Script loaded]".
/// </summary>
/// <remarks>
/// The plugin references neither Skua.Core nor CommunityToolkit, so everything is found by name at run time: any missing
/// type or method, or any failure (also in a handler), means "not sent" and never an exception; the start goes on as
/// before. A successful lookup is kept; a failed one is tried again on the next send (starts are rare). The send runs on
/// the caller's thread, as Skua itself updates the same view from non-UI threads (its <c>ScriptStartedMessage</c> from the
/// thread that calls <c>StartScript</c>, its stop messages from the script thread). <c>StartScriptMessage</c> is never sent:
/// its handler shows dialogs and starts the script on its own.
/// </remarks>
public sealed class SkuaMessengerBridge
{
    public const string LoadScriptMessageTypeName = "Skua.Core.Messaging.LoadScriptMessage";
    public const string MessageChannelsTypeName = "Skua.Core.Messaging.MessageChannels";
    public const string MessengerTypeName = "CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger";
    public const string ScriptStatusChannel = "ScriptStatus";

    private readonly Func<string, Type?> _findType;
    private volatile Action<string>? _send;

    /// <param name="findType">Finds a type by its full name, or null; may throw (taken for "not found").</param>
    public SkuaMessengerBridge(Func<string, Type?> findType) => _findType = findType;

    /// <summary>Over the assemblies loaded in this process at the time of each lookup (Skua's own, in Skua).</summary>
    public static SkuaMessengerBridge ForLoadedAssemblies() =>
        new(name => FindType(AppDomain.CurrentDomain.GetAssemblies(), name));

    /// <summary>The first type with this full name in these assemblies, or null; an assembly that fails is skipped.</summary>
    public static Type? FindType(IEnumerable<Assembly> assemblies, string fullName)
    {
        foreach (var assembly in assemblies)
        {
            try
            {
                if (assembly.GetType(fullName, false) is { } type)
                {
                    return type;
                }
            }
            catch (Exception)
            {
                // A dynamic or unloadable assembly: not this one.
            }
        }

        return null;
    }

    /// <summary>
    /// Sends Skua's <c>LoadScriptMessage(path)</c> on the <c>ScriptStatus</c> channel. True when it was sent and every
    /// handler returned; false (never an exception) when Skua's types are not there or anything failed. An empty path is
    /// never sent: Skua's handler would open its file dialog.
    /// </summary>
    public bool TryAnnounceLoadedScript(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var send = _send ??= Resolve();
            if (send is null)
            {
                return false;
            }

            send(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Action<string>? Resolve()
    {
        var messageType = _findType(LoadScriptMessageTypeName);
        var channelsType = _findType(MessageChannelsTypeName);
        var messengerType = _findType(MessengerTypeName);
        if (messageType is null || channelsType is null || messengerType is null || !channelsType.IsEnum)
        {
            return null;
        }

        if (!Enum.GetNames(channelsType).Contains(ScriptStatusChannel, StringComparer.Ordinal))
        {
            return null;
        }

        var token = Convert.ToInt32(Enum.Parse(channelsType, ScriptStatusChannel), System.Globalization.CultureInfo.InvariantCulture);

        var constructor = messageType.GetConstructor([typeof(string)]);
        var messenger = messengerType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var method = FindSend(messengerType);
        if (constructor is null || messenger is null || method is null)
        {
            return null;
        }

        var closed = method.MakeGenericMethod(messageType, typeof(int));
        object boxedToken = token;
        return path => closed.Invoke(messenger, [constructor.Invoke([path]), boxedToken]);
    }

    /// <summary>The instance <c>Send&lt;TMessage, TToken&gt;(TMessage message, TToken token)</c>.</summary>
    private static MethodInfo? FindSend(Type messengerType)
    {
        foreach (var method in messengerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != "Send" || !method.IsGenericMethodDefinition)
            {
                continue;
            }

            var arguments = method.GetGenericArguments();
            var parameters = method.GetParameters();
            if (arguments.Length == 2 && parameters.Length == 2 &&
                parameters[0].ParameterType == arguments[0] && parameters[1].ParameterType == arguments[1])
            {
                return method;
            }
        }

        return null;
    }
}
