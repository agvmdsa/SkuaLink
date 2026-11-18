namespace AqwCompanion.Skua;

/// <summary>What the boost and login commands need from Skua. The plugin implements it; the tests fake it. Any member may throw.</summary>
public interface ISkuaBoostHost
{
    bool LoggedIn { get; }

    /// <summary>The four raw values (gold, class, reputation, XP), or null when they could not be read.</summary>
    IReadOnlyList<int>? ReadBoostValues();

    /// <summary>Skua's <c>Boosts.UseBoost(itemId)</c>. Never called with a bank search.</summary>
    void UseBoost(int itemId);

    /// <summary>The server the character last played (Skua's <c>Servers.LastName</c>).</summary>
    string? ServerName { get; }

    /// <summary>Skua's <c>Servers.Relogin(serverName)</c>; blocks up to about 30 s.</summary>
    bool Relogin(string serverName);
}

/// <summary>
/// The guards of the two commands that act on the game: <c>useBoost</c> and <c>relogin</c>. Pure decisions over an
/// <see cref="ISkuaBoostHost"/>. Nothing from the app is trusted except the item id, which must be in the plugin's own list.
/// </summary>
public sealed class SkuaBoostCommands(
    ISkuaBoostHost host, Func<SkuaBoosts?> published, Func<DateTimeOffset> now, Func<TimeSpan, Task>? delay = null,
    Func<Action, Task>? background = null, Action? onChanged = null)
{
    public static readonly TimeSpan ActivationWait = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ActivationPoll = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan ReloginCooldown = TimeSpan.FromSeconds(15);

    public const string NotLoggedIn = "The character is not logged in.";
    public const string NotInBag = "That boost is not in the bag.";
    public const string UnknownType = "That boost is not a gold, class, reputation or XP boost.";
    public const string AlreadyActive = "A boost of that type is already active.";
    public const string NotActivated = "The game did not activate the boost.";
    public const string CouldNotRead = "Could not read the boosts.";
    public const string AlreadyLoggedIn = "The character is already logged in.";
    public const string WaitAMoment = "Wait a moment before trying again.";
    public const string LoginRunning = "A login is already in progress.";
    public const string NoServer = "There is no server to log in to.";

    private static readonly string[] Types = ["gold", "class", "rep", "xp"];
    private readonly object _lock = new();
    private readonly Func<TimeSpan, Task> _delay = delay ?? (time => Task.Delay(time));
    private readonly Func<Action, Task> _background = background ?? (work => Task.Run(work));
    private DateTimeOffset? _lastLogin;
    private Task _login = Task.CompletedTask;

    public async Task<ResultMessage> UseAsync(UseBoostCommand command)
    {
        if (!host.LoggedIn)
        {
            return Fail(command.ReqId, NotLoggedIn);
        }

        var item = published()?.Items?.FirstOrDefault(i => i.ItemId == command.ItemId && i.Quantity > 0);
        if (item is null)
        {
            return Fail(command.ReqId, NotInBag);
        }

        var type = Array.IndexOf(Types, item.Type);
        if (type < 0)
        {
            return Fail(command.ReqId, UnknownType);
        }

        if (host.ReadBoostValues() is not { Count: 4 } before)
        {
            return Fail(command.ReqId, CouldNotRead);
        }

        if (before[type] > 0)
        {
            return Fail(command.ReqId, AlreadyActive);
        }

        host.UseBoost(item.ItemId);
        for (var waited = TimeSpan.Zero; waited < ActivationWait; waited += ActivationPoll)
        {
            await _delay(ActivationPoll).ConfigureAwait(false);
            if (host.ReadBoostValues() is { Count: 4 } after && after[type] > 0)
            {
                onChanged?.Invoke();
                return Ok(command.ReqId);
            }
        }

        onChanged?.Invoke();
        return Fail(command.ReqId, NotActivated);
    }

    public ResultMessage Relogin(ReloginCommand command)
    {
        if (host.LoggedIn)
        {
            return Fail(command.ReqId, AlreadyLoggedIn);
        }

        var server = host.ServerName?.Trim();
        lock (_lock)
        {
            if (_lastLogin is { } last && now() - last < ReloginCooldown)
            {
                return Fail(command.ReqId, WaitAMoment);
            }

            if (!_login.IsCompleted)
            {
                return Fail(command.ReqId, LoginRunning);
            }

            if (string.IsNullOrEmpty(server))
            {
                return Fail(command.ReqId, NoServer);
            }

            _lastLogin = now();
            _login = _background(() =>
            {
                try
                {
                    host.Relogin(server);
                }
                catch (Exception)
                {
                    // The status shows whether the login worked; there is nothing else to do with the failure here.
                }

                onChanged?.Invoke();
            });
        }

        return Ok(command.ReqId);
    }

    private static ResultMessage Ok(string reqId) => new(reqId, true, null, null, null, null, false, false);

    private static ResultMessage Fail(string reqId, string error) => new(reqId, false, error, null, null, null, false, false);
}
