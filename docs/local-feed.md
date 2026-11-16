# Local feed (for other programs)

Off by default. **Settings, "Let other programs on this PC read my characters"** turns on a read-only feed that other programs on the same PC can use, for example a remote viewer. It listens on `127.0.0.1:47821` only, never on the network, and it never sends anything to the game.

- **Secret:** a program must send `Authorization: Bearer <secret>`. The secret is the text in `%LOCALAPPDATA%\AqwCompanion\feed.token`, which is created when you turn the feed on; **New secret** in Settings replaces it. A request with an `Origin` header (a web page) or another `Host` is refused.
- **`GET /v1/ping`** returns `{"contract":1,"app":"SkuaLink","version":"..."}`.
- **`GET /v1/state`** returns the snapshot below.
- **`GET /v1/events`** is a Server-Sent Events stream: a `state` event with the full snapshot at once and whenever it changed (at most once a second), and a `ping` event every 25 seconds.

```json
{
  "contract": 1,
  "capture": { "state": "1 Skua connected.", "listening": true },
  "selected": "Alpha Hero",
  "characters": [
    {
      "name": "Alpha Hero", "level": 60, "live": true,
      "savedAtUtc": "2026-11-09T12:00:00+00:00", "lastLiveUpdateUtc": "2026-11-09T12:05:00+00:00", "liveUpdates": 3,
      "figures": { "gold": 1200, "exp": 340, "expToLevel": 5000, "ac": 777, "classPoints": 99, "daysPlayed": 0 },
      "lastKill": null
    },
    {
      "name": "Beta Hero", "level": 12, "live": false,
      "savedAtUtc": "2026-11-09T11:00:00+00:00", "lastLiveUpdateUtc": null, "liveUpdates": 0,
      "figures": { "gold": 5, "exp": 6, "expToLevel": 7, "ac": 8, "classPoints": 0, "daysPlayed": 0 },
      "lastKill": null
    }
  ],
  "factions": [
    { "name": "Nation", "rank": 3, "rep": 5000, "toNext": 5000, "fraction": 0.21875, "max": false },
    { "name": "Good", "rank": 10, "rep": 302500, "toNext": 0, "fraction": 1, "max": true }
  ],
  "hardFarms": { "owned": 4, "total": 8, "missing": ["Cape of Chaos", "Nation Hoodie"] }
}
```

Every character Skua reported numbers for is its own entry, identified by its name; characters keep their last known numbers when Skua is closed. `capture` describes the Skua connection (the field keeps its contract 1 name): `listening` is true while at least one Skua with the SkuaLink plugin is connected. `live` is true while the character is logged in in a connected Skua; `lastLiveUpdateUtc` is when its numbers last changed while live and `liveUpdates` counts those changes since SkuaLink started. Since 0.6.0 `daysPlayed` is always 0 and `lastKill` is always null (Skua does not provide them); the fields stay so contract 1 readers keep working. `factions` and `hardFarms` describe the `selected` character (`hardFarms` is `null` while the Hard Farms list or the inventory is unknown, and `missing` is cut to 200 names). Fields are only ever added within contract 1; a breaking change raises the number. There are no ids, e-mail, IP addresses or other players in it.
