using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AqwCompanion.Skua;

/// <summary>One JSON object per line. Never throws on input; <see cref="Parse"/> answers null for anything it cannot use.</summary>
public static class SkuaLine
{
    public static string ToLine(SkuaMessage message)
    {
        var max = message switch
        {
            ResultMessage => SkuaProtocol.MaxResultLineBytes,
            InventoryMessage => SkuaProtocol.MaxInventoryLineBytes,
            _ => SkuaProtocol.MaxLineBytes,
        };
        var line = Write(message, Limits.All);
        if (Fits(line, max))
        {
            return line;
        }

        // Texts are cut, but long lists of long names in escaped non-ASCII could still pass the line cap, and a line over the
        // cap is refused whole. For a status the last factions, then the last quests, are left out until the line fits. For a
        // result the last scripts are left out (the result says Truncated); a result that still does not fit becomes an error
        // rather than losing option values or definitions, which would mislead the form built from them. For an inventory the last
        // items are left out (the inventory says Truncated).
        var limits = Limits.All;
        switch (message)
        {
            case StatusMessage { Status: var status }:
                line = Shrink(message, ref limits, max, Math.Min(status.Factions?.Count ?? 0, SkuaProtocol.MaxFactions), (l, n) => l with { Factions = n });
                if (!Fits(line, max))
                {
                    line = Shrink(message, ref limits, max, Math.Min(status.Quests?.Count ?? 0, SkuaProtocol.MaxQuests), (l, n) => l with { Quests = n });
                }

                if (!Fits(line, max))
                {
                    line = Shrink(message, ref limits, max, Math.Min(status.Boosts?.Items?.Count ?? 0, SkuaProtocol.MaxBoostItems), (l, n) => l with { BoostItems = n });
                }

                break;
            case ResultMessage result:
                line = Shrink(message, ref limits, max, Math.Min(result.Scripts?.Count ?? 0, SkuaProtocol.MaxScripts), (l, n) => l with { Scripts = n });
                if (!Fits(line, max))
                {
                    line = Write(new ResultMessage(result.ReqId, false, "result too large", null, null, null, false, false), Limits.All);
                }

                break;
            case InventoryMessage { Inventory: var inventory }:
                line = Shrink(message, ref limits, max, inventory.Items.Count, (l, n) => l with { Items = n });
                break;
        }

        return line;
    }

    /// <summary>
    /// The line for a command, or null when it is over <see cref="SkuaProtocol.MaxCommandLineBytes"/> (the plugin would drop
    /// it): the sender answers the request itself instead of waiting for a result that never comes.
    /// </summary>
    public static string? ToCommandLine(SkuaCommand command)
    {
        var line = Write(command, Limits.All);
        return Fits(line, SkuaProtocol.MaxCommandLineBytes) ? line : null;
    }

    /// <summary>How many entries of each list are written at most (on top of the protocol caps).</summary>
    private readonly record struct Limits(int Quests, int Factions, int Scripts, int Defs, int Values, int Items, int BoostItems)
    {
        public static readonly Limits All = new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
    }

    private static bool Fits(string line, int max) => Encoding.UTF8.GetByteCount(line) <= max;

    /// <summary>
    /// Writes the line with the largest number of entries below <paramref name="count"/> (all of them are known not to fit)
    /// that fits, or with none of this list when even that does not fit. A binary search: one more entry never shortens the line.
    /// </summary>
    private static string Shrink(SkuaMessage message, ref Limits limits, int max, int count, Func<Limits, int, Limits> with)
    {
        var low = 0;
        var high = count - 1;
        while (low < high)
        {
            var mid = low + ((high - low + 1) / 2);
            if (Fits(Write(message, with(limits, mid)), max))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        limits = with(limits, low);
        return Write(message, limits);
    }

    private static string Write(SkuaMessage message, Limits limits)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", SkuaProtocol.Version);
            switch (message)
            {
                case HelloMessage h:
                    w.WriteString("type", "hello");
                    w.WriteString("plugin", Cut(h.Hello.PluginVersion));
                    w.WriteString("skua", Cut(h.Hello.SkuaVersion));
                    if (h.Hello.InstanceId is { } instanceId)
                    {
                        w.WriteString("instanceId", Cut(instanceId));
                    }

                    break;
                case StatusMessage s:
                    w.WriteString("type", "status");
                    w.WriteBoolean("loggedIn", s.Status.LoggedIn);
                    WriteNullable(w, "character", s.Status.Character);
                    WriteNullable(w, "server", s.Status.Server);
                    WriteNullable(w, "map", s.Status.Map);
                    WriteNullable(w, "script", s.Status.Script);
                    w.WriteBoolean("running", s.Status.ScriptRunning);
                    if (s.Status.RunningSince is { } since)
                    {
                        w.WriteString("runningSince", since.ToString("o", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        w.WriteNull("runningSince");
                    }

                    if (s.Status.Quests is { } quests)
                    {
                        w.WriteStartArray("quests");
                        foreach (var quest in quests.Take(Math.Min(limits.Quests, SkuaProtocol.MaxQuests)))
                        {
                            w.WriteStartObject();
                            w.WriteNumber("id", quest.Id);
                            w.WriteString("name", Cut(quest.Name));
                            w.WriteBoolean("ready", quest.Ready);
                            w.WriteEndObject();
                        }

                        w.WriteEndArray();
                    }

                    if (s.Status.Factions is { } factions)
                    {
                        w.WriteStartArray("factions");
                        foreach (var faction in factions.Take(Math.Min(limits.Factions, SkuaProtocol.MaxFactions)))
                        {
                            w.WriteStartObject();
                            w.WriteNumber("id", faction.Id);
                            w.WriteString("name", Cut(faction.Name));
                            w.WriteNumber("rank", faction.Rank);
                            w.WriteNumber("totalRep", faction.TotalRep);
                            w.WriteNumber("rep", faction.Rep);
                            w.WriteNumber("requiredRep", faction.RequiredRep);
                            w.WriteEndObject();
                        }

                        w.WriteEndArray();
                    }

                    if (s.Status.Control is { } control && SkuaProtocol.ControlStates.Contains(control))
                    {
                        w.WriteString("control", control);
                    }

                    if (s.Status.SettleSecondsLeft is { } left && left >= 0)
                    {
                        w.WriteNumber("settleSecondsLeft", left);
                    }

                    if (s.Status.OptionsStuck)
                    {
                        w.WriteBoolean("optionsStuck", true);
                    }

                    if (s.Status.Profile is { } p)
                    {
                        WriteProfile(w, p);
                    }

                    if (s.Status.Boosts is { } boosts)
                    {
                        WriteBoosts(w, boosts, limits);
                    }

                    break;
                case InventoryMessage i:
                    WriteInventory(w, i.Inventory, limits);
                    break;
                case LogMessage l:
                    w.WriteString("type", "log");
                    w.WriteString("at", l.Entry.At.ToString("o", CultureInfo.InvariantCulture));
                    w.WriteString("kind", l.Entry.Kind.ToString());
                    w.WriteString("text", Cut(l.Entry.Text));
                    if (l.Entry.Item is { } item)
                    {
                        w.WriteString("item", Cut(item));
                        w.WriteNumber("count", Math.Clamp(l.Entry.Count, 1, SkuaProtocol.MaxCount));
                    }
                    break;
                case ListScriptsCommand c:
                    w.WriteString("type", "listScripts");
                    w.WriteString("reqId", Cut(c.ReqId));
                    break;
                case GetOptionsCommand c:
                    w.WriteString("type", "getOptions");
                    w.WriteString("reqId", Cut(c.ReqId));
                    w.WriteString("id", Cut(c.ScriptId));
                    break;
                case SetOptionsCommand c:
                    w.WriteString("type", "setOptions");
                    w.WriteString("reqId", Cut(c.ReqId));
                    w.WriteString("id", Cut(c.ScriptId));
                    WriteValues(w, "values", c.Values, int.MaxValue);
                    break;
                case StartCommand c:
                    w.WriteString("type", "start");
                    w.WriteString("reqId", Cut(c.ReqId));
                    w.WriteString("id", Cut(c.ScriptId));
                    break;
                case StopCommand c:
                    w.WriteString("type", "stop");
                    w.WriteString("reqId", Cut(c.ReqId));
                    break;
                case UseBoostCommand c:
                    w.WriteString("type", "useBoost");
                    w.WriteString("reqId", Cut(c.ReqId));
                    w.WriteNumber("itemId", c.ItemId);
                    break;
                case ReloginCommand c:
                    w.WriteString("type", "relogin");
                    w.WriteString("reqId", Cut(c.ReqId));
                    break;
                case ResultMessage r:
                    WriteResult(w, r, limits);
                    break;
                default:
                    throw new ArgumentException("This message is never sent.", nameof(message));
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteBoosts(Utf8JsonWriter w, SkuaBoosts boosts, Limits limits)
    {
        w.WriteStartObject("boosts");
        w.WriteNumber("gold", BoostLevel(boosts.Gold));
        w.WriteNumber("class", BoostLevel(boosts.Class));
        w.WriteNumber("rep", BoostLevel(boosts.Rep));
        w.WriteNumber("xp", BoostLevel(boosts.Xp));
        w.WriteNumber("readAt", Math.Max(0, boosts.ReadAt));
        if (boosts.Items is { } items)
        {
            w.WriteStartArray("items");
            var written = 0;
            foreach (var item in items)
            {
                var name = Cut(item.Name, SkuaProtocol.MaxBoostNameLength);
                if (item.ItemId <= 0 || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (written >= Math.Min(limits.BoostItems, SkuaProtocol.MaxBoostItems))
                {
                    break;
                }

                written++;
                w.WriteStartObject();
                w.WriteNumber("itemId", item.ItemId);
                w.WriteString("name", name);
                w.WriteString("type", SkuaProtocol.BoostTypes.Contains(item.Type) ? item.Type : "unknown");
                w.WriteNumber("quantity", Math.Max(0, item.Quantity));
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    private static int BoostLevel(int value) => Math.Max(SkuaProtocol.BoostTimeUnknown, value);

    private static void WriteProfile(Utf8JsonWriter w, SkuaProfile p)
    {
        w.WriteStartObject("profile");
        w.WriteNumber("level", Math.Max(0, p.Level));
        w.WriteNumber("xp", Math.Max(0, p.Xp));
        w.WriteNumber("xpToLevel", Math.Max(0, p.XpToLevel));
        w.WriteNumber("gold", Math.Max(0, p.Gold));
        w.WriteNumber("coins", Math.Max(0, p.Coins));
        w.WriteNumber("classRank", Math.Max(0, p.ClassRank));
        w.WriteBoolean("isMember", p.IsMember);
        WriteOptional(w, "className", p.ClassName, SkuaProtocol.MaxTextLength);
        WriteCount(w, "classId", p.ClassId);
        WriteCount(w, "classPoints", p.ClassPoints);
        WriteOptional(w, "guild", p.Guild, SkuaProtocol.MaxTextLength);
        WriteCount(w, "bagSlots", p.BagSlots);
        WriteCount(w, "bankSlots", p.BankSlots);
        WriteCount(w, "houseSlots", p.HouseSlots);
        w.WriteEndObject();
    }

    /// <summary>A count is written only when reported and not negative; absent otherwise.</summary>
    private static void WriteCount(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } n && n >= 0)
        {
            w.WriteNumber(name, n);
        }
    }

    /// <summary>
    /// Only known containers that were read are written, each item only in a read container, at most
    /// <see cref="SkuaProtocol.MaxInventoryItems"/> per container and <see cref="Limits.Items"/> in all; a cut sets truncated.
    /// </summary>
    private static void WriteInventory(Utf8JsonWriter w, SkuaInventory inventory, Limits limits)
    {
        w.WriteString("type", "inventory");
        if (inventory.Character is { } who && Cut(who) is { Length: > 0 } character)
        {
            w.WriteString("character", character);
        }

        var read = inventory.Read.Where(SkuaProtocol.InventoryContainers.Contains).Distinct(StringComparer.Ordinal).ToList();
        w.WriteStartArray("read");
        foreach (var container in read)
        {
            w.WriteStringValue(container);
        }

        w.WriteEndArray();
        var cut = false;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var written = 0;
        w.WriteStartArray("items");
        foreach (var item in inventory.Items)
        {
            var name = Cut(item.Name);
            if (name.Length == 0 || !read.Contains(item.Where))
            {
                continue;
            }

            var n = counts.GetValueOrDefault(item.Where);
            if (n >= SkuaProtocol.MaxInventoryItems || written >= limits.Items)
            {
                cut = true;
                continue;
            }

            counts[item.Where] = n + 1;
            written++;
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteNumber("qty", Math.Max(0, item.Quantity));
            w.WriteString("where", item.Where);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        if (inventory.Truncated || cut)
        {
            w.WriteBoolean("truncated", true);
        }
    }

    private static void WriteResult(Utf8JsonWriter w, ResultMessage r, Limits limits)
    {
        w.WriteString("type", "result");
        w.WriteString("reqId", Cut(r.ReqId));
        w.WriteBoolean("ok", r.Ok);
        if (r.Error is { } error)
        {
            w.WriteString("error", Cut(error));
        }

        if (r.Scripts is { } scripts)
        {
            w.WriteStartArray("scripts");
            foreach (var script in scripts.Take(Math.Min(limits.Scripts, SkuaProtocol.MaxScripts)))
            {
                w.WriteStartObject();
                w.WriteString("id", Cut(script.Id));
                w.WriteString("name", Cut(script.Name));
                w.WriteString("folder", Cut(script.Folder));
                WriteOptional(w, "title", script.Title, SkuaProtocol.MaxScriptTitleLength);
                WriteOptional(w, "description", script.Description, SkuaProtocol.MaxScriptDescriptionLength);
                WriteOptional(w, "tags", script.Tags, SkuaProtocol.MaxScriptTagsLength);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        if (r.OptionDefs is { } defs)
        {
            w.WriteStartArray("optionDefs");
            foreach (var def in defs.Take(limits.Defs))
            {
                w.WriteStartObject();
                w.WriteString("category", Cut(def.Category));
                w.WriteString("name", Cut(def.Name));
                w.WriteString("displayName", Cut(def.DisplayName));
                w.WriteString("description", Cut(def.Description));
                w.WriteString("type", OptionType(def.Type));
                w.WriteString("default", Cut(def.Default, SkuaProtocol.MaxOptionValueLength));
                if (def.Choices is { } choices)
                {
                    w.WriteStartArray("choices");
                    foreach (var choice in choices.Select(SkuaText.WithoutLoneSurrogates).Where(c => c.Length <= SkuaProtocol.MaxOptionValueLength))
                    {
                        w.WriteStringValue(choice);
                    }

                    w.WriteEndArray();
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        if (r.Values is { } values)
        {
            WriteValues(w, "options", values, limits.Values);
        }

        w.WriteBoolean("hasOptions", r.HasOptions);
        w.WriteBoolean("firstRun", r.FirstRun);
        if (r.Truncated || (r.Scripts?.Count ?? 0) > Math.Min(limits.Scripts, SkuaProtocol.MaxScripts))
        {
            w.WriteBoolean("truncated", true);
        }
    }

    /// <summary>A value over <see cref="SkuaProtocol.MaxOptionValueLength"/> is left out rather than cut: a cut value is another value.</summary>
    private static void WriteValues(Utf8JsonWriter w, string name, IReadOnlyList<SkuaOptionValue> values, int max)
    {
        w.WriteStartArray(name);
        foreach (var value in values
                     .Select(v => v with { Value = SkuaText.WithoutLoneSurrogates(v.Value) })
                     .Where(v => v.Value.Length <= SkuaProtocol.MaxOptionValueLength)
                     .Take(max))
        {
            w.WriteStartObject();
            w.WriteString("category", Cut(value.Category));
            w.WriteString("name", Cut(value.Name));
            w.WriteString("value", value.Value);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static string OptionType(string type) => SkuaProtocol.OptionTypes.Contains(type) ? type : "other";

    public static SkuaMessage? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // Only a result or an inventory may be longer than MaxLineBytes; the type is known only after parsing, so the larger cap
        // comes first (MaxInventoryLineBytes is never above MaxResultLineBytes).
        var bytes = Encoding.UTF8.GetByteCount(line);
        if (bytes > SkuaProtocol.MaxResultLineBytes)
        {
            return null;
        }

        if (HasLoneSurrogate(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || !v.TryGetInt32(out var version))
            {
                return null;
            }

            if (version != SkuaProtocol.Version)
            {
                return new UnsupportedVersionMessage(version);
            }

            var type = Str(root, "type");
            if ((type is not ("result" or "inventory") && bytes > SkuaProtocol.MaxLineBytes)
                || (type == "inventory" && bytes > SkuaProtocol.MaxInventoryLineBytes)
                || (type is "listScripts" or "getOptions" or "setOptions" or "start" or "stop" or "useBoost" or "relogin" && bytes > SkuaProtocol.MaxCommandLineBytes))
            {
                return null;
            }

            return type switch
            {
                "hello" => Str(root, "plugin") is { } plugin && Str(root, "skua") is { } skua
                    ? new HelloMessage(new SkuaHello(Cut(plugin), Cut(skua), Optional(() => NonEmpty(root, "instanceId"))))
                    : null,
                "status" => ParseStatus(root),
                "log" => ParseLog(root),
                "listScripts" => NonEmpty(root, "reqId") is { } reqId ? new ListScriptsCommand(reqId) : null,
                "getOptions" => NonEmpty(root, "reqId") is { } reqId && NonEmpty(root, "id") is { } id ? new GetOptionsCommand(reqId, id) : null,
                "setOptions" => NonEmpty(root, "reqId") is { } reqId && NonEmpty(root, "id") is { } id && ParseList(root, "values", int.MaxValue, ParseValue) is { } values
                    ? new SetOptionsCommand(reqId, id, values)
                    : null,
                "start" => NonEmpty(root, "reqId") is { } reqId && NonEmpty(root, "id") is { } id ? new StartCommand(reqId, id) : null,
                "stop" => NonEmpty(root, "reqId") is { } reqId ? new StopCommand(reqId) : null,
                "useBoost" => NonEmpty(root, "reqId") is { } reqId && Count(root, "itemId") is { } itemId && itemId > 0 ? new UseBoostCommand(reqId, itemId) : null,
                "relogin" => NonEmpty(root, "reqId") is { } reqId ? new ReloginCommand(reqId) : null,
                "result" => ParseResult(root),
                "inventory" => ParseInventory(root),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static SkuaMessage? ParseStatus(JsonElement root)
    {
        if (!root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("running", out var running) || running.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        DateTimeOffset? since = null;
        if (Str(root, "runningSince") is { } text)
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return null;
            }

            since = parsed;
        }

        var control = Optional(() => Str(root, "control"));
        return new StatusMessage(new SkuaStatus(
            loggedIn.GetBoolean(), CutOrNull(Str(root, "character")), CutOrNull(Str(root, "server")), CutOrNull(Str(root, "map")), CutOrNull(Str(root, "script")),
            running.GetBoolean(), since, ParseQuests(root), ParseFactions(root),
            control is not null && SkuaProtocol.ControlStates.Contains(control) ? control : null,
            Count(root, "settleSecondsLeft"),
            IsTrue(root, "optionsStuck"),
            Optional(() => ParseProfile(root)),
            Optional(() => ParseBoosts(root))));
    }

    /// <summary>
    /// The profile, or null when absent or damaged (a required number missing, negative or out of range, or no member flag):
    /// only the profile is lost, never the status. An unusable optional part is read as not reported.
    /// </summary>
    private static SkuaProfile? ParseProfile(JsonElement root)
    {
        if (!root.TryGetProperty("profile", out var p) || p.ValueKind != JsonValueKind.Object
            || Count(p, "level") is not { } level || Whole(p, "xp") is not { } xp || Whole(p, "xpToLevel") is not { } toLevel
            || Whole(p, "gold") is not { } gold || Count(p, "coins") is not { } coins || Count(p, "classRank") is not { } rank
            || !p.TryGetProperty("isMember", out var member) || member.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return new SkuaProfile(
            level, xp, toLevel, gold, coins, rank, member.GetBoolean(),
            OptionalText(p, "className", SkuaProtocol.MaxTextLength), Count(p, "classId"), Count(p, "classPoints"),
            OptionalText(p, "guild", SkuaProtocol.MaxTextLength), Count(p, "bagSlots"), Count(p, "bankSlots"), Count(p, "houseSlots"));
    }

    /// <summary>The boosts, or null when absent or damaged (a value below -1 or not a whole number, or no readAt): only the boosts are lost, never the status.</summary>
    private static SkuaBoosts? ParseBoosts(JsonElement root)
    {
        if (!root.TryGetProperty("boosts", out var b) || b.ValueKind != JsonValueKind.Object
            || BoostValue(b, "gold") is not { } gold || BoostValue(b, "class") is not { } cls
            || BoostValue(b, "rep") is not { } rep || BoostValue(b, "xp") is not { } xp || Whole(b, "readAt") is not { } readAt)
        {
            return null;
        }

        return new SkuaBoosts(gold, cls, rep, xp, readAt, ParseList(b, "items", SkuaProtocol.MaxBoostItems, ParseBoostItem));
    }

    /// <summary>A whole number from -1 (<see cref="SkuaProtocol.BoostTimeUnknown"/>) to the int32 maximum, or null.</summary>
    private static int? BoostValue(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value) && value >= SkuaProtocol.BoostTimeUnknown ? value : null;

    private static SkuaBoostItem? ParseBoostItem(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || Count(entry, "itemId") is not { } id || id <= 0 || NonEmpty(entry, "name") is not { } name || Count(entry, "quantity") is not { } quantity)
        {
            return null;
        }

        var type = Str(entry, "type");
        return new SkuaBoostItem(id, Cut(name, SkuaProtocol.MaxBoostNameLength), type is not null && SkuaProtocol.BoostTypes.Contains(type) ? type : "unknown", quantity);
    }

    /// <summary>A whole number from 0 to <see cref="long.MaxValue"/>, or null.</summary>
    private static long? Whole(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var value) && value >= 0 ? value : null;

    /// <summary>
    /// The inventory, or null without a <c>read</c> and an <c>items</c> array. Unknown containers are dropped from the read list;
    /// a damaged item, or one in a container not read, is dropped on its own; at most <see cref="SkuaProtocol.MaxInventoryItems"/>
    /// per container are kept and a cut sets truncated.
    /// </summary>
    private static SkuaMessage? ParseInventory(JsonElement root)
    {
        if (!root.TryGetProperty("read", out var readArray) || readArray.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("items", out var itemsArray) || itemsArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var read = new List<string>();
        foreach (var entry in readArray.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String && Optional(() => entry.GetString()) is { } container
                && SkuaProtocol.InventoryContainers.Contains(container) && !read.Contains(container))
            {
                read.Add(container);
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var items = new List<SkuaItem>();
        var cut = false;
        foreach (var entry in itemsArray.EnumerateArray())
        {
            if (ParseItem(entry) is not { } item || !read.Contains(item.Where))
            {
                continue;
            }

            var n = counts.GetValueOrDefault(item.Where);
            if (n >= SkuaProtocol.MaxInventoryItems)
            {
                cut = true;
                continue;
            }

            counts[item.Where] = n + 1;
            items.Add(item);
        }

        return new InventoryMessage(new SkuaInventory(Optional(() => NonEmpty(root, "character")), read, items, cut || IsTrue(root, "truncated")));
    }

    private static SkuaItem? ParseItem(JsonElement entry)
    {
        try
        {
            return entry.ValueKind == JsonValueKind.Object && NonEmpty(entry, "name") is { } name && Count(entry, "qty") is { } qty && Str(entry, "where") is { } where
                ? new SkuaItem(name, qty, where)
                : null;
        }
        catch (InvalidOperationException)
        {
            return null; // an escaped lone surrogate in the name: only this item is lost
        }
    }

    private static SkuaMessage? ParseResult(JsonElement root)
    {
        if (NonEmpty(root, "reqId") is not { } reqId
            || !root.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return new ResultMessage(
            reqId, ok.GetBoolean(), Optional(() => CutOrNull(Str(root, "error"))),
            ParseList(root, "scripts", SkuaProtocol.MaxScripts, ParseScript),
            ParseList(root, "optionDefs", int.MaxValue, ParseOptionDef),
            ParseList(root, "options", int.MaxValue, ParseValue),
            IsTrue(root, "hasOptions"), IsTrue(root, "firstRun"),
            IsTrue(root, "truncated")
            || (root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array && scripts.GetArrayLength() > SkuaProtocol.MaxScripts));
    }

    /// <summary>
    /// The list, or null when the field is absent or not an array. A damaged entry is left out on its own (an escaped lone
    /// surrogate included); at most <paramref name="max"/> are kept.
    /// </summary>
    private static List<T>? ParseList<T>(JsonElement root, string name, int max, Func<JsonElement, T?> parse)
        where T : class
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<T>();
        foreach (var entry in array.EnumerateArray())
        {
            if (list.Count >= max)
            {
                break;
            }

            if (Optional(() => parse(entry)) is { } item)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static SkuaScriptInfo? ParseScript(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && NonEmpty(entry, "id") is { } id && Str(entry, "name") is { } name && Str(entry, "folder") is { } folder
            ? new SkuaScriptInfo(id, Cut(name), Cut(folder), OptionalText(entry, "title", SkuaProtocol.MaxScriptTitleLength),
                OptionalText(entry, "description", SkuaProtocol.MaxScriptDescriptionLength), OptionalText(entry, "tags", SkuaProtocol.MaxScriptTagsLength))
            : null;

    /// <summary>A string field cut to <paramref name="max"/>; null when absent, not a string, empty or unreadable (the entry is kept).</summary>
    private static string? OptionalText(JsonElement entry, string name, int max)
    {
        try
        {
            return Str(entry, name) is { } text && Cut(text, max) is { Length: > 0 } cut ? cut : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static SkuaOptionValue? ParseValue(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && Str(entry, "category") is { } category && NonEmpty(entry, "name") is { } name
        && Str(entry, "value") is { Length: <= SkuaProtocol.MaxOptionValueLength } value
            ? new SkuaOptionValue(Cut(category), name, value)
            : null;

    private static SkuaOptionDef? ParseOptionDef(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || Str(entry, "category") is not { } category
            || NonEmpty(entry, "name") is not { } name
            || Str(entry, "displayName") is not { } displayName
            || Str(entry, "description") is not { } description
            || Str(entry, "type") is not { } type
            || Str(entry, "default") is not { } defaultValue)
        {
            return null;
        }

        return new SkuaOptionDef(
            Cut(category), name, Cut(displayName), Cut(description), OptionType(type), Cut(defaultValue, SkuaProtocol.MaxOptionValueLength),
            ParseList(entry, "choices", int.MaxValue, ParseChoice));
    }

    private static string? ParseChoice(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = entry.GetString();
        return text is null || text.Contains('\uFFFD') || text.Length > SkuaProtocol.MaxOptionValueLength ? null : text;
    }

    /// <summary>A non-empty string value, cut, or null.</summary>
    private static string? NonEmpty(JsonElement root, string name) => Str(root, name) is { Length: > 0 } text ? Cut(text) : null;

    private static bool IsTrue(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Reads an optional part: an escaped lone surrogate in it (<c>GetString</c> throws <see cref="InvalidOperationException"/>)
    /// loses only that part, never the line.
    /// </summary>
    private static T? Optional<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The active quests, or null when the field is absent or not an array (an older plugin, or damage: the rest of the
    /// status still counts). A damaged entry is left out on its own; at most <see cref="SkuaProtocol.MaxQuests"/> are kept.
    /// </summary>
    private static IReadOnlyList<SkuaQuest>? ParseQuests(JsonElement root)
    {
        if (!root.TryGetProperty("quests", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var quests = new List<SkuaQuest>();
        foreach (var entry in array.EnumerateArray())
        {
            if (quests.Count >= SkuaProtocol.MaxQuests)
            {
                break;
            }

            if (ParseQuest(entry) is { } quest)
            {
                quests.Add(quest);
            }
        }

        return quests;
    }

    private static SkuaQuest? ParseQuest(JsonElement entry)
    {
        try
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var questId) || questId < 0
                || Str(entry, "name") is not { } name
                || !entry.TryGetProperty("ready", out var ready) || ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return new SkuaQuest(questId, Cut(name), ready.GetBoolean());
        }
        catch (InvalidOperationException)
        {
            // An escaped lone surrogate in the name: only this entry is lost.
            return null;
        }
    }

    /// <summary>
    /// The factions, or null when the field is absent or not an array (an older plugin, or damage). A damaged entry (a
    /// number that is missing, negative, fractional or outside int32, a missing or unusable name) is left out on its own; at
    /// most <see cref="SkuaProtocol.MaxFactions"/> are kept.
    /// </summary>
    private static IReadOnlyList<SkuaFaction>? ParseFactions(JsonElement root)
    {
        if (!root.TryGetProperty("factions", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var factions = new List<SkuaFaction>();
        foreach (var entry in array.EnumerateArray())
        {
            if (factions.Count >= SkuaProtocol.MaxFactions)
            {
                break;
            }

            if (ParseFaction(entry) is { } faction)
            {
                factions.Add(faction);
            }
        }

        return factions;
    }

    private static SkuaFaction? ParseFaction(JsonElement entry)
    {
        try
        {
            if (entry.ValueKind != JsonValueKind.Object
                || Count(entry, "id") is not { } id
                || Str(entry, "name") is not { } name
                || Count(entry, "rank") is not { } rank
                || Count(entry, "totalRep") is not { } totalRep
                || Count(entry, "rep") is not { } rep
                || Count(entry, "requiredRep") is not { } requiredRep)
            {
                return null;
            }

            return new SkuaFaction(id, Cut(name), rank, totalRep, rep, requiredRep);
        }
        catch (InvalidOperationException)
        {
            // An escaped lone surrogate in the name: only this entry is lost.
            return null;
        }
    }

    /// <summary>A whole number from 0 to <see cref="int.MaxValue"/>, or null.</summary>
    private static int? Count(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value) && value >= 0 ? value : null;

    private static SkuaMessage? ParseLog(JsonElement root)
    {
        if (Str(root, "at") is not { } at
            || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            || Str(root, "kind") is not { } kindText
            || Array.IndexOf(Enum.GetNames<SkuaLogKind>(), kindText) < 0
            || Str(root, "text") is not { } body)
        {
            return null;
        }

        var kind = Enum.Parse<SkuaLogKind>(kindText);
        var item = LogItem(root);
        var count = item is not null && root.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number
            && c.TryGetInt32(out var n) && n is >= 1 and <= SkuaProtocol.MaxCount ? n : 1;
        return new LogMessage(new SkuaLogEntry(time, kind, Cut(body), item, count));
    }

    /// <summary>The optional drop item, or null when absent or unusable: only the item and its count are lost, never the line.</summary>
    private static string? LogItem(JsonElement root)
    {
        try
        {
            return CutOrNull(Str(root, "item"));
        }
        catch (InvalidOperationException)
        {
            // An escaped lone surrogate in the item.
            return null;
        }
    }

    /// <summary>
    /// A string value, or null when absent, not a string, or containing U+FFFD (which the plugin never sends). An escaped
    /// lone surrogate (<c>\ud800</c>) makes <c>GetString</c> throw <see cref="InvalidOperationException"/>: the caller
    /// catches it (<see cref="Parse"/> for the whole line, <c>ParseQuest</c> for one quest entry).
    /// </summary>
    private static string? Str(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = p.GetString();
        return text is null || text.Contains('\uFFFD') ? null : text;
    }

    /// <summary>Written only when there is text (cut to <paramref name="max"/>); absent otherwise.</summary>
    private static void WriteOptional(Utf8JsonWriter w, string name, string? value, int max)
    {
        if (value is not null && Cut(value, max) is { Length: > 0 } cut)
        {
            w.WriteString(name, cut);
        }
    }

    private static void WriteNullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
        {
            w.WriteNull(name);
        }
        else
        {
            w.WriteString(name, Cut(value));
        }
    }

    /// <summary>
    /// Every string goes through here on both sides: lone surrogates are removed first (the writer would throw on one, see
    /// <see cref="SkuaText.WithoutLoneSurrogates"/>), then the text is cut to <see cref="SkuaProtocol.MaxTextLength"/>
    /// without splitting a pair.
    /// </summary>
    private static string Cut(string value) => Cut(value, SkuaProtocol.MaxTextLength);

    private static string Cut(string value, int max) => SkuaText.Cut(value, max);

    private static string? CutOrNull(string? value) => value is null ? null : Cut(value);
}
