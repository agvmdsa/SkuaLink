using System.Text.RegularExpressions;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Queue;

public sealed record QueueStepInput(string? Id, string ScriptId, string? GoalItem, int GoalQuantity, ReputationGoal? Reputation = null, int? MaxMinutes = null);

public sealed record QueueValidation(bool Ok, string? Error, IReadOnlyList<QueueStep> Steps);

public static partial class QueueValidator
{
    public const string BadCount = "A queue needs 1 to 20 steps.";
    public const string UnknownScript = "Script not found. Choose it from the list.";
    public const string NeedItem = "Every step needs the item it must obtain.";
    public const string BadQuantity = "The quantity must be from 1 to 1000000.";
    public const string BadMinutes = "The time limit must be from 1 to 1440 minutes.";
    public const string BadReputation = "The reputation goal needs a faction and a rank from 1 to 10.";

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>
    /// Checks a step list the way the phone API and the editor must: script ids only from <paramref name="scripts"/> (the plugin's own
    /// list; the stored script name comes from it), limits from <see cref="QueueLimits"/>, text cleaned. A client id is kept only
    /// when it has the shape of <paramref name="newId"/>'s ids and is not repeated.
    /// </summary>
    public static QueueValidation Validate(IReadOnlyList<QueueStepInput> input, IReadOnlyList<SkuaScriptInfo> scripts, Func<string> newId, bool allowEmpty = false)
    {
        if (input.Count > QueueLimits.MaxSteps || (input.Count == 0 && !allowEmpty))
        {
            return Fail(BadCount);
        }

        var byId = scripts.GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var steps = new List<QueueStep>();
        foreach (var step in input)
        {
            if (!SkuaOptionCache.IsScriptId(step.ScriptId) || !byId.TryGetValue(step.ScriptId, out var script))
            {
                return Fail(UnknownScript);
            }

            var item = QueueText.Clean(step.GoalItem, QueueLimits.MaxItemName);
            if (item.Length == 0)
            {
                return Fail(NeedItem);
            }

            if (step.GoalQuantity is < QueueLimits.MinQuantity or > QueueLimits.MaxQuantity)
            {
                return Fail(BadQuantity);
            }

            if (step.MaxMinutes is { } minutes && minutes is < QueueLimits.MinMinutes or > QueueLimits.MaxMinutes)
            {
                return Fail(BadMinutes);
            }

            ReputationGoal? reputation = null;
            if (step.Reputation is { } goal)
            {
                var faction = QueueText.Clean(goal.Faction, QueueLimits.MaxFaction);
                if (faction.Length == 0 || goal.Rank is < QueueLimits.MinRank or > QueueLimits.MaxRank)
                {
                    return Fail(BadReputation);
                }

                reputation = new ReputationGoal(faction, goal.Rank);
            }

            var id = step.Id is { } given && IdPattern().IsMatch(given) && !used.Contains(given) ? given : NextId(newId, used);
            used.Add(id);
            steps.Add(new QueueStep(id, step.ScriptId, QueueText.Clean(script.Name, QueueLimits.MaxScriptName), item, step.GoalQuantity, reputation, step.MaxMinutes));
        }

        return new QueueValidation(true, null, steps);
    }

    /// <summary>The steps of a stored file made safe: damaged steps are dropped, text cleaned, the list capped, ids kept only when valid and unique.</summary>
    public static IReadOnlyList<QueueStep> Normalize(IEnumerable<QueueStep?>? steps)
    {
        var result = new List<QueueStep>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps ?? [])
        {
            if (result.Count >= QueueLimits.MaxSteps)
            {
                break;
            }

            if (step is null || step.Id is null || !IdPattern().IsMatch(step.Id) || !used.Add(step.Id) || !SkuaOptionCache.IsScriptId(step.ScriptId)
                || step.GoalQuantity is < QueueLimits.MinQuantity or > QueueLimits.MaxQuantity
                || (step.MaxMinutes is { } m && m is < QueueLimits.MinMinutes or > QueueLimits.MaxMinutes))
            {
                continue;
            }

            var item = QueueText.Clean(step.GoalItem, QueueLimits.MaxItemName);
            var name = QueueText.Clean(step.ScriptName, QueueLimits.MaxScriptName);
            if (item.Length == 0 || name.Length == 0)
            {
                continue;
            }

            ReputationGoal? reputation = null;
            if (step.Reputation is { } goal)
            {
                var faction = QueueText.Clean(goal.Faction, QueueLimits.MaxFaction);
                if (faction.Length > 0 && goal.Rank is >= QueueLimits.MinRank and <= QueueLimits.MaxRank)
                {
                    reputation = new ReputationGoal(faction, goal.Rank);
                }
            }

            result.Add(step with { ScriptName = name, GoalItem = item, Reputation = reputation });
        }

        return result;
    }

    private static string NextId(Func<string> newId, HashSet<string> used)
    {
        string id;
        do
        {
            id = newId();
        }
        while (used.Contains(id));

        return id;
    }

    private static QueueValidation Fail(string error) => new(false, error, []);
}
