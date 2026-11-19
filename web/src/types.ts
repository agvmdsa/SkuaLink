export interface Figures {
  gold: number;
  exp: number;
  expToLevel: number;
  ac: number;
  classPoints: number;
  daysPlayed: number;
}

export interface Kill {
  gold: number;
  exp: number;
}

export interface Character {
  name: string;
  level: number;
  live: boolean;
  savedAtUtc: string;
  lastLiveUpdateUtc: string | null;
  liveUpdates: number;
  figures: Figures;
  lastKill: Kill | null;
}

export interface Faction {
  name: string;
  rank: number;
  rep: number;
  toNext: number;
  fraction: number;
  max: boolean;
}

export interface HardFarms {
  owned: number;
  total: number;
  missing: string[];
}

export interface Snapshot {
  contract: number;
  capture: { state: string; listening: boolean };
  selected: string | null;
  characters: Character[];
  factions: Faction[];
  hardFarms: HardFarms | null;
}

export type NoticeKind = 'RankUp' | 'HardFarmDrop' | 'ActivityStopped' | 'NewLogin' | 'SkuaDisconnected' | 'SkuaReconnected' | 'SkuaOptionsWaiting' | 'ScriptStopped' | 'ScriptError' | 'CharacterKicked' | 'NoProgress' | 'DyingOften' | 'UpdateAvailable' | 'QueueAttention' | 'QueueFinished' | 'BoostEnded';

export interface Notice {
  id: string;
  kind: NoticeKind;
  character: string;
  title: string;
  body: string;
  atUtc: string;
}

export type SkuaConnection = 'NotConnected' | 'Connected' | 'VersionMismatch';

export interface SkuaQuest {
  id: number;
  name: string;
  ready: boolean;
}

/** A faction as the Skua plugin reads it: rep is earned inside the current rank, requiredRep the size of the rank step (0 at the maximum rank). */
export interface SkuaFaction {
  id: number;
  name: string;
  rank: number;
  totalRep: number;
  rep: number;
  requiredRep: number;
}

export interface SkuaStatus {
  loggedIn: boolean;
  character: string | null;
  server: string | null;
  map: string | null;
  script: string | null;
  scriptRunning: boolean;
  runningSince: string | null;
  /** null: the plugin does not report quests (an older plugin); empty: no active quests. */
  quests: SkuaQuest[] | null;
  /** null: the plugin does not report reputation (an older plugin); empty: no factions. */
  factions: SkuaFaction[] | null;
  /** What the control logic says; absent from an older plugin. */
  control?: SkuaControl;
  /** Seconds left of the settle wait; only while control is settling. */
  settleSecondsLeft?: number;
  /** The plugin could not close the script's options window on the PC. */
  optionsStuck?: boolean;
}

export type SkuaControl = 'idle' | 'running' | 'stopping' | 'settling' | 'awaitingOptions';

export interface SkuaLogEntry {
  at: string;
  kind: string;
  text: string;
}

export interface SkuaState {
  connection: SkuaConnection;
  hello: { pluginVersion: string; skuaVersion: string } | null;
  status: SkuaStatus | null;
  log: SkuaLogEntry[];
}

/** One Skua of the several the PC can hold: the same parts as the single Skua state, plus who it is. */
export interface SkuaInstance extends SkuaState {
  instanceId: string;
  /** The character name, or "Skua N" while nobody is logged in. */
  label: string;
  /** The plugin went away; the instance stays listed. */
  disconnected: boolean;
}

/** What /api/state and the "state" event carry: the last snapshot of the PC and its recent notices. */
export interface RemoteState {
  snapshot: Snapshot | null;
  notices: Notice[];
  /** The Skua part; an older app sends none, which means null. */
  skua?: SkuaState | null;
  /** Every Skua the PC knows, in order of arrival; an older app sends none (absent). */
  skuaInstances?: SkuaInstance[];
  /** Every character SkuaLink knows, live or last known; an older app sends none (absent). */
  characters?: PhoneCharacter[];
  /** Every queue the PC knows, raw: the lazy queue chunk validates it. */
  queues?: unknown[];
  /** Every Skua's boosts, raw: the lazy queue chunk validates it. */
  boosts?: unknown[];
}

/** A character's numbers as the Skua plugin reads them; the nullable ones may be unknown (not read yet, or an older plugin). */
export interface SkuaProfile {
  level: number;
  xp: number;
  xpToLevel: number;
  gold: number;
  coins: number;
  classRank: number;
  isMember: boolean;
  className: string | null;
  classId: number | null;
  classPoints: number | null;
  guild: string | null;
  bagSlots: number | null;
  bankSlots: number | null;
  houseSlots: number | null;
}

/** Ready: hardFarms holds the result; NoList: the list is not imported; NoInventory: the plugin reported no inventory. */
export type HardFarmsState = 'Ready' | 'NoList' | 'NoInventory';

/** One row of the Characters list: a character live in a Skua or remembered, or a connected Skua with nobody logged in. */
export interface PhoneCharacter {
  /** The character name; the Skua label when hasCharacter is false. */
  name: string;
  hasCharacter: boolean;
  instanceId: string | null;
  live: boolean;
  lastSeenUtc: string | null;
  profile: SkuaProfile | null;
  factions: SkuaFaction[] | null;
  factionsLive: boolean;
  hardFarmsState: HardFarmsState;
  hardFarms: HardFarms | null;
  inventoryAtUtc: string | null;
  inventoryTruncated: boolean;
}
