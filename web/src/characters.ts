import { t, type MessageKey } from './i18n';
import { count, cut, instanceState, isLive, isObject, parseFactions, type InstanceState } from './skua';
import { characterHref, formatCompact, formatNumber, relativeTime, type Section } from './state';
import type { HardFarms, HardFarmsState, PhoneCharacter, RemoteState, SkuaFaction, SkuaInstance, SkuaProfile } from './types';

const MAX_CHARACTERS = 40;
const HARD_FARMS_STATES: readonly HardFarmsState[] = ['Ready', 'NoList', 'NoInventory'];
const FLAGS = ['hasCharacter', 'live', 'factionsLive', 'inventoryTruncated'] as const;
const TEXTS = ['instanceId', 'lastSeenUtc', 'inventoryAtUtc'] as const;

const text = (v: unknown): string | null => (typeof v === 'string' ? cut(v) : null);

const NUMBERS = ['level', 'xp', 'xpToLevel', 'gold', 'coins', 'classRank'] as const;
const OPTIONAL_NUMBERS = ['classId', 'classPoints', 'bagSlots', 'bankSlots', 'houseSlots'] as const;

/**
 * The numbers; null when a number every plugin sends is missing or damaged. An unknown or damaged optional part is null on its own,
 * and a damaged membership reads as not a member.
 */
function parseProfile(v: unknown): SkuaProfile | null {
  if (!isObject(v)) return null;
  const p: Record<string, unknown> = { isMember: v.isMember === true, className: text(v.className), guild: text(v.guild) };
  for (const key of NUMBERS) if ((p[key] = count(v[key])) === null) return null;
  for (const key of OPTIONAL_NUMBERS) p[key] = count(v[key]);
  return p as unknown as SkuaProfile;
}

function parseHardFarms(v: unknown): HardFarms | null {
  if (!isObject(v) || !Array.isArray(v.missing)) return null;
  const owned = count(v.owned), total = count(v.total);
  if (owned === null || total === null) return null;
  const missing = (v.missing as unknown[]).filter((m): m is string => typeof m === 'string').map(cut);
  return { owned, total, missing };
}

/** The characters of the state: anything but an array is absent (an older app), a damaged entry is left out, at most 40 are read. */
export function parseCharacters(v: unknown): PhoneCharacter[] | undefined {
  if (!Array.isArray(v)) return undefined;
  const out: PhoneCharacter[] = [];
  for (const c of v as unknown[]) {
    if (out.length >= MAX_CHARACTERS) break;
    if (!isObject(c) || typeof c.name !== 'string') continue;
    const parsed: Record<string, unknown> = {
      name: cut(c.name), profile: parseProfile(c.profile), factions: parseFactions(c.factions), hardFarms: parseHardFarms(c.hardFarms),
      hardFarmsState: (HARD_FARMS_STATES as readonly unknown[]).includes(c.hardFarmsState) ? c.hardFarmsState : 'NoInventory',
    };
    for (const key of FLAGS) parsed[key] = c[key] === true;
    for (const key of TEXTS) parsed[key] = text(c[key]);
    out.push(parsed as unknown as PhoneCharacter);
  }
  return out;
}

/** The character of that exact name, or null. */
export function findCharacter(state: RemoteState | null, name: string): PhoneCharacter | null {
  return state?.characters?.find((c) => c.name === name) ?? null;
}

/** The Skua the character is (or was last) played in, if the PC still lists it. */
export function instanceOf(state: RemoteState | null, c: PhoneCharacter): SkuaInstance | null {
  // An instance id is never null, so a character with none matches nothing.
  return state?.skuaInstances?.find((i) => i.instanceId === c.instanceId) ?? null;
}

export type BadgeKind = 'live' | 'idle' | 'saved';

/** The status dot of an instance state: running is live (green), busy is idle (orange), anything else is quiet. */
export const TONE_KIND: Readonly<Record<InstanceState['tone'], BadgeKind>> = { run: 'live', warn: 'idle', none: 'saved' };

/** What the character's bot is doing, in words: the state of its live Skua, else Not connected. */
export function badge(c: PhoneCharacter, i: SkuaInstance | null): { text: string; kind: BadgeKind } {
  if (!c.live || !i || !isLive(i)) return { text: t('bot.notConnected'), kind: 'saved' };
  const st = instanceState(i);
  return { text: st.text, kind: TONE_KIND[st.tone] };
}

/** "Last seen 1 h ago", or null when the time is unknown. */
export function lastSeen(c: PhoneCharacter, nowMs: number): string | null {
  return c.lastSeenUtc ? t('character.lastSeen', { when: relativeTime(c.lastSeenUtc, nowMs) }) : null;
}

export interface CharacterRowView {
  name: string;
  /** Null for a Skua with nobody logged in: there is no character to open. */
  href: string | null;
  level: string | null;
  gold: string | null;
  badge: { text: string; kind: BadgeKind };
  /** When it was last seen; only when it is not live. */
  age: string | null;
}

export function characterRows(state: RemoteState | null, nowMs: number, sectionOf?: (name: string) => Section): CharacterRowView[] {
  return (state?.characters ?? []).map((c) => {
    const p = c.profile;
    return {
      name: c.name,
      href: c.hasCharacter ? characterHref(c.name, sectionOf?.(c.name)) : null,
      level: !c.hasCharacter ? t('list.noCharacter') : p ? t('list.level', { level: p.level }) : null,
      gold: p ? t('list.gold', { gold: formatCompact(p.gold) }) : null,
      badge: badge(c, instanceOf(state, c)),
      age: c.live ? null : lastSeen(c, nowMs),
    };
  });
}

/** The Overview's figures in order; an unknown one is left out (Slots only when all three totals are known). */
export function overviewFacts(p: SkuaProfile): { label: string; value: string }[] {
  const fact = (key: MessageKey, value: string) => ({ label: t(key), value });
  const facts = [
    fact('character.level', String(p.level)),
    fact('character.exp', t('character.expOf', { exp: formatNumber(p.xp), toLevel: formatNumber(p.xpToLevel) })),
    fact('character.gold', formatNumber(p.gold)),
    fact('character.coins', formatNumber(p.coins)),
    fact('character.class', p.className ? t('character.classValue', { name: p.className, rank: p.classRank }) : t('character.classRank', { rank: p.classRank })),
  ];
  if (p.classPoints !== null) facts.push(fact('character.classPoints', formatNumber(p.classPoints)));
  facts.push(fact('character.membership', t(p.isMember ? 'character.member' : 'character.notMember')));
  if (p.guild) facts.push(fact('character.guild', p.guild));
  if (p.bagSlots !== null && p.bankSlots !== null && p.houseSlots !== null) {
    facts.push(fact('character.slots', t('character.slotsValue', { bag: p.bagSlots, bank: p.bankSlots, house: p.houseSlots })));
  }
  return facts;
}

export interface FactionRow {
  name: string;
  rank: number;
  /** The maximum rank: rank 10, or a rank step of 0. */
  max: boolean;
  /** The share of the current rank already earned, 0 to 1 (1 at the maximum rank). */
  fraction: number;
  toNext: number;
}

const finite = (n: number): number => (Number.isFinite(n) ? n : 0);

/** The ordering key: what is missing for the next rank, the maximum rank last. */
const missing = (r: FactionRow): number => (r.max ? Number.POSITIVE_INFINITY : r.toNext);

/** Ordinal comparison ignoring case, like the app's StringComparer.OrdinalIgnoreCase. */
const byName = (a: string, b: string): number => {
  const x = a.toUpperCase(), y = b.toUpperCase();
  return x < y ? -1 : x > y ? 1 : 0;
};

/** The Reputation card's rows: closest to the next rank first, the maximum rank last, ties by name ignoring case. */
export function factionRowsOf(factions: readonly SkuaFaction[]): FactionRow[] {
  return factions
    .map((f): FactionRow => {
      const max = f.rank >= 10 || f.requiredRep <= 0;
      return {
        name: f.name,
        rank: f.rank,
        max,
        fraction: max ? 1 : Math.min(1, Math.max(0, finite(f.rep / f.requiredRep))),
        toNext: max ? 0 : Math.max(0, finite(f.requiredRep - f.rep)),
      };
    })
    .sort((a, b) => missing(a) - missing(b) || byName(a.name, b.name));
}
