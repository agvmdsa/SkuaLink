# SkuaLink.SkuaBridge

A Skua plugin. It runs inside Skua 1.4.5.0, reports Skua's state and a short event log to SkuaLink over a local named
pipe (`SkuaLink.SkuaBridge.v1`, current Windows user only, two-way since AQW-0088), and accepts a fixed set of commands
from SkuaLink. Status: **connect and status verified by the owner on Windows on 2026-11-11 with an earlier build; the
quest reading, the re-attach across script runs, the commands, the options handling, several Skua at once and the
profile and inventory reads (0.6.0) are not yet verified** (only the owner's Windows can prove it). Since 0.6.0 the plugin
is SkuaLink's only data source, and SkuaLink runs as the normal user.

## What the plugin can do

Exactly this, nothing else:

1. **Report** (as before): `hello`, `status` and `log` lines, plus (AQW-0110) the character's profile and its inventory,
   see "What it sends":
   - **Profile**, in the status while logged in: level, experience and what the level needs, gold, coins, class name, class
     rank and class points (from the equipped class item), membership, guild, and the bag, bank and house slot counts.
   - **Inventory**, its own `inventory` line: the names and quantities of the items in the bag, the house and the bank
     (the bank only once it was opened in Skua this session; the plugin never opens it), at most 5000 items per container
     and 1 MB per line (`truncated` when cut), latest-only in the queue and never replayed after a reconnect.
   - **When:** a pass runs on the 3 s poll, only while SkuaLink is connected and a character is logged in: at once after a
     login, a character change or a connect; after an item event (bought, sold, dropped, quest turned in, added to the
     bank; these fire only while a script runs) once the gap since the last pass has passed (5 s, or 10 times the last
     pass's duration, at most 5 min); and every 5 min otherwise. See "Cost of the inventory reads".
2. **List scripts** (`listScripts`): the `.cs` files under Skua's scripts folder (`ClientFileSources.SkuaScriptsDIR`,
   `%AppData%\Skua\Scripts`), except Skua's compile cache `Cached-Scripts`, hidden/system files and links, sorted by
   folder then name, at most `SkuaProtocol.MaxScripts` (the answer says `truncated` when cut). Each script travels as
   `{id, name, folder, title?, description?, tags?}`: `id` is the first 8 bytes of the SHA-256 of the path relative to the scripts folder, lowercase
   hex; no full path leaves the plugin. Libraries are not listed: a file name starting with `Core` (case-insensitive,
   prefix only), any file under a folder named `Core`, and any file without `void ScriptMain(` (read once, at most 2 MB,
   cached by size and write time; a read error lists the file).
   The same read parses the header comment most bots start with (`/* name: ... description: ... tags: ... */`, only the
   first block at the top of the file, first 4 KB, one line per value): `name` travels as `title` (cut to 100 chars),
   `description` (200) and `tags` (120), control characters replaced by a space. A script without that header
   travels without them and the phone shows its file name. With descriptions the list of 2000 scripts is about
   600 KB, so the result line cap is 1 MB.
3. **Start a listed script** (`start {id}`): only an id from the plugin's own listing (an id it has not listed yet gets one
   fresh scan). The path is checked again (inside the scripts folder plus a separator, ends in `.cs`, not under
   `Cached-Scripts`, neither the file nor a folder on the way a link) and must still exist. Refused while a script runs
   (`already running`) or while `stopping` / `settling`; only then `SetLoadedScript(path)` and `await StartScript()`. The
   error Skua returns (compile error, version error) becomes the answer's `error`, absolute paths reduced to file names,
   cut to 300 chars.
4. **Stop the running script** (`stop`): answers at once, then calls Skua's `StopScript()` on a pool thread (never on the
   script thread), once at a time. The status shows `stopping`, then `settling`, then `idle`.
5. **Read a script's options** (`getOptions {id}`): the definitions the script declares and the values saved in its
   options file (see "Script options").
6. **Write a script's options** (`setOptions {id, values}`): only names the script declares, only values that fit their
   type, only into that script's options file inside Skua's options folder, only while no script runs.
7. **Close a started script's options dialog** on the PC (part of `start`), so Skua saves the values already in the file.
8. **Use a boost** (`useBoost {itemId}`): refused unless the character is logged in ("The character is not logged in."),
   the id is in the plugin's own last boost list with a quantity above 0 ("That boost is not in the bag."), its type is
   gold, class, reputation or XP ("That boost is not a gold, class, reputation or XP boost."), the four values can be read
   ("Could not read the boosts.") and that type is not already active ("A boost of that type is already active."). Then it
   sends `serverUseItem` to the game through `Boosts.UseBoost(itemId)`, once, never retrying, and re-reads the four values
   every 500 ms for up to 3 s: it answers ok at the first value above 0, else "The game did not activate the boost.". It
   never calls `GetBoostID` (and never with a bank search, which would load the bank).
9. **Log in again** (`relogin`): refused while the character is logged in ("The character is already logged in."), within
   15 s of the last attempt ("Wait a moment before trying again."), while an earlier login is still running ("A login is
   already in progress.") and when Skua has no last server ("There is no server to log in to."). Otherwise it answers ok at
   once and calls `Servers.Relogin(Servers.LastName)` on a pool thread (it can block for about 30 s). That is Skua's own
   login with the credentials Skua already holds: the plugin never reads, stores or sends a password.

It never accepts a path, code or a script name from SkuaLink, and option values only for options the script itself
declared; nothing but an item id for a boost. The only things that reach the game are `Boosts.UseBoost(itemId)` and Skua's
own login, each only on a command, never automatically.

## Corrections to the plan (verified against the 1.4.5.0 DLLs and the 1.4.5.0 tag source)

Every member below was compiled against `lib/skua/Skua.Core.Interfaces.dll` and `Skua.Core.Models.dll` (1.4.5.0) in a
throwaway project and read in the tag source.

1. **`bot.Manager` is not `INotifyPropertyChanged`.** `IScriptInterface.Manager` is typed `IScriptStatus`, which has no
   change notification (assigning it to `INotifyPropertyChanged` does not compile). `IScriptManager : IScriptStatus,
   INotifyPropertyChanged` is the notifying type. The plugin asks the provider for `IScriptManager` (Skua registers
   `IScriptManager` and `IScriptInterface` as singletons in `Skua.Core/AppStartup/Services.cs`, and `ScriptInterface`
   exposes that same instance as `Manager`), with `bot.Manager as INotifyPropertyChanged` as a fallback.
   `ScriptManager` raises `PropertyChanged` for `ScriptRunning` and `LoadedScript` through `[ObservableProperty]`, and
   raises `ScriptRunning` a second time at the end of `StopScript`, so the plugin only acts on real transitions.
2. **`ItemBase` has no `CharItemID`.** It is on `InventoryItem : ItemBase`. The inventory is enumerated with
   `IScriptInventory.Items` (`List<InventoryItem>`, inherited from `IItemContainer<InventoryItem>` through
   `ICanEquip -> ICheckEquipped -> ICanBank -> ICheckInventory<InventoryItem>`). `ID`, `Name`, `Quantity` are on `ItemBase`.
   The snapshot is keyed by `CharItemID`, because `ItemBought` and `ItemSold` carry the CharItemID.
3. **Skua drops plugin handlers on every script stop.** `ScriptEvent` handles `ScriptStoppedMessage` with
   `ClearHandlers()`, which sets `PlayerDeath`, `QuestAccepted`, `QuestTurnedIn`, `MapChanged`, `ReloginTriggered`,
   `ItemDropped` and `ScriptStopping` (and others) to null. The plugin therefore re-attaches its handlers on each
   `ScriptRunning` notification (remove, then add, under a lock, so there is exactly one copy; done on every
   notification, not only on detected transitions). The stop message is sent before `ScriptRunning = false` on the same
   thread, so the re-attach runs after the clear. A script that itself calls `Bot.Events.ClearHandlers()` silences the
   plugin's log lines until the next script start or stop.
4. **Game events only arrive while a script runs.** `ScriptEvent` registers for the game messages on
   `ScriptStartedMessage` and unregisters on `ScriptStoppedMessage`. A login, logout or map change by hand raises no
   event, so the status is re-read every 3 s on the plugin's own background task and sent only when it changed. The log
   events (bought, sold, dropped, map, died, logged out, relogin) are therefore only reported during scripts.
5. **`ScriptStopping` return value.** The handler returns `bool`. Skua invokes the multicast delegate inside
   `Task.Run` and the last handler's value goes back to `ScriptManager`, which uses it only to choose a `Trace.WriteLine`
   ("Script finished successfully." / "Script finished early or with errors."); nothing else reads it. The plugin
   returns `true` (the delegate's documented "the script should be stopped", which is what happens anyway). Because the
   plugin re-attaches when `ScriptRunning` turns true, before `ScriptMain` runs, a script's own handler is added after
   the plugin's and its value is the one Skua sees.
6. **Script started/stopped come from `ScriptRunning` changes**, not from Skua's `ScriptStartedMessage` /
   `ScriptStoppedMessage` as the spec suggested, so `Skua.Core.dll` is not referenced.
7. **Active quests (AQW-0076):** `IScriptInterface.Quests` -> `IScriptQuest.Active` (`List<Quest>`, never null in
   1.4.5.0: it is `Tree.FindAll(x => x.Active)`, `Quest.Active` is `Status != null`, and the `world.questTree` binding
   answers an empty dictionary when the read fails), with `Quest.ID`, `Quest.Name` and `Quest.Status`. "Ready" is
   `Quest.Status == "c"` on the quests that one read returned: Skua's own `CanComplete(id)` is
   `Completed.Contains(q => q.ID == id)` with `Completed = Tree.FindAll(x => x.Status == "c")`, so this is the same
   check without a second tree read (Skua's remark on it: "can not always be right"). `CanComplete` is not called. The
   `QuestAccepted` and `QuestTurnedIn` subscriptions and the `IScriptQuest.Tree` name lookup were removed: the active
   list in the status replaces those two log lines. AQW-0110 subscribes to `QuestTurnedIn` again, only to ask for an
   inventory pass (no log line).
8. **Faction reputation (AQW-0081):** `IScriptInterface.Reputation` -> `IScriptFaction.FactionList` (`List<Faction>`).
   In 1.4.5.0 it is generated from `[ObjectBinding("world.myAvatar.factions", Default = "new()", HasSetter = false)]`:
   one `Flash.GetGameObject<List<Faction>>("world.myAvatar.factions")` call (`getGameObject`, one Flash call), a fresh
   deserialized list per read, `new()` when the answer is null or anything throws (so it never throws and never answers
   null; logged out it answers an empty list). It touches no Skua cache. `Faction.ID` (FactionID), `Name` (sName),
   `Rank` (iRank), `TotalRep` (iRep), `Rep` (iSpillRep, earned inside the current rank) and `RequiredRep` (iRepToRank,
   the size of the current rank step, 0 at rank 10) are read; `RemainingRep` is not.
9. `IScriptInterface.Version` exists (Skua's version from its settings) but is not used: `hello` reports
   `Assembly.GetEntryAssembly()?.GetName().Version` as decided.

## What it sends

- `hello` on every (re)connect: plugin version (informational version without build metadata), Skua's version, and
  `instanceId`, a random GUID made once per plugin load (SkuaLink tells several Skua apart by it).
- `status` on every (re)connect and whenever it changes: logged in; character, server, map (only while logged in);
  script file name without folder or extension (no local path leaves the plugin); running; running since; the active
  quests (`quests`: id, name, ready to turn in; at most 30, in Skua's order, a blank name sent as `#id`; empty while
  logged out; re-read at most every 15 s, see below); the faction reputation (`factions`: id, name, rank, total rep,
  rep inside the rank, size of the rank step; at most 60, in Skua's order, a blank name sent as `#id`; empty while
  logged out; read on every status, so about every 3 s). A change in either list, a ready flag or a reputation number
  sends a new status; nothing changed sends nothing. Also `control` (`idle`, `running`, `stopping`, `settling`,
  `awaitingOptions`) with `settleSecondsLeft` while settling and `optionsStuck` (see "Commands and control state").
  Also `profile` (AQW-0110), only while logged in, see below, and `boosts`, only while logged in, see below.
- `status.boosts`: `gold`, `class`, `rep`, `xp` are the seconds left as of `readAt` (unix ms): 0 inactive, -1 active with the
  elapsed time unknown, otherwise a positive number; plus `items`, the boost items of the bag (item id, name, type, quantity;
  at most 12). Four plain reads per poll (about every 3 s), one Flash call each: `world.myAvatar.objData.iBoostG`,
  `iBoostCP`, `iBoostRep`, `iBoostXP`, through `Flash.GetGameObject<int>`. A read that throws gives no reading (the last
  values stay); Skua's getter answers 0 for a value it cannot read and offers no way to tell that from a real 0, so a drop
  to 0 is believed only on the second read in a row (about 3 s later). The sampler is reset when another character logs in
  or after three polls in a row see the character logged out, not on a single logged-out poll (a map change). Sent only
  when a value or the item list changed (and after a reconnect).
- `status.profile` (AQW-0110): the character's numbers, read on every status (eight plain getters of `IScriptPlayer`, one
  Flash call each; sent only when something in the status changed, as before):
  - `level` = `Player.Level` (`world.myAvatar.dataLeaf.intLevel`), `xp` = `XP` (`objData.intExp`), `xpToLevel` =
    `RequiredXP` (`objData.intExpToLevel`), `gold` = `Gold` (`objData.intGold`), `coins` = `Coins` (`objData.intCoins`),
    `guild` = `Guild` (`objData.guild.Name`, blank not sent), `classRank` = `CurrentClassRank` (`objData.iRank`),
    `isMember` = `IsMember` (`objData.iUpgDays >= 0`). Negative numbers are sent as 0.
  - `className`, `classId`, `classPoints`: the equipped item of category (`sType`) `Class` in the plugin's last inventory
    pass (name, `ItemID`, quantity `iQty`). Skua has no class points property; `Player.CurrentClass` is never called
    because every read of it reads the whole inventory again. That `iQty` of a class item is its class points is
    unverified (U1). Not sent before the first inventory pass; a class changed by hand shows with the next pass.
  - `bagSlots`, `bankSlots`, `houseSlots`: `Inventory.Slots` (`objData.iBagSlots`), `Bank.Slots` (`objData.iBankSlots`),
    `House.Slots` (`objData.iHouseSlots`), read once per inventory pass (one small Flash call each), 0 not sent.
  - A failed read keeps the last profile (none is sent before the first good read). Skua's getters catch a failed Flash
    call and return the default instead of throwing, so a failed read mostly arrives as zeros (for example `dataLeaf`
    during a map join): a reading whose level is 0 or less counts as failed. A logout or another character clears it (and the class, slots and the last
    inventory sent), so nothing of one character is reported for another. No `profile` is sent while the character is
    still loading: Skua reports logged in (socket connected) before the game loaded the bag, so the profile appears once
    an inventory pass found a bag, or earlier when the level already reads above 0.
- `inventory` (AQW-0110): `{character, read, items: [{name, qty, where}], truncated?}`. `where` is `inv`, `house` or
  `bank`; `read` lists the containers actually read: the bag always, the house when its list came back (an empty one
  too; not when the read returned nothing, a failed Flash call or not loaded yet, so SkuaLink keeps the house it knew), the bank only when
  its read returned at least one item (`world.bankinfo.items` fills only once the bank was opened in Skua this session;
  the plugin never opens it), so SkuaLink replaces only those and keeps the bank it knew. After a switch to another
  character in the same Skua, a bank read equal to the last bank read of the previous character does not count (Skua may
  keep `world.bankinfo.items` across the relogin, U3); a bank read with other items counts for the new character. Names trimmed, blank names
  skipped, negative quantities 0, at most 5000 items per container (`truncated` when cut); a line over 1 MB is cut by the
  protocol (the bank first, it comes last). An empty bag sends nothing (not logged in or not loaded). Sent only when the
  snapshot changed, and once more after every (re)connect. In the pipe queue it is latest-only, like the status (one
  marker, the newest line written), and never replayed after a reconnect: a line not yet written when the connection
  drops is discarded (and one built while disconnected is not queued at all); the plugin reads again on connect. The
  character name is read before and after the three containers; a pass during which it changed is dropped. When it is
  read: see "Cost of the inventory reads".
- `result` once per command (see below).
- `log`: bought (quantity = now minus snapshot when positive, else none), sold (name from the snapshot taken before the
  sale), dropped (`Drop: X` for an item offered in the drop list, Skua's `dropItem`, the usual farming case; `Received: X`
  for an item put straight into the inventory, Skua's `addItems`, such as a reward; accepting a drop-list item raises no
  further event, so each drop is one line; a drop whose name is known also carries `item` and `count` 1, and SkuaLink
  merges repeated drops of one item into one line, `Drop: X x13`, while each comes within 60 s of the last), map changed, died, logged out, relogin, script started, script stopped,
  script error (the exception's message with every absolute path reduced to its file name, so no local folder and its
  Windows user name leaves the plugin). Quest accepted / turned in are no longer sent (the active quests travel in the status instead); the two
  kinds stay in the protocol so logs kept by an older version still load.
- Never: monster kills (`MonsterKilled` is not subscribed), the temporary inventory, item details beyond name and quantity.

## Commands and control state (AQW-0088)

- The pipe is opened `InOut`. The plugin reads only through the shared bounded reader (`SkuaCommandReader` over
  `BoundedLineReader` in `src/AqwCompanion.Skua`): a line over 4 KB (`SkuaProtocol.MaxCommandLineBytes`) is dropped, as
  is an unknown `type`, a line of another version, anything that is not a command, and anything malformed; unknown fields
  are ignored. The reader never throws; when SkuaLink closes the pipe it ends and the connection is made again like
  after a failed write. An older SkuaLink serves an inbound-only pipe, which Windows refuses to a client that asks to read
  (access denied): the plugin then connects outbound only, as before, and reads nothing.
- Each command goes to one command worker (queue of 16; one more is answered `busy` at once) and is answered exactly once
  with a `result {reqId, ok, error?, scripts?, optionDefs?, options?, hasOptions?, firstRun?, truncated?}`. Every failure is `ok: false` with a short error:
  `unknown script id`, `not found`, `already running`, `stopping`, `settling`, `not running`, `could not list scripts`,
  `busy`, `options not known yet`, `options file not allowed`, `could not read options`, `could not write options`, a
  validation text (`unknown option X`, `not a valid int for X`, ...), or Skua's own start error. Nothing reaches Skua as an exception.
- The decisions live in `src/AqwCompanion.Skua/SkuaCommandHandler.cs` (unit-tested on the Mac with a fake Skua); the
  Skua-bound part is `ScriptControl.cs`. Commands run one at a time, so a start's check and its acceptance cannot
  interleave with another start or stop.
- `SetLoadedScript` is never called while a script runs (Skua restarts the loaded path by itself on restart and relogin).
- After a successful start, `hasOptions` = the script declares at least one option (`Config.Options` /
  `Config.MultipleOptions`, read after the start); the state becomes `awaitingOptions` with options, `running` without
  (see "Script options" for how `awaitingOptions` ends). A start Skua has not reported running after 10 s (its running
  report may come and go before the start is accepted) settles, so the state never sticks.
- A stop accepted before Skua reported the start (state `running` or `awaitingOptions`, Skua not yet running) moves to
  `stopping` (a poll may settle it, since nothing runs yet); the stop is remembered until that start's 10 s wait ends, and
  when Skua reports the script running (its running change, or the 3 s poll) the plugin calls `StopScript()` once and the
  state goes back to `stopping`, so a script never runs on after an accepted stop. The same holds for a discovery start's
  own stop. A script started by hand in Skua after that wait is not stopped.
- `ScriptRunning` is read inside the control state's lock both by the running event and by the poll, so an older read
  can never overwrite a newer one.
- Settling (`SkuaControlState`): when the script stops running the state is `settling` until the map has not changed for
  10 s, then `idle`. Skua raises `MapChanged` only while a script runs, so during the settle the map is compared on the
  3 s status poll (each change restarts the 10 s). `ScriptRunning` changes and the poll both feed the state; the poll
  (`SkuaCommandHandler.Tick`) also runs while SkuaLink is away (it reads only `ScriptRunning`, no Flash).

## Skua's script loader view (AQW-0108)

Skua's "Load Script" view (`ScriptLoaderViewModel` in Skua.Core) keeps its own file name (`LoadedScript`) and status text
(`ScriptStatus`), changed only by Skua's messenger messages, never by `IScriptManager`. The plugin loads and starts
through `IScriptManager`, so before AQW-0108 the Skua window kept showing the script loaded before a phone start (for
example a Hollowborn bot started by hand, then stopped and replaced from the phone).

- **What is synced.** Right before its own `SetLoadedScript(path)` (every phone start, also a discovery start and the real
  start after it), the plugin sends Skua's own `LoadScriptMessage(path)` on the `ScriptStatus` channel, as Skua's script
  repository does (`StrongReferenceMessenger.Default.Send<LoadScriptMessage, int>(new(path), (int)MessageChannels.ScriptStatus)`).
  The view's handler sets the loaded path (the same call the plugin makes next), the file name and the text
  "[Script loaded]". Stop needs nothing new: Skua's `ScriptManager` itself sends `ScriptStoppingMessage` and
  `ScriptStoppedMessage` on every stop, also one the plugin asks for, and the view shows "Stopping..." and "[Stopped]".
  A discovery start therefore ends with the new name and "[Stopped]".
- **The status text while running.** Skua sets "[Running]" only inside its own start path (the view's start button or
  `StartScriptMessage`), and no message sets it alone. `StartScriptMessage` is not sent: its handler asks with dialogs,
  starts the script itself without waiting (`async void`), and would bypass the plugin's error answer, discovery and
  options handling. So after a phone start the Skua window shows the new script's name with "[Script loaded]" while it runs.
- **How.** The plugin references neither Skua.Core nor CommunityToolkit: `SkuaMessengerBridge` (in
  `src/AqwCompanion.Skua`, unit-tested on the Mac with stand-in types) finds `Skua.Core.Messaging.LoadScriptMessage`,
  `Skua.Core.Messaging.MessageChannels.ScriptStatus` and `CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default`
  with its instance `Send<TMessage, TToken>(TMessage, TToken)` by name among the loaded assemblies, and keeps the result.
  Anything missing, and any exception (also from a handler), means "not sent": the start goes on exactly as before. An
  empty path is never sent (the view would open its file dialog). The send runs on the command worker, not through
  `IDispatcherService`: Skua itself changes the same view from non-UI threads (`ScriptStartedMessage` from the thread
  that calls `StartScript`, the stop messages from the script thread), and WPF passes such property changes to the UI.
- **Other receivers.** In Skua 1.4.5.0 the only other `LoadScriptMessage` receiver is the account manager
  (`AccountManagerViewModel`, which shows a "Script loaded" box), registered only in the separate Skua Manager app, not
  in the Skua client the plugin runs in.

## Script options (AQW-0089)

From the Skua 1.4.5.0 source: a script declares its options in fields (`Options`, `MultiOptions`, `OptionsStorage`,
`DontPreconfigure`). On every start `ScriptManager.LoadScriptConfig` puts a new `Config` (`IScriptOptionContainer`) in
place with those options, loads the values from the options file `Config.OptionsFile` =
`%AppData%\Skua\options\{OptionsStorage}.cfg` (`ClientFileSources.SkuaOptionsDIR`; `default.cfg` without
`OptionsStorage`), and, for a script with `Options` and no `DontPreconfigure = true`, calls `Config.Configure()` on a pool
task once the script thread runs: it loads the file again and shows the modal options dialog (`HostDialog`, `DataContext`
= `OptionContainerViewModel`) with `ShowDialog` on Skua's UI thread. When that dialog closes in any way, Skua saves the
values it shows. The script thread already runs meanwhile, with the values loaded from the file. File format: one line
per option, `Options:Name=Value` for `Options` and `Category:Name=Value` for each `MultiOptions` list (the category is
the field name); enum values with spaces for underscores (Skua turns them back when reading).

How the plugin uses that (decisions in `src/AqwCompanion.Skua/SkuaCommandHandler.cs` and `SkuaScriptOptions.cs`,
unit-tested; the Skua-bound part is `ScriptOptions.cs`):

- **Definitions are learnt at a start.** Right after the plugin's own `StartScript()` returns, `Config` describes that
  script (only kept when `LoadedScript` is still the path just started). The plugin keeps, per script id and for this
  plugin load, the definitions (category, name, display name, description, type mapped to
  `bool|int|double|string|enum|other`, default, enum members) and `Config.OptionsFile`. They are not kept across a Skua
  restart or a plugin reload (SkuaLink keeps its own copy of the definitions).
- **Options file without learnt definitions (fallback):** the plugin finds the script's options file from its source:
  a plain `public string OptionsStorage = "Name";` line (the public field Skua reads; uncommented, a literal, one value,
  a safe file name) gives `%AppData%\Skua\options\Name.cfg`. The source is only read (at most 4 MB), never run. A script
  whose storage name cannot be read this way has no fallback (Skua's `default.cfg` is shared by every script without
  `OptionsStorage`, so it is never assumed).
- **`getOptions`:** learnt: `optionDefs` (empty for a script without options) and the saved values from the options file
  (only options the script declares, in its order; no file is no values). Not learnt but the options file exists: `ok`
  with no `optionDefs` and the file's values as they are written (no type is known, so enum underscores and the PC's
  decimal comma are not converted). Neither: `ok` with no `optionDefs` and no values, which means "options not known
  yet": the next start is a discovery and the phone says the first start uses the default options.
- **`setOptions`:** refused while a script runs, starts or stops (Skua reads the file at the start, and an open dialog
  would overwrite it when it closes); refused before the definitions are learnt (`options not known yet`); every value is
  checked with `SkuaOptionsFile.Validate` (known name in its category, at most 200 chars, no line break, no trailing
  space, fits the type: `True`/`False`, invariant int, invariant double, an enum member). The file is written with the
  saved values and these applied, only lines for declared options, in Skua's format (`SkuaOptionsFile.Format`).
  Without learnt definitions but with the options file (fallback above): only names already in the file are accepted,
  with the rules that need no type (at most 200 chars, no line break, no trailing space); SkuaLink checks the types from
  its cached definitions. The file keeps its names and order. With neither: `options not known yet`. Only a
  `.cfg` file inside Skua's options folder is ever read or written (never through a link); anything else is refused.
- **First start of a script with options (discovery)**, only for a script with no learnt definitions AND no options file
  (a file from an earlier session, or one that exists but cannot be read, means a normal start): the plugin waits for the options dialog (up to 5 s), closes it,
  then stops the script at once and answers `ok, firstRun, optionDefs` (plus the saved values). The phone shows the form;
  the owner fills it in, `setOptions`, then starts again. A script without options just runs. If no dialog appears within
  5 s (the script preconfigures itself) the script is stopped the same way. If the dialog is still open after 8 s
  (`FirstRunOptionsStuckAfter`, so the answer comes well within SkuaLink's 15 s), the script is NOT stopped: the answer
  is `ok, hasOptions` with the definitions, the state stays `awaitingOptions` with `optionsStuck`, and the phone shows the
  loud "Waiting for you on the PC" state.
- **Later starts:** the answer comes at once (`ok, hasOptions, optionDefs`), state `awaitingOptions`; in the background
  the plugin looks for the dialog every 250 ms and closes it. `awaitingOptions` ends: dialog gone after it was seen ->
  `running`; no dialog found within 5 s (Skua shows none for a script that preconfigures itself) -> `running`; dialog
  still open (or the windows could not be looked at) 10 s after the start -> `optionsStuck: true`, no more closing, and
  `running` once the owner closes it on the PC. A stop, or a start never seen running, ends it too. Windows that cannot be
  looked at are never taken for "no dialog".
- **Window glue:** framework WPF only (`UseWPF` in the project: `PresentationFramework`, no Skua.Core or Skua.WPF). On
  Skua's UI thread through Skua's `IDispatcherService.Invoke` (resolved from the service provider): scan
  `Application.Current.Windows` for a `Window` whose `DataContext` type is named `OptionContainerViewModel` and whose
  `Container` property (read by reflection) is the started script's `Config` (a plugin's options window, which uses the
  same view model with another container, is left alone); close it with `Window.Close()`. Each window call is waited for
  at most 2 s and only one is in flight at a time (a blocked UI thread counts as "cannot look"). The code that runs on
  Skua's UI thread catches everything itself (`Close()` runs Skua's own close handlers), so nothing reaches Skua's
  dispatcher; a failed look is "cannot look", a failed close is seen on the next look, then `optionsStuck`.
- Definitions are learnt only when `LoadedScript` is the script just started and, when its source declares a storage
  name, `Config.OptionsFile` is that name's file (otherwise `Config` may be another script's). `setOptions` for a learnt
  script without options is refused (`options not known yet`) rather than writing an empty file, which could be the
  `default.cfg` other scripts share.

## Protocol additions and several Skua (AQW-0087, AQW-0090)

- `hello` carries `instanceId`, a random GUID made once per plugin load. SkuaLink keeps one entry per Skua, up to 8
  connections on the same pipe name (9 pipe instances with the user-only descriptor: 8 connections and one waiting
  spare); a ninth is closed at once and keeps retrying. Commands are routed by instance and answered by the same Skua.
- `status` adds `control`, `settleSecondsLeft` and `optionsStuck`; `result` and the commands are new; `SkuaProtocol`
  caps a command line at 4 KB and a result line at 256 KB (at most `MaxScripts` scripts, `truncated` when cut).
- A Skua keeps its entry after it disconnects (marked disconnected) and a restarted Skua whose character logs in takes
  over the old entry; SkuaLink keeps at most 16 entries and one log file per character.
- A plugin older than the app (no `control` in the status) shows its state but cannot start or stop; the phone says so.

## Threading and safety

- Every handler goes through `Safe(Action)`; `Load`, `Unload` and every public `PipeClient` member catch everything.
  A failed `Load` removes whatever it attached. Skua never sees an exception from the plugin.
- Handlers run on Skua's threads (the UI thread for game packets, the script thread, a `Task.Run` for
  `ScriptStopping`) and only build a message and queue it. Every read that goes through Flash (inventory, quests,
  logged in, map, user name, active quests, factions) runs on one background task, never on Skua's threads.
- Queues are bounded (256, oldest dropped): the pipe client's line queue and the worker's queue. A status is never
  queued itself: one marker means "write the latest status here", and at most one marker is pending, so status changes
  during a long disconnect cannot push log lines out or queue repeated writes. The inventory line works the same way
  with its own marker, but is dropped when the connection ends instead of being written to the next one.
- Every string sent loses its lone surrogates (an unpaired UTF-16 half that Skua or the game may hand over), because
  the JSON writer refuses them and the whole message would be lost.
- While SkuaLink is not connected the plugin does no Flash reads at all: no status poll, no inventory snapshot, no
  quest or faction read (log lines still queue, unnamed where a read would be needed). When the pipe connects, `hello` and
  the last known status are written, then one status read and one snapshot are requested, and an inventory pass is
  asked for (it runs on the next 3 s poll and is sent even when unchanged).
- Pipe client: one connect attempt each time (timeout 0: on Windows a longer connect timeout spins a pool thread for the
  whole timeout while the pipe does not exist), then waits 1 s doubling to 30 s, retrying for the plugin's whole life; on
  each connect it writes `hello`, the latest status, then the queue; a write error drops the connection and reconnects;
  an empty line every 10 s while idle finds a closed SkuaLink (SkuaLink's reader skips empty lines). `Dispose`
  cancels, waits up to 2 s, then disposes its token source. `Unload` first waits up to 1 s for the background task to
  finish the read it may be in (bounded, since `Unload` may run on Skua's UI thread), then disposes the pipe client.
- No snapshot on a map change (it does not change the inventory).

## Cost of the quest reads (unverified on a real Skua)

`Quests.Active` deserializes the whole `world.questTree` (every quest loaded this session, not only the active ones) in
one Flash call, and every read rebuilds Skua's own shared tree cache (see the audit below). The plugin therefore reads it
at most once every 15 s, and at once after the pipe connects and after a login or relogin. The 3 s poll and the
event-driven status refreshes (map change, logout, script start or stop) never read it by themselves: in between, the
status carries the last list read. One read gives both the list and the ready flags, so there is no per-quest read.
Because the 15 s check runs on the 3 s poll, a quest accepted, turned in or made ready shows up to about 18 s late. If a
read fails, the last reported list stays (nothing is reported when there never was one); while logged out the list is
empty and the next login reads at once. **Reading `Active` is unverified on a real Skua**: neither the cost, nor whether
`Active` is meaningful outside a running script, nor whether "ready" matches the game has been checked inside Skua. The
decision rules live in `src/AqwCompanion.Skua/SkuaQuestRefresh.cs` and are unit-tested without Skua.

## Cost of the inventory reads (unverified on a real Skua)

`Inventory.Items`, `House.Items` and `Bank.Items` are each one `Flash.GetGameObject<List<InventoryItem>>` call
(`getGameObject`, `IFlashUtil.cs:90-118`). The AS3 side answers `JSON.stringify` of the whole object
(`Skua.AS3/skua/src/skua/Main.as:272-275`), every field of every item, descriptions included, wrapped in an XML
ExternalInterface reply parsed with `XElement.Parse` (`Skua.WPF/Flash/FlashUtil.cs:135-157`). So one call per container,
but the reply grows with the item count (order of 1 KB per item), and the call goes through the Flash ActiveX control,
marshalled to Skua's UI thread. The real time of a big bank read, and whether Skua's window stutters, is unverified (U2).

The schedule (`src/AqwCompanion.Skua/SkuaInventorySchedule.cs`, unit-tested with a fake clock) therefore limits the
passes. A pass (bag, house, bank and the three slot totals) runs on the plugin's worker, on the 3 s poll, only while
SkuaLink is connected and a character is logged in. It runs at once when nothing was read since the last login, character
change or connect; otherwise when an item event asked for it (bought, sold, every drop, also one offered in the drop
list, quest turned in, item added to the bank; these fire only while a script runs) and at least the gap has passed
since the last pass started; and without any event every 5 min (manual play raises no events). The gap is `max(5 s, 10 x
the time the last pass took)`, capped at 5 min, measured with `Stopwatch`. A request that comes during a pass stays for
the next one. A pass that found no bag (the bag is not loaded yet right after a login, the read failed, or the character
changed during the pass) is not a completed pass: it sends nothing, sets nothing, and the next one follows after 5 s.

## Audit: every Skua member the plugin touches

Generated from the compiled DLL's member references (System.Reflection.Metadata) and checked against the source.

| Skua member | Kind | Where it is used |
|---|---|---|
| `ISkuaPlugin` (implemented: `Name`, `Author`, `Description`, `Options`, `Load`, `Unload`) | implemented by the plugin | plugin entry point |
| `IServiceProvider.GetService(typeof(IScriptInterface))`, `GetService(typeof(IScriptManager))` | read (service lookup) | `Load`; the `IScriptManager` (or `bot.Manager`) drives the commands |
| `IScriptInterface.Events` | read | subscribe / re-attach / unsubscribe |
| `IScriptEvent.ItemBought` | event subscription | log "Bought", asks for an inventory pass |
| `IScriptEvent.ItemSold` | event subscription | log "Sold", asks for an inventory pass |
| `IScriptEvent.ItemDropped` | event subscription | log "Drop", asks for an inventory pass (every drop) |
| `IScriptEvent.MapChanged` | event subscription | log "Map", status refresh |
| `IScriptEvent.PlayerDeath` | event subscription | log "Died" |
| `IScriptEvent.Logout` | event subscription | log "Logged out", status refresh |
| `IScriptEvent.ReloginTriggered` | event subscription | log "Relogin", status refresh |
| `IScriptEvent.ScriptStopping` | event subscription (returns `true`, see correction 5) | log "Script error" |
| `IScriptEvent.QuestTurnedIn` | event subscription (AQW-0110) | asks for an inventory pass |
| `IScriptEvent.ItemAddedToBank` | event subscription (AQW-0110) | asks for an inventory pass |
| `INotifyPropertyChanged.PropertyChanged` on Skua's `ScriptManager` | event subscription | `ScriptRunning` / `LoadedScript` changes |
| `IScriptInterface.Manager` | read | |
| `IScriptStatus.ScriptRunning` | read | running state |
| `IScriptStatus.LoadedScript` | read | script name |
| `IScriptInterface.Player` | read | |
| `IScriptPlayer.LoggedIn` | read | status, gate for other reads |
| `IScriptPlayer.Username` | read | status character, inventory character |
| `IScriptPlayer.Level`, `.XP`, `.RequiredXP`, `.Gold`, `.Coins`, `.IsMember`, `.Guild`, `.CurrentClassRank` | read (one Flash read each, no cache; on every status while logged in) | status profile (AQW-0110) |
| `IScriptInterface.Servers` | read | |
| `IScriptServers.LastName` | read (getter only; the setter is never used) | status server |
| `IScriptInterface.Map` | read | |
| `IScriptMap.Name` | read | status map |
| `IScriptInterface.Inventory` | read | |
| `IItemContainer<InventoryItem>.Items` on `Inventory` | read | name snapshot, bought quantity, inventory pass |
| `IScriptInterface.House`, `IScriptInterface.Bank` | read | inventory pass (AQW-0110) |
| `House.Items`, `Bank.Items` (`IItemContainer<InventoryItem>.Items`) | read (generated getter, one Flash call each, no cache written: `ObjectBindingGenerator.cs:76-96`) | inventory pass (AQW-0110) |
| `Inventory.Slots`, `Bank.Slots`, `House.Slots` (`ILimitedInventory.Slots`) | read (generated getter, one Flash call each, no cache written) | status profile slots, once per inventory pass (AQW-0110) |
| `InventoryItem.CharItemID` | read | snapshot key |
| `InventoryItem.Equipped`, `ItemBase.ID`, `ItemBase.CategoryString` | read | equipped class of the inventory pass (AQW-0110) |
| `ItemBase.Name` | read | item names |
| `ItemBase.Quantity` | read | bought quantity, inventory quantities, class points |
| `IScriptInterface.Quests` | read | |
| `IScriptQuest.Active` | read (writes Skua's tree cache, see below; at most every 15 s) | status quests |
| `Quest.ID`, `Quest.Name`, `Quest.Status` | read | status quests, ready flag (`Status == "c"`) |
| `IScriptInterface.Reputation` | read | |
| `IScriptFaction.FactionList` | read (one Flash read of `world.myAvatar.factions`, no cache; on every status) | status factions |
| `Faction.ID`, `Faction.Name`, `Faction.Rank`, `Faction.TotalRep`, `Faction.Rep`, `Faction.RequiredRep` | read | status factions |
| `ClientFileSources.SkuaScriptsDIR` (Skua.Core.Models) | read | the scripts folder listed by `listScripts` |
| `StrongReferenceMessenger.Default.Send<LoadScriptMessage, int>(new(path), (int)MessageChannels.ScriptStatus)` (Skua.Core / CommunityToolkit, by reflection, best effort) | **changes Skua's loader view** (file name, "[Script loaded]", the loaded path) | `start`, right before `SetLoadedScript` (AQW-0108) |
| `IScriptStatus.SetLoadedScript(string)` | **changes Skua state** | `start`, only when no script runs |
| `IScriptStatus.StartScript()` | **changes Skua state** | `start` |
| `IScriptStatus.Config` -> `IScriptOptionContainer.Options`, `.MultipleOptions`, `.OptionsFile` | read | option definitions and the options file, after the plugin's own start; `Config` also identifies the dialog |
| `IOption.Name`, `.DisplayName`, `.Description`, `.Type`, `.DefaultValue` | read | option definitions |
| `ClientFileSources.SkuaOptionsDIR` (Skua.Core.Models) | read | the only folder options files are read from or written to |
| `IServiceProvider.GetService(typeof(IDispatcherService))`, `IDispatcherService.Invoke(Action)` | read / runs plugin code on Skua's UI thread | find and close the options dialog |
| the options file `%AppData%\Skua\options\{Storage}.cfg` | **written** (file, not a Skua member) | `setOptions`, only while no script runs |
| a listed script's `.cs` file | read as text (file, not a Skua member; never compiled or run by the plugin) | its `OptionsStorage` name, when the definitions are not learnt |
| `IScriptStatus.StopScript()` | **changes Skua state** | `stop`, on a pool thread; a first start with options (discovery) |
| `IScriptInterface.Flash` -> `Flash.GetGameObject<int>("world.myAvatar.objData.iBoostG" / "iBoostCP" / "iBoostRep" / "iBoostXP")` | read (one Flash call each, every status poll, and while a boost use is checked) | `status.boosts`, `useBoost` |
| `IScriptInterface.Boosts` -> `Boosts.UseBoost(int itemId)` | **sends `serverUseItem` to the game** | `useBoost`, only on that command |
| `IScriptServers.LastName` | read (also read for the status server) | `relogin` |
| `IScriptServers.Relogin(string)` | **logs in through Skua** (blocks up to about 30 s; Skua's own credentials) | `relogin`, on a pool thread, only on that command |
| `InventoryItem.Category`, `ItemCategory.ServerUse` | read | which bag items are boost items |

Framework WPF members (not Skua's): `Application.Current`, `Application.Windows`, `FrameworkElement.DataContext`, and
`Window.Close()` on the one window matched as the started script's options dialog (which makes Skua save the values it
shows).

Removed in AQW-0076: the `IScriptEvent.QuestAccepted` and `IScriptEvent.QuestTurnedIn` subscriptions and the
`IScriptQuest.Tree` read (it only named those two log lines); AQW-0110 subscribes to `QuestTurnedIn` again, only to ask
for an inventory pass. Never called: `Bank.Load` (it sends a game packet, `%xt%zm%loadBank%`), `Bank.Loaded` (not relied
on, U3), `Player.CurrentClass` (re-reads the whole inventory). No other Skua member is referenced (the one messenger
`Send` of `LoadScriptMessage` above is found by name, not referenced). In particular nothing calls the game's `Send`, `Flash`, `Shops`, `Options`, `RestartScriptAsync`, `Compile`, `CanComplete`, `Inventory.Sell` or
any setter; `SetLoadedScript`, `StartScript` and `StopScript` are called only by the `start` and `stop` commands above
(a discovery start stops its script itself). `Config.Set`, `Save`, `Load` and `Configure` are not called: the options file
is written directly, in Skua's format. Some getters above are implemented by Skua as Flash reads (`isLoggedIn`,
`world.strMapName`, `world.myAvatar.items`, `world.myAvatar.houseitems`, `world.bankinfo.items`, `world.myAvatar.factions`,
`world.questTree`, `loginInfo.strUsername`, the `world.myAvatar.objData` / `dataLeaf` numbers); they read game objects and
change nothing.

Apart from the commands (load, start and stop a script, use a boost and log in again, on SkuaLink's request), the plugin never calls anything that
sends a packet or changes game or script state. One side effect inside Skua remains: `IScriptQuest.Active` goes through `IScriptQuest.Tree`, which
is not a pure getter. It writes Skua's own shared 100 ms cache of the quest tree it just read (`_cachedTree`,
`_lastTreeUpdate`, no lock), and Skua clears that cache itself after accepting or turning in a quest. So a plugin read
racing a script's own quest actions can, at most, make the script's immediately following tree read (for example its
`CanComplete` check) be served from a cache filled a moment earlier, bounded by the 100 ms cache life. This is why the
plugin reads `Active` at most every 15 s.

## Build

The Skua DLLs are not committed. Put `Skua.Core.Interfaces.dll` and `Skua.Core.Models.dll` from the Skua 1.4.5.0
release in `lib/skua/` (or pass `-p:SkuaLibDir=...`), then:

```
dotnet build plugin/SkuaLink.SkuaBridge -c Release
```

The output is `SkuaLink.SkuaBridge.dll` (plus `.pdb` and `.deps.json`); no Skua assembly is copied. The protocol
files from `src/AqwCompanion.Skua` are compiled in as source, so the plugin is one DLL whose only non-framework
references are `Skua.Core.Interfaces` and `Skua.Core.Models`. Since AQW-0089 the project sets `UseWPF` (the Windows
Desktop framework reference, no package) for the options dialog: the DLL then also references the framework's
`PresentationFramework`, which Skua (a WPF app on `net10.0-windows`) already loads.

## Not verified (owner's Windows checklist)

Only the owner's Windows can prove these; the full step-by-step list is kept outside the repository.

- **Load and connect:** the plugin loads in Skua 1.4.5.0 and connects to SkuaLink with both running as the normal user
  (SkuaLink runs as the normal user since 0.6.0; Skua started as administrator is not tested, and the plugin's own
  `CurrentUserOnly` check is expected to refuse it). The plugin and SkuaLink must come from the same release.
- **Reports:**
  - Whether the game has applied a purchase to the inventory when `buyItem` arrives (the plugin reads it 300 ms later);
    if not, "Bought" shows without a quantity and possibly as `item #id`. The quantity can also be wrong for back-to-back
    buys of the same item.
  - That the re-attach after `ClearHandlers` keeps the log working across several script runs.
  - Reading `Quests.Active` at most every 15 s: the cost (see above), whether the list is right with no script running,
    that the quest card updates within about 18 s, that the ready flag matches the game, and that a script that
    registers quests keeps working normally.
  - The faction list read every 3 s: its cost with about 50 factions (the 0.4.0 item "reputation cost every 3 s"), that it
    matches the game's Reputation panel, and that `Rep` / `RequiredRep` are the in-rank progress and the rank step (0 at
    rank 10).
- **Profile and inventory (AQW-0110, 0.6.0):**
  - U1. Class points: on the Overview, "Class points" matches the game for the equipped class (the plugin uses the class
    item's quantity, `iQty`). A class changed by hand shows its new name and rank within 5 min (at once if a script is
    running and buys or drops something).
  - U2. Read cost: with a big bank (hundreds or thousands of items) opened in Skua, Skua's window does not visibly
    stutter on each read (at most one every 5 s, and one every 5 min without events). Note how long a read seems to take.
  - U3. Bank: before the bank is opened in Skua this session, the Overview shows the bank items saved last time (or
    none); after it is opened, the bank items count in the Hard Farms within 5 min; closing the bank window does not erase
    them. Nothing depends on Skua's `Bank.Loaded`. Unknown: whether `world.bankinfo.items` is cleared on a relogin to
    another character in the same Skua session. To be safe the plugin treats the bank as not read after a character
    change until a bank read with other items than the previous character's arrives; check that after switching
    characters the Overview does not show the other character's bank, and that opening the bank shows the right one.
  - U4. House: the house items appear right after login, without visiting the house.
  - U14. Events: with a script running, buying, selling, picking up a drop, turning in a quest and a drop that goes
    straight to the bank update the Hard Farms within seconds. Right after a login the inventory shows within about 10 s
    (the first pass may find the bag not yet loaded and is retried after 5 s). Picking up an item from the drop list
    raises no event of its own (the event comes when the drop is offered), so the pass it asks for may run before the
    pickup; the item then shows with the next event or the 5 min pass. Without a script running there are no item events,
    only the 5 min pass.
  - U15. An older 0.5 plugin with SkuaLink 0.6: the character appears, the Overview says the plugin does not report the
    numbers or the inventory, and the Bot section works as before.
- **Boosts and log in again (not yet verified; only a real Skua can prove them):**
  - That `Boosts.UseBoost(itemId)` activates a boost from the bag and that the value turns above 0 within 3 s; that a
    refused use (a boost of that type already active) consumes nothing.
  - That the four values read as the remaining seconds in a real game, are 0 when no boost is active, and drop to 0 at the
    end of a boost; that a failed Flash read does not show up as a false end.
  - That the boost items of the bag are listed with the right type (the reputation and XP boost names are unknown).
  - That `Servers.Relogin(Servers.LastName)` logs in from the login screen and after a kick, and that Skua's UI does not
    freeze during the blocking call.
  - That a map change (one logged-out poll) does not reset the boost reading.
- **Control (AQW-0088):**
  - The two-way pipe connects to the new SkuaLink; that Windows refuses the `InOut` client of an older SkuaLink's
    inbound-only pipe with `UnauthorizedAccessException` (so the plugin falls back to send-only) is from the Win32 rules,
    not seen. That fallback must also be checked when the plugin sees a pipe that never reads (`outBufferSize`).
  - Listing: the folder, the names and folders shown, `Cached-Scripts` left out, the time a large scripts folder takes.
  - Start: the script really starts from the plugin (no Skua UI involved), and the text of a compile or version error
    as it reaches the phone.
  - Skua's loader view (AQW-0108): start a script from the PC, stop it and start another from the phone: the Skua window
    must show the new script's name (the reflection lookup of `LoadScriptMessage`, `MessageChannels` and
    `StrongReferenceMessenger` has only run against stand-in types). Its status text reads "[Script loaded]" while the
    script runs (expected, see "Skua's script loader view"), "[Stopped]" after a stop.
  - Stop duration: how long Skua takes from `stop` until `ScriptRunning` turns false, and a script that ignores the stop
    (Skua gives up after about 40 s and the state stays `stopping` until it really ends; a new `stop` tries again).
  - Settling: that 10 s without a map change (seen on the 3 s poll while no script runs) covers Skua's cleanup after a
    stop, and that a script that stops by itself also settles. A start accepted but never reported running settles after
    10 s.
- **Script options (AQW-0089, AQW-0091):**
  - The options file path and format: `%AppData%\Skua\options\{OptionsStorage}.cfg`, the lines Skua writes, enum values
    with spaces, and that a file written by the plugin is read back by Skua unchanged.
  - That `Config` read right after `StartScript()` returns is the script just started (definitions, `OptionsFile`, and
    `hasOptions`).
  - The window match (`DataContext` type name `OptionContainerViewModel`, its `Container` being `Config`) and the close
    with `Window.Close()` through `IDispatcherService.Invoke` while Skua's dialog runs in `ShowDialog`. If the match fails
    while the dialog is open, the 5 s rule makes the state `running` with the dialog still on the PC, silently.
  - That the script reads the saved values after the dialog closes, including a script that reads its options before.
  - First start (discovery): that stopping the script right after closing its dialog leaves Skua clean, and that a
    dialog appearing later than 5 s after the start (slow PC) does not stay on the PC.
  - A second start of the same script in one Skua session: whether Skua shows the dialog again, and that with no dialog
    the state goes to `running` after 5 s. A script with `DontPreconfigure = true` shows no dialog.
  - The fallback after a Skua restart: that real scripts declare `public string OptionsStorage = "...";` on one line, and
    that the file found this way is the one Skua uses.
  - A PC whose culture writes decimals with a comma: the plugin writes doubles in the PC's culture (`1,5`) as Skua reads
    them (`Convert.ChangeType`, current culture); that the plugin thread's culture is Skua's script thread's is not seen.
    After a Skua restart SkuaLink converts doubles itself with its own culture (same Windows user assumed) and sends enum
    members with spaces; that Skua reads both back correctly is not seen.
- **Several Skua (AQW-0090):** up to 8 plugins connect at once and each one's commands reach only that Skua; a ninth Skua, closed at once by SkuaLink, keeps retrying every second without errors in Skua; a
  restarted Skua (new instance id) takes over its old entry once its character logs in; a Skua on the send-only fallback
  still reports while its commands time out ("Skua did not answer"); the per-character log files
  (`skua-log.<character>.json`) appear in SkuaLink's data folder and the old `skua-log.json` is folded into the first
  character that logs in. The 0.4.0 item "character names match without regard to case" is still open here too.
- **Phone page (AQW-0093, AQW-0116):** start, stop and the options step end to end against a real Skua (every option
  type) from a character's Bot section, and with two Skua open each Bot section acting on its own Skua; the sheet as the browser's own dialog (focus in and back,
  Escape, back gesture, backdrop tap) on iPhone Safari 15.4 or later and Android Chrome; the lazy control chunk loading
  offline from the service worker; the "Ask for options" switch on the running card; "Skua did not answer" after a real
  504; the notices `SkuaDisconnected`, `SkuaReconnected` and `SkuaOptionsWaiting`.
