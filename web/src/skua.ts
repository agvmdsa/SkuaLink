import { t } from './i18n';
import type { SkuaConnection, SkuaControl, SkuaFaction, SkuaInstance, SkuaLogEntry, SkuaQuest, SkuaState, SkuaStatus } from './types';

const MAX_TEXT = 300;
const MAX_ENTRIES = 100;
const MAX_QUESTS = 30;
const MAX_QUEST_ID = 2_147_483_647;
const MAX_INT32 = MAX_QUEST_ID;
const MAX_FACTIONS = 60;
const CONNECTIONS: readonly string[] = ['NotConnected', 'Connected', 'VersionMismatch'];
const MAX_INSTANCES = 8;
const CONTROLS: readonly string[] = ['idle', 'running', 'stopping', 'settling', 'awaitingOptions'];
/** The bound on a stored label; anything longer is not one of ours (labels are cut to MAX_TEXT). */
const MAX_LABEL = 300;
/** The localStorage key of the Skua last looked at on the old Skua tab, by its label: read only to redirect an old #/skua link. */
export const SKUA_INSTANCE_KEY = 'sl-skua-instance';

export const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | null => (typeof v === 'string' ? v : null);

function parseStatus(v: unknown): SkuaStatus | null {
  if (!isObject(v)) return null;
  return {
    loggedIn: v.loggedIn === true,
    character: str(v.character),
    server: str(v.server),
    map: str(v.map),
    script: str(v.script),
    scriptRunning: v.scriptRunning === true,
    runningSince: str(v.runningSince),
    quests: parseQuests(v.quests),
    factions: parseFactions(v.factions),
    control: typeof v.control === 'string' && CONTROLS.includes(v.control) ? (v.control as SkuaControl) : undefined,
    settleSecondsLeft: count(v.settleSecondsLeft) ?? undefined,
    optionsStuck: v.optionsStuck === true ? true : undefined,
  };
}

/** Cut to the text cap without leaving half of a surrogate pair at the end. */
export function cut(text: string): string {
  if (text.length <= MAX_TEXT) return text;
  const last = text.charCodeAt(MAX_TEXT - 1);
  return text.slice(0, last >= 0xd800 && last <= 0xdbff ? MAX_TEXT - 1 : MAX_TEXT);
}

/** The active quests; anything but an array is "not reported" (null), and a damaged entry is left out on its own. */
function parseQuests(v: unknown): SkuaQuest[] | null {
  if (!Array.isArray(v)) return null;
  const quests: SkuaQuest[] = [];
  for (const q of v as unknown[]) {
    if (quests.length >= MAX_QUESTS) break;
    if (!isObject(q) || typeof q.id !== 'number' || !Number.isInteger(q.id) || q.id < 0 || q.id > MAX_QUEST_ID) continue;
    if (typeof q.name !== 'string' || typeof q.ready !== 'boolean') continue;
    quests.push({ id: q.id, name: cut(q.name), ready: q.ready });
  }
  return quests;
}

/** A whole number from 0 to the int32 maximum, or null. */
export const count = (v: unknown): number | null => (typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= MAX_INT32 ? v : null);

/** The factions; anything but an array is "not reported" (null), and a damaged entry is left out on its own. */
export function parseFactions(v: unknown): SkuaFaction[] | null {
  if (!Array.isArray(v)) return null;
  const factions: SkuaFaction[] = [];
  for (const f of v as unknown[]) {
    if (factions.length >= MAX_FACTIONS) break;
    if (!isObject(f) || typeof f.name !== 'string') continue;
    const id = count(f.id), rank = count(f.rank), totalRep = count(f.totalRep), rep = count(f.rep), requiredRep = count(f.requiredRep);
    if (id === null || rank === null || totalRep === null || rep === null || requiredRep === null) continue;
    factions.push({ id, name: cut(f.name), rank, totalRep, rep, requiredRep });
  }
  return factions;
}

function parseHello(v: unknown): SkuaState['hello'] {
  if (!isObject(v) || typeof v.pluginVersion !== 'string' || typeof v.skuaVersion !== 'string') return null;
  return { pluginVersion: v.pluginVersion, skuaVersion: v.skuaVersion };
}

/** The Skua part of the state; anything that is not the expected shape is null (the Skua card is then not shown). */
export function parseSkua(value: unknown): SkuaState | null {
  if (!isObject(value) || typeof value.connection !== 'string' || !CONNECTIONS.includes(value.connection) || !Array.isArray(value.log)) return null;
  const log: SkuaLogEntry[] = [];
  for (const e of value.log as unknown[]) {
    if (log.length >= MAX_ENTRIES) break;
    if (!isObject(e) || typeof e.at !== 'string' || typeof e.text !== 'string') continue;
    log.push({ at: e.at, kind: typeof e.kind === 'string' ? e.kind : '', text: cut(e.text) });
  }
  return { connection: value.connection as SkuaConnection, hello: parseHello(value.hello), status: parseStatus(value.status), log };
}

/**
 * The instance list of the state; null, absent or anything but an array is undefined (an older app: the single Skua is shown as before).
 * A damaged entry is left out on its own, and no more than the eight the PC can hold are read.
 */
export function parseSkuaInstances(value: unknown): SkuaInstance[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const out: SkuaInstance[] = [];
  for (const item of value as unknown[]) {
    if (out.length >= MAX_INSTANCES) break;
    if (!isObject(item) || typeof item.instanceId !== 'string' || typeof item.label !== 'string') continue;
    const skua = parseSkua(item);
    if (!skua) continue;
    out.push({ ...skua, instanceId: cut(item.instanceId), label: cut(item.label), disconnected: item.disconnected === true });
  }
  return out;
}

export const isLive = (i: SkuaInstance): boolean => i.connection === 'Connected' && !i.disconnected;

/** The remembered label; storage can be blocked, and an odd value is not ours. */
export function loadInstance(storage: Pick<Storage, 'getItem'> | null | undefined): string | null {
  try {
    const raw = storage?.getItem(SKUA_INSTANCE_KEY) ?? null;
    return raw !== null && raw.length <= MAX_LABEL ? raw : null;
  } catch {
    return null;
  }
}

export interface InstanceState {
  text: string;
  /** How the dot looks: running (green), busy (orange: stopping, settling, waiting) or quiet. */
  tone: 'run' | 'warn' | 'none';
}

/** What an instance is doing, in words (never colour alone). */
export function instanceState(i: SkuaInstance): InstanceState {
  if (i.disconnected || i.connection === 'NotConnected') return { text: t('skua.state.disconnected'), tone: 'none' };
  if (i.connection === 'VersionMismatch') return { text: t('skua.state.mismatch'), tone: 'warn' };
  const st = i.status;
  if (!st || !st.loggedIn) return { text: t('skua.state.notLoggedIn'), tone: 'none' };
  if (st.control === 'awaitingOptions' || st.optionsStuck) return { text: t('skua.state.waiting'), tone: 'warn' };
  if (st.control === 'stopping') return { text: t('skua.state.stopping'), tone: 'warn' };
  if (st.control === 'settling') {
    return { text: st.settleSecondsLeft === undefined ? t('skua.state.settlingNoTime') : t('skua.state.settling', { n: st.settleSecondsLeft }), tone: 'warn' };
  }
  const running = st.control === undefined ? st.scriptRunning : st.control === 'running';
  return running ? { text: t('skua.state.running'), tone: 'run' } : { text: t('skua.state.idle'), tone: 'none' };
}

export const instanceStateText = (i: SkuaInstance): string => instanceState(i).text;
