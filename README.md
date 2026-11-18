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
- **Local:** everything stays on your computer. No telemetry. **Forget my data** (in **Settings**) removes it. Nothing leaves the PC except the Wiki request you start, the update check (a plain request to GitHub for the latest release while **Check for updates** is on) and, if you turn them on, [Notifications](#notifications) (encrypted text to the push service of your phone's browser: Apple, Google, Mozilla or Microsoft), [Phone access](#watch-and-control-from-your-phone) and the [Skua plugin](#skua-plugin).

## Install

You need 64-bit Windows, [Skua](https://github.com/auqw/Skua) with the [SkuaLink plugin](#skua-plugin), and [Tailscale](https://tailscale.com/) for the phone.

1. Download `SkuaLink-Setup-vX.Y.Z.exe` from the [latest release](https://github.com/agvmdsa/SkuaLink/releases/latest) and run it.
2. Start SkuaLink from the Start menu. It runs as your normal Windows user.
   SkuaLink checks for new versions itself (see [Updates](#updates)); you can also install a new `SkuaLink-Setup-vX.Y.Z.exe` over the old one.
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
- **Notifications:** see the next section.

### Notifications

Off until you turn them on, per phone. Open the page, then **Settings**, then **Notifications**, then **Enable on this phone** (on an iPhone the page must be added to the Home Screen first, iOS 16.4 or later). Each kind has its own switch, and **Send a test notification** checks the whole path. You get a notice when:

- a script stopped, or failed (with the first line of the error);
- a Skua or character was disconnected (including a kick or an unexpected logout while a script was running);
- Skua is waiting for you on the PC (a script's options window);
- you rank up, or obtain a Hard Farm;
- a character made no progress for 10 minutes (no change in experience, gold, AC, class points, inventory, reputation, map or script state);
- a character is dying too often (5 deaths in 2 minutes; the notification names the character);
- a queue needs you (it paused, or skipped a step), or finished.

Tapping a notification opens that character's **Bot** section. A script you stopped yourself from the phone does not notify you.

A phone that subscribed before the update check existed must switch the new **update** notification on once in the phone's **Settings**, **Notifications** (there is no migration of existing subscriptions).

**Limits:** SkuaLink must be running and the PC must have internet access: nothing can be sent while SkuaLink is closed. If a system proxy is configured, SkuaLink uses it for push. Skua reports script and death events only while a script runs, so deaths in manual play are not counted. Delivery is up to the push service and can be delayed or dropped.

**Privacy:** the notification text is encrypted end to end for your phone and goes through the push service of your phone's browser (Apple, Google, Mozilla or Microsoft). That service sees only ciphertext and metadata: the address, the size, the time and the signed request headers. The notice carries the character and script names and, for an error, the first line of the error message, so it can show that text on your phone. The PC keeps its own push key in `vapid.key` and each phone's subscription in `push.json` in `%LOCALAPPDATA%\AqwCompanion`; the page never receives either. **Forget my data** deletes both, and revoking a phone deletes its subscription.

## Skua plugin

A separate download for [Skua](https://github.com/auqw/Skua) (a third-party game client), and the only source of what SkuaLink shows. `SkuaLink.SkuaBridge.dll` runs inside Skua and shows its state in the **Bot** section of each character on the phone page: what the character is doing (server, map, script running), the **active quests** (the ones ready to turn in marked) and a short **log** with filters (buys, sells, drops, map changes, script start and stop, deaths).

It reports each character's **reputation** live and groups repeated drops in the log ("Drop: Blood Orb x13"). It also reports the character's numbers and an inventory snapshot (bag, house, and the bank once you opened it in Skua) for the Overview and the Hard Farms; it re-reads the inventory after buys, sales, drops and quest turn-ins while a script runs, and every 5 minutes otherwise. It can also **start and stop scripts**: pick one from the scripts in Skua's scripts folder (bot scripts only, with the title and description from the script's header; search by name, folder, description or tag), set its options if it has any, and press Start. Several Skua can run at once: each character's Bot section belongs to the Skua that plays it.

Besides reporting, the plugin does exactly five things for SkuaLink: list the scripts in Skua's scripts folder, read and write the options file of one of them, start a chosen script, and stop the running one. It never accepts a file path or code from the phone, and it sends nothing to the game. Skua may open a script's options window on the PC when it starts; the plugin closes that window itself, and if it cannot, the phone says "Waiting for you on the PC".

1. Download `SkuaLink.SkuaBridge.dll` from the [latest release](https://github.com/agvmdsa/SkuaLink/releases/latest) (check it against `SHA256SUMS.txt`).
2. Copy it into `%AppData%\Skua\plugins` (the **Skua** block in **Settings** opens that folder) and reopen Skua. With SkuaLink running, that block reads "Skua connected". Or press **Download plugin** in **Settings** (the **Skua** and **Updates** blocks) with Skua closed: SkuaLink downloads the plugin of the right release, checks it against `SHA256SUMS.txt` and puts it in that folder for you.
3. To remove it, close Skua and delete the file.

The plugin and SkuaLink must come from the same release: with a plugin from 0.5 the Bot section works but the Overview has no numbers or Hard Farms, and an even older plugin shows the state but cannot start or stop anything. Tested with Skua 1.4.5.0. Up to 8 Skua at once; log lines appear while a script is running; Skua started as administrator is not supported. Skua publishes no license: the plugin only references its interfaces and copies none of its code.

## Script queue

Chain scripts and let SkuaLink run them one after the other. A queue belongs to a character and has up to 20 steps. A step is a script from Skua's scripts folder plus the item it must obtain (name and quantity, counted in the inventory only, not in the bank), with an optional reputation rank and an optional time limit in minutes. SkuaLink starts the script of the first step, watches the character's inventory, **stops the script when the goal is reached**, waits for Skua to settle (the same 10 seconds as always) and starts the next step. "The script ended" is never a success.

- A script that ends without the item (usually a daily you already did today) raises a notice ("The step ... ended without ... It may be a daily you already did today.") and the queue skips to the next step. A step whose item you already have is skipped at once.
- **Problems pause the queue** and notify you: a script error, a kick or logout, Skua disconnecting, a start that fails, Skua not becoming ready to start within 90 seconds, a time limit, or you starting another script yourself. Nothing is retried and nothing restarts by itself: press **Continue**. Nothing starts when SkuaLink opens either.
- **Use Pause or Stop queue in SkuaLink before you step in by hand in Skua.** If you stop the queue's script in Skua yourself, SkuaLink cannot tell it from a script that ended by itself: it treats the step as ended without the item and skips to the next step (unless the log shows an error, a logout or a kick). If you start a different script yourself, the queue pauses ("You changed the script"); it never stops a script it did not start.
- **Start**, **Pause**, **Continue**, **Skip step** and **Stop queue** are on the **Script queue** card of the PC window and on the phone, in the **Bot** section. On the phone, **Skip step** and **Stop queue** ask for confirmation first. After SkuaLink restarts, a queue is stopped at the step it was on; nothing starts by itself.
- Edit the queue with **Edit queue** on the PC (a window) or on the phone (a page loaded only when you open it). The reputation rank is edited on the PC only: the phone editor does not show it and does not remove one the PC set. A save from the phone overwrites the whole list, but it is refused with "The queue changed on the PC. Open the editor again." when the queue changed after the editor opened (your rows stay on screen: copy what you need, close the editor and open it again), and the phone refuses to save an empty queue (use **Stop queue**).
- Two notifications, **The queue needs you** and **The queue finished**, have their own switches; a phone that subscribed before this version must switch them on once in **Settings**, **Notifications** (there is no migration).
- It uses only what the plugin already does (list, start and stop scripts) and reports (status, inventory, reputation): there is no plugin change. Queues are saved as `skua-queue.<character>.json` in `%LOCALAPPDATA%\AqwCompanion`, hold only script ids, item names and numbers, and **Forget my data** removes them. Nothing about them leaves the PC and your paired phones.

**Limits:** the plugin refreshes the inventory after item events and at least every 5 minutes, so a goal can be noticed a little late (or a step wrongly skipped from an old snapshot). SkuaLink cannot tell a script you stopped yourself from a script that ended by itself, so it treats both as "ended without the item". Scripts with the same name in two folders look the same to the "you changed the script" check. This has not been run against a real Skua yet.

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

### Updates

**Check for updates** (Settings, on by default) asks GitHub for the latest SkuaLink release 30 seconds after the app starts and every 6 hours, and when you press **Check now**. Only the latest finished release is considered (never a draft or a pre-release), and an update is offered only when it is newer than the version you run. Turn the switch off and SkuaLink makes no request to GitHub at all. Nothing is sent except the plain request and a `User-Agent` that says `SkuaLink` and its version. If GitHub cannot be reached SkuaLink says "Could not check" and tries again at the next interval.

When there is an update, a banner "SkuaLink X.Y.Z is available" appears at the top of the window with **Update now** and **Release notes**, the tray icon menu shows **Update available**, and your paired phones get a notice once per version (and a push notification if you turned that kind on in **Notifications**; the notification only tells you, the update is done on the PC).

**Update now** downloads `SkuaLink-Setup-vX.Y.Z.exe` and `SHA256SUMS.txt` from the release into a new temporary folder, checks the installer's SHA-256 against the line for that exact file name, and only then starts the installer through the normal Windows administrator prompt (it is never silent, and never automatic). SkuaLink then closes so the installer can replace it; if you cancel the Windows prompt, SkuaLink keeps running and says so. If the check fails, the download is deleted and nothing is started. Downloads are accepted only from `github.com` and GitHub's release asset hosts over https.

**The builds are not code-signed.** The SHA-256 check protects against a corrupted download or one swapped by another server; it does not protect against a compromised release in the GitHub repository itself.

**New DLL required.** The Skua plugin must be the same version as the app. Before an update, the banner, the **Updates** block and the **Skua** block of Settings say "New DLL required" with the version that matches the one you are about to install; after an update, they say it again whenever a connected Skua reports a plugin version that differs from the app's. **Download plugin** downloads that `SkuaLink.SkuaBridge.dll`, checks it and puts it in `%AppData%\Skua\plugins` when Skua is not using the file; if Skua has it loaded, nothing is touched and SkuaLink says "Close Skua and press the button again". Reopen Skua afterwards. **Open plugins folder** and **Open release page** stay available for a manual install.

## For developers

- [Local feed](docs/local-feed.md): a read-only feed for other programs on the same PC.
- [Build from source and releases](docs/building.md).
