using AqwCompanion.Phone.Queue;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Queue;

/// <summary>Where the editor gets the scripts Skua lists for a character; null when they cannot be had.</summary>
public interface IQueueScriptSource
{
    Task<IReadOnlyList<SkuaScriptInfo>?> ListAsync(string character, CancellationToken ct);
}

/// <summary>Asks the connected Skua of the character for its script list. Never throws.</summary>
public sealed class SkuaQueueScripts(ISkuaFeed feed, ISkuaCommandChannel channel) : IQueueScriptSource
{
    public async Task<IReadOnlyList<SkuaScriptInfo>?> ListAsync(string character, CancellationToken ct)
    {
        try
        {
            var instance = feed.Current.Instances.FirstOrDefault(i => !i.Disconnected && QueueText.SameName(i.Status?.Character, character));
            if (instance is null)
            {
                return null;
            }

            var result = await channel.SendCommandAsync(instance.InstanceId, new ListScriptsCommand(string.Empty), ct).ConfigureAwait(false);
            return result is { Ok: true } ? result.Scripts ?? [] : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
