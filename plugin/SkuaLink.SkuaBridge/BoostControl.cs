using AqwCompanion.Skua;
using Skua.Core.Interfaces;

namespace SkuaLink.SkuaBridge;

/// <summary>
/// The thin Skua-bound glue behind the boost and login commands: four plain Flash reads, <c>Boosts.UseBoost(itemId)</c> (a game
/// packet, only on a tap) and <c>Servers.Relogin(server)</c> (Skua's own login with the credentials it already holds; the plugin
/// never reads a password). Every decision is in <see cref="SkuaBoostCommands"/>. Never used on Skua's UI thread: the command worker
/// and a pool thread call it.
/// </summary>
internal sealed class BoostControl(IScriptInterface bot) : ISkuaBoostHost
{
    private static readonly string[] Keys = ["iBoostG", "iBoostCP", "iBoostRep", "iBoostXP"];

    public bool LoggedIn => bot.Player.LoggedIn;

    /// <summary>
    /// The four values, or null when any read throws. Skua's getter answers 0 for a value it cannot read and offers no way to tell
    /// that from a real 0 (the sampler's two-reads rule covers it).
    /// </summary>
    public IReadOnlyList<int>? ReadBoostValues()
    {
        try
        {
            var values = new int[Keys.Length];
            for (var i = 0; i < Keys.Length; i++)
            {
                values[i] = bot.Flash.GetGameObject<int>("world.myAvatar.objData." + Keys[i]);
            }

            return values;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void UseBoost(int itemId) => bot.Boosts.UseBoost(itemId);

    public string? ServerName => bot.Servers.LastName;

    public bool Relogin(string serverName) => bot.Servers.Relogin(serverName);
}
