<p align="center">
  <img src="docs/logo.svg" width="128" height="128" alt="SkuaLink logo">
</p>

<h1 align="center">SkuaLink</h1>

<p align="center">
  Watch your Skua bots from your phone and control them. SkuaLink was made to keep an eye on the progress of the bots,
  and to start and stop them, from far away. The author hopes it helps other Skua users with the same need.
</p>

<p align="center">
  <a href="https://github.com/agvmdsa/SkuaLink/releases/latest"><img src="docs/download-windows.svg" width="368" alt="Download SkuaLink for Windows"></a>
</p>

<p align="center">
  <sub>Self-contained single file · SHA-256 checksum next to every release · <a href="#install">how to install</a></sub>
</p>

<p align="center">
  <a href="https://github.com/agvmdsa/SkuaLink/actions/workflows/ci.yml"><img src="https://github.com/agvmdsa/SkuaLink/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI status of the main branch"></a>
</p>

## What it does

SkuaLink puts your Skua bots on your phone. See what each Skua is doing and its progress, then start or stop its scripts without going back to the PC.

- **From your phone:** state, active quests, live reputation, the controls to stop a script or start another one with its options, and a log for each character's bot. Navigation is a floating dock; the page is installable and has a light, dark or system theme.
- **Characters:** every character you play in Skua, with its level, experience, gold, AC, class, reputation and the Hard Farms you already own (from its inventory, house and bank), kept on the PC when Skua is closed, with how old the data is.
- **Several Skua at once:** each character shows its own bot: state, active quests, log and controls.
- **Hands off the game:** through the [Skua plugin](#skua-plugin), and only when you ask from the phone, it can start and stop Skua scripts and read or write the options file of a script in Skua's scripts list. It never sends anything to the game.
- **Local:** everything stays on your computer. No telemetry. **Forget my data** (in **Settings**) removes it. Nothing leaves the PC except the Wiki request you start and, if you turn them on, [Phone access](#watch-and-control-from-your-phone) and the [Skua plugin](#skua-plugin).

## Install

You need 64-bit Windows, [Skua](https://github.com/auqw/Skua) with the [SkuaLink plugin](#skua-plugin), and [Tailscale](https://tailscale.com/) for the phone.

1. Download `SkuaLink-Setup-vX.Y.Z.exe` from the [latest release](https://github.com/agvmdsa/SkuaLink/releases/latest) and run it.
2. Start SkuaLink from the Start menu. It runs as your normal Windows user.
3. The builds are not code-signed yet, so SmartScreen may warn you: choose **More info**, then **Run anyway**, if you trust the file. You can compare the download with `SHA256SUMS.txt` (`Get-FileHash .\SkuaLink-Setup-vX.Y.Z.exe -Algorithm SHA256` in PowerShell).
4. Set up the [Skua plugin](#skua-plugin) (everything SkuaLink shows comes from it) and [phone access](#watch-and-control-from-your-phone).

No installer? Download the `.zip` instead, extract it anywhere and run `SkuaLink.exe`. Your data, logs and crash reports live in `%LOCALAPPDATA%\AqwCompanion`; delete that folder to remove everything the app stored.

## Watch and control from your phone

Off by default. **Settings, "Watch my characters from my phone"** serves a small page on a phone you pair, updating live: **Characters** first, and for each character its **Overview** (numbers, reputation, Hard Farms) and its **Bot** (what Skua is doing and its controls). It works as an installed web app, with a light, dark or system theme.

**You need:** [Tailscale](https://tailscale.com/) on this PC and on your phone (same account), **MagicDNS** and **HTTPS certificates** turned on once in the Tailscale admin console (the app tells you which step is missing), and SkuaLink running on the PC. The phone reaches the page only through your own tailnet; nothing is opened to the internet.

**Steps:** turn on the switch, press **Pair a phone...**, scan the QR code with the phone camera, then add the page to the home screen (on an iPhone: Share, then Add to Home Screen). A pairing link works once and for 5 minutes. Each paired phone can be revoked in the same window.

- **Installed to the Home Screen first?** An iPhone Home Screen app keeps its own storage, so pairing in Safari does not pair it. Open the installed app, use the **Pairing link** field on its "Not paired" card and paste the link there.
- **PC off?** The page opens from its saved copy within a few seconds and says it cannot reach SkuaLink, with how old the last data is.
- Anyone on your tailnet can open the page, but only a paired phone sees any data.
- Push notifications (a notice while the page is closed) do not exist yet.

## Skua plugin

A separate download for [Skua](https://github.com/auqw/Skua) (a third-party game client), and the only source of what SkuaLink shows. `SkuaLink.SkuaBridge.dll` runs inside Skua and shows its state in the **Bot** section of each character on the phone page: what the character is doing (server, map, script running), the **active quests** (the ones ready to turn in marked) and a short **log** with filters (buys, sells, drops, map changes, script start and stop, deaths).

It reports each character's **reputation** live and groups repeated drops in the log ("Drop: Blood Orb x13"). It also reports the character's numbers and an inventory snapshot (bag, house, and the bank once you opened it in Skua) for the Overview and the Hard Farms; it re-reads the inventory after buys, sales, drops and quest turn-ins while a script runs, and every 5 minutes otherwise. It can also **start and stop scripts**: pick one from the scripts in Skua's scripts folder (bot scripts only, with the title and description from the script's header; search by name, folder, description or tag), set its options if it has any, and press Start. Several Skua can run at once: each character's Bot section belongs to the Skua that plays it.

Besides reporting, the plugin does exactly five things for SkuaLink: list the scripts in Skua's scripts folder, read and write the options file of one of them, start a chosen script, and stop the running one. It never accepts a file path or code from the phone, and it sends nothing to the game. Skua may open a script's options window on the PC when it starts; the plugin closes that window itself, and if it cannot, the phone says "Waiting for you on the PC".

1. Download `SkuaLink.SkuaBridge.dll` from the [latest release](https://github.com/agvmdsa/SkuaLink/releases/latest) (check it against `SHA256SUMS.txt`).
2. Copy it into `%AppData%\Skua\plugins` (the **Skua** block in **Settings** opens that folder) and reopen Skua. With SkuaLink running, that block reads "Skua connected".
3. To remove it, close Skua and delete the file.

The plugin and SkuaLink must come from the same release: with a plugin from 0.5 the Bot section works but the Overview has no numbers or Hard Farms, and an even older plugin shows the state but cannot start or stop anything. Tested with Skua 1.4.5.0. Up to 8 Skua at once; log lines appear while a script is running; Skua started as administrator is not supported. Skua publishes no license: the plugin only references its interfaces and copies none of its code.

## Using the PC app

The window shows the characters Skua reported, with their numbers, reputation (closest to the next rank first) and Hard Farms. A character keeps its last known data when Skua is closed, and the window says how old it is.

- **Character chip** (top bar): switch between your characters.
- **Hard Farms:** press **Update from the Wiki** in its card; the list is imported on your click and never bundled (CC BY-SA 3.0). What you own is marked from the character's inventory, house and bank.
- **Settings** (the gear): start with Windows, phone access, the Skua plugin, the privacy text, **Forget my data**, credits.
- **Tray icon:** closing the window hides it; **Exit** in the tray menu really closes the app.
- **Debug mode** (Settings, or Ctrl+Shift+D): the Hard Farms entries without a match and the diagnostics export.

### What it reads, and what it does not

SkuaLink reads, on this computer only, what the SkuaLink plugin inside Skua reports: each character's name, level, experience, gold, AC, class and class rank, class points, membership, guild and slot counts, faction reputation, the names and quantities of the items in the inventory, the house and the bank (once opened in Skua), and what Skua is doing (server, map, script, active quests, a short log). It never reads or keeps your e-mail address, IP address, password, user or character ids, or anything about other players, and it never sends anything to the game.

The bank is only known after you open it in the Skua session; until then the Hard Farms use the bank saved last time, if any.

Updating from 0.5: the game data the old traffic reading saved is no longer read; **Forget my data** removes it.

## For developers

- [Local feed](docs/local-feed.md): a read-only feed for other programs on the same PC.
- [Build from source and releases](docs/building.md).
