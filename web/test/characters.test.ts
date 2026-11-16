import { describe, expect, it } from 'vitest';
import { badge, characterRows, factionRowsOf, findCharacter, controlTarget, lastSeen, overviewFacts, parseCharacters, skuaRedirect } from '../src/characters';
import type { PhoneCharacter, RemoteState, SkuaInstance } from '../src/types';

const NOW = Date.parse('2026-11-15T12:00:00Z');
const iso = (minutesAgo: number) => new Date(NOW - minutesAgo * 60_000).toISOString();
const profile = { level: 60, xp: 340, xpToLevel: 5000, gold: 93650135, coins: 777, classRank: 10, isMember: true, className: 'Void Highlord', classId: 31, classPoints: 302500, guild: 'Heroes', bagSlots: 140, bankSlots: 300, houseSlots: 50 };
const hero = (over: Partial<PhoneCharacter> = {}): PhoneCharacter => ({
  name: 'Hero <b>x</b>', hasCharacter: true, instanceId: 'a', live: true, lastSeenUtc: iso(0), profile, factions: [], factionsLive: true,
  hardFarmsState: 'Ready', hardFarms: { owned: 1, total: 2, missing: ['X'] }, inventoryAtUtc: iso(3), inventoryTruncated: false, ...over,
});
const instance = (over: Partial<SkuaInstance> = {}): SkuaInstance => ({
  instanceId: 'a', label: 'Hero <b>x</b>', disconnected: false, connection: 'Connected', hello: null, log: [],
  status: { loggedIn: true, character: 'Hero <b>x</b>', server: null, map: null, script: null, scriptRunning: true, runningSince: null, quests: [], factions: [], control: 'running' }, ...over,
});
const state = (characters: PhoneCharacter[], skuaInstances: SkuaInstance[] = [instance()]): RemoteState => ({ snapshot: null, notices: [], characters, skuaInstances });

describe('parseCharacters', () => {
  it('keeps well-formed entries, drops damaged ones and reads anything else as absent', () => {
    expect(parseCharacters('x')).toBeUndefined();
    const parsed = parseCharacters([hero(), { name: 5 }, { ...hero(), profile: { level: 'x' } }])!;
    expect(parsed).toHaveLength(2);
    expect(parsed[1]!.profile).toBeNull();
  });
  it('reads at most 40 characters and cuts names to 300 characters', () => {
    const many = Array.from({ length: 50 }, (_, i) => hero({ name: `${'n'.repeat(400)}${i}` }));
    const parsed = parseCharacters(many)!;
    expect(parsed).toHaveLength(40);
    expect(parsed[0]!.name).toHaveLength(300);
  });
  it('reads a damaged membership as not a member and keeps the numbers', () => {
    for (const isMember of ['yes', 1, null, undefined]) {
      const p = parseCharacters([{ ...hero(), profile: { ...profile, isMember } }])![0]!.profile;
      expect(p).not.toBeNull();
      expect(p!.isMember).toBe(false);
      expect(p!.level).toBe(60);
    }
  });
  it('keeps at most 200 missing Hard Farms items', () => {
    const missing = Array.from({ length: 250 }, (_, i) => `Item ${i}`);
    const hf = parseCharacters([{ ...hero(), hardFarms: { owned: 1, total: 251, missing } }])![0]!.hardFarms!;
    expect(hf.missing).toHaveLength(200);
    expect(hf.missing[199]).toBe('Item 199');
  });
  it('turns an unknown hard farms state into NoInventory', () => expect(parseCharacters([{ ...hero(), hardFarmsState: 'Odd' }])![0]!.hardFarmsState).toBe('NoInventory'));
});

describe('badge', () => {
  it('is the instance state while live', () => expect(badge(hero(), instance())).toEqual({ text: 'Running', kind: 'live' }));
  it('says Not connected without a live instance', () => {
    expect(badge(hero({ live: false }), instance({ disconnected: true }))).toEqual({ text: 'Not connected', kind: 'saved' });
    expect(badge(hero({ live: false, instanceId: null }), null)).toEqual({ text: 'Not connected', kind: 'saved' });
  });
  it.each([['stopping', 'Stopping', 'idle'], ['idle', 'Idle', 'saved'], ['awaitingOptions', 'Waiting for you', 'idle']] as const)('shows %s as %s', (control, text, kind) => {
    const i = instance({ status: { ...instance().status!, control, scriptRunning: control !== 'idle' } });
    expect(badge(hero(), i)).toEqual({ text, kind });
  });
});

describe('characterRows', () => {
  it('links each character to its remembered section and shows the age only when not live', () => {
    const rows = characterRows(state([hero(), hero({ name: 'Alt', live: false, instanceId: null, lastSeenUtc: iso(90) })]), NOW, (n) => (n === 'Alt' ? 'bot' : 'overview'));
    expect(rows.map((r) => r.href)).toEqual(['#/character/Hero%20%3Cb%3Ex%3C%2Fb%3E', '#/character/Alt/bot']);
    expect(rows[0]!.age).toBeNull();
    expect(rows[1]!.age).toBe('Last seen 1 h ago');
    expect(rows[0]!.level).toBe('Lv 60');
  });
  it('shows a Skua without a character as a row with no link', () => {
    const row = characterRows(state([hero({ name: 'Skua 1', hasCharacter: false, profile: null })]), NOW)[0]!;
    expect(row.href).toBeNull();
    expect(row.level).toBe('No character logged in');
  });
  it('is empty without data', () => expect(characterRows(null, NOW)).toEqual([]));
});

describe('overviewFacts', () => {
  it('lists the known numbers in order and leaves unknown ones out', () => {
    expect(overviewFacts(profile).map((f) => f.label)).toEqual(['Level', 'Experience', 'Gold', 'AC', 'Class', 'Class points', 'Membership', 'Guild', 'Slots']);
    const bare = overviewFacts({ ...profile, className: null, classPoints: null, guild: null, bagSlots: null, bankSlots: null, houseSlots: null });
    expect(bare.map((f) => f.label)).toEqual(['Level', 'Experience', 'Gold', 'AC', 'Class', 'Membership']);
    expect(bare.find((f) => f.label === 'Class')!.value).toBe('Rank 10');
  });
});

describe('factionRowsOf', () => {
  it('orders closest to the next rank first, the maximum rank last', () => {
    const rows = factionRowsOf([
      { id: 1, name: 'Good', rank: 10, totalRep: 302500, rep: 0, requiredRep: 0 },
      { id: 2, name: 'Nation', rank: 3, totalRep: 5000, rep: 300, requiredRep: 1200 },
    ]);
    expect(rows.map((r) => [r.name, r.max, r.toNext])).toEqual([['Nation', false, 900], ['Good', true, 0]]);
  });
});

describe('findCharacter', () => {
  it('finds by exact name only', () => {
    const s = state([hero()]);
    expect(findCharacter(s, 'Hero <b>x</b>')).not.toBeNull();
    expect(findCharacter(s, 'hero <b>x</b>')).toBeNull();
  });
  it('prefers a character over a character-less Skua row of the same name', () => {
    const s = state([hero({ hasCharacter: false, profile: null }), hero()]);
    expect(findCharacter(s, 'Hero <b>x</b>')!.hasCharacter).toBe(true);
    expect(findCharacter(state([hero({ hasCharacter: false })]), 'Hero <b>x</b>')!.hasCharacter).toBe(false);
  });
});

describe('lastSeen', () => {
  it('is null when the time is missing or does not parse', () => {
    expect(lastSeen(hero({ lastSeenUtc: null }), NOW)).toBeNull();
    expect(lastSeen(hero({ lastSeenUtc: 'not a time' }), NOW)).toBeNull();
    expect(lastSeen(hero({ lastSeenUtc: iso(90) }), NOW)).toBe('Last seen 1 h ago');
  });
});

describe('skuaRedirect', () => {
  it('goes to the Bot section of the remembered Skua character, else the first live one, else Characters', () => {
    const s = state([hero(), hero({ name: 'Alt', instanceId: 'b' })], [instance(), instance({ instanceId: 'b', label: 'Alt', status: { ...instance().status!, character: 'Alt' } })]);
    expect(skuaRedirect(s, 'alt')).toBe('#/character/Alt/bot');
    expect(skuaRedirect(s, 'Nobody')).toBe('#/character/Hero%20%3Cb%3Ex%3C%2Fb%3E/bot');
    expect(skuaRedirect(state([hero({ live: false })], []), null)).toBe('#/');
    expect(skuaRedirect(null, null)).toBe('#/');
  });
  it('skips characters whose Skua is gone or disconnected, and Skua rows without a character', () => {
    const s = state([hero({ name: 'Skua 1', hasCharacter: false, instanceId: 'c' }), hero({ instanceId: 'b' })], [instance({ instanceId: 'c', label: 'Skua 1' }), instance({ instanceId: 'b', disconnected: true })]);
    expect(skuaRedirect(s, 'Skua 1')).toBe('#/');
  });
});

describe('controlTarget', () => {
  const bot = { view: 'character', name: 'Hero <b>x</b>', section: 'bot' } as const;
  it('is the live Skua of the character on its Bot section', () => {
    expect(controlTarget(state([hero()]), bot)!.instanceId).toBe('a');
  });
  it('is null on the Overview, the list and other routes', () => {
    expect(controlTarget(state([hero()]), { ...bot, section: 'overview' })).toBeNull();
    expect(controlTarget(state([hero()]), { view: 'list' })).toBeNull();
    expect(controlTarget(state([hero()]), { view: 'skua' })).toBeNull();
    expect(controlTarget(null, bot)).toBeNull();
  });
  it('is null for a stale or missing instance id, a Skua that is not live, a version mismatch and a character-less row', () => {
    expect(controlTarget(state([hero({ instanceId: 'gone' })]), bot)).toBeNull();
    expect(controlTarget(state([hero({ instanceId: null })]), bot)).toBeNull();
    expect(controlTarget(state([hero({ live: false })]), bot)).toBeNull();
    expect(controlTarget(state([hero()], [instance({ disconnected: true })]), bot)).toBeNull();
    expect(controlTarget(state([hero()], [instance({ connection: 'VersionMismatch' })]), bot)).toBeNull();
    expect(controlTarget(state([hero({ hasCharacter: false })]), bot)).toBeNull();
  });
});
