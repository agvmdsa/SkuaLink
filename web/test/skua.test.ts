import { describe, expect, it } from 'vitest';
import { instanceStateText, loadInstance, parseSkua, parseSkuaInstances, SKUA_INSTANCE_KEY } from '../src/skua';
import { isUsableState, keepState, noticeLine, parseRoute, parseState, recallKept } from '../src/state';
import { characterView, type Model, type VNode } from '../src/views';
import { factionRowsOf } from '../src/characters';
import type { PhoneCharacter, RemoteState, SkuaFaction, SkuaInstance } from '../src/types';

const NOW = Date.parse('2026-11-11T12:12:00Z');
const wire = {
  connection: 'Connected',
  hello: { pluginVersion: '0.1.0', skuaVersion: '1.4.5.0' },
  status: { loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: true, runningSince: '2026-11-11T12:00:00+00:00', quests: null, factions: null },
  log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: 'Sold Blood Orb x50 for 2,500 gold' }],
};
const faction = (id: number, name: string, rank: number, rep: number, requiredRep: number, totalRep = 0) => ({ id, name, rank, totalRep, rep, requiredRep });
const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];
const texts = (n: VNode) => all(n).map((x) => x.text).filter((t): t is string => t !== undefined);

describe('parseSkua', () => {
  it('accepts the full example', () => expect(parseSkua(wire)).toEqual(wire));
  it('accepts NotConnected with no status and an empty log', () => {
    expect(parseSkua({ connection: 'NotConnected', hello: null, status: null, log: [] })?.connection).toBe('NotConnected');
  });
  it('rejects an unknown connection and a log that is not an array', () => {
    expect(parseSkua({ ...wire, connection: 'Maybe' })).toBeNull();
    expect(parseSkua({ ...wire, log: 'x' })).toBeNull();
    expect(parseSkua('x')).toBeNull();
    expect(parseSkua(null)).toBeNull();
  });
  it('drops entries without text or at, caps text at 300 and the list at 100', () => {
    const many = Array.from({ length: 150 }, (_, i) => ({ at: '2026-11-11T12:00:00Z', kind: 'Sold', text: `e${i}` }));
    const s = parseSkua({ ...wire, log: [{ at: 'x', kind: 'Sold' }, { text: 'no at' }, { at: 'a', kind: 'k', text: 'z'.repeat(1000) }, ...many] })!;
    expect(s.log).toHaveLength(100);
    expect(s.log[0]!.text).toHaveLength(300);
    expect(parseSkua({ ...wire, log: [{ at: 'x', kind: 'Sold' }] })!.log).toEqual([]);
  });
  it('cuts a log text without splitting a surrogate pair', () => {
    const s = parseSkua({ ...wire, log: [{ at: 'a', kind: 'k', text: 'a'.repeat(299) + '\u{1F600}tail' }] })!;
    expect(s.log[0]!.text).toBe('a'.repeat(299));
  });
});

describe('parseSkua quests', () => {
  const withQuests = (quests: unknown) => ({ ...wire, status: { ...wire.status, quests } });
  const quests = (value: unknown) => parseSkua(withQuests(value))!.status!.quests;

  it('reads the active quests with their ready flag', () => {
    expect(quests([{ id: 5, name: 'Slay the Beast', ready: true }, { id: 12, name: 'Gather 10 Bones', ready: false }])).toEqual([
      { id: 5, name: 'Slay the Beast', ready: true },
      { id: 12, name: 'Gather 10 Bones', ready: false },
    ]);
  });
  it('keeps null (not reported) apart from an empty list (none active)', () => {
    expect(quests([])).toEqual([]);
    expect(quests(null)).toBeNull();
  });
  it('reads an older payload without quests as not reported', () => {
    const { quests: _omit, ...old } = wire.status;
    expect(parseSkua({ ...wire, status: old })!.status!.quests).toBeNull();
  });
  it('treats a quests value that is not an array as not reported, keeping the rest of the status', () => {
    for (const bad of ['x', 5, true, { id: 1, name: 'a', ready: true }]) {
      const s = parseSkua(withQuests(bad))!;
      expect(s.status!.quests).toBeNull();
      expect(s.status!.character).toBe('Hero');
    }
  });
  it('drops a malformed entry and keeps the others', () => {
    const good = { id: 5, name: 'Slay the Beast', ready: true };
    const bad = [
      null, 'q', 7, [], { id: -1, name: 'n', ready: true }, { id: 1.5, name: 'n', ready: true }, { id: '7', name: 'n', ready: true },
      { id: 2_147_483_648, name: 'n', ready: true }, { id: Number.NaN, name: 'n', ready: true }, { name: 'n', ready: true },
      { id: 7, name: null, ready: true }, { id: 7, ready: true }, { id: 7, name: 'n', ready: 'true' }, { id: 7, name: 'n' },
    ];
    expect(quests([...bad, good])).toEqual([good]);
  });
  it('keeps at most 30 quests and cuts a long name to 300 without splitting a surrogate pair', () => {
    const many = Array.from({ length: 40 }, (_, i) => ({ id: i, name: `Q${i}`, ready: false }));
    const list = quests(many)!;
    expect(list).toHaveLength(30);
    expect(list[29]!.id).toBe(29);
    expect(quests([{ id: 1, name: 'n'.repeat(400), ready: true }])![0]!.name).toHaveLength(300);
    const pair = quests([{ id: 1, name: 'a'.repeat(299) + '\u{1F600}tail', ready: true }])![0]!.name;
    expect(pair).toBe('a'.repeat(299));
  });
  it('never throws on hostile input', () => {
    expect(() => parseSkua(withQuests([{ id: 1, name: { toString: 1 }, ready: true }, Object.create(null)]))).not.toThrow();
  });
});

describe('parseSkua factions', () => {
  const withFactions = (factions: unknown) => ({ ...wire, status: { ...wire.status, factions } });
  const factions = (value: unknown) => parseSkua(withFactions(value))!.status!.factions;
  const good = faction(1, 'Nation', 3, 300, 1200, 5000);

  it('reads the factions', () => {
    expect(factions([good, faction(20, 'Good', 10, 0, 0, 302500)])).toEqual([good, faction(20, 'Good', 10, 0, 0, 302500)]);
  });
  it('keeps null (not reported) apart from an empty list (none)', () => {
    expect(factions([])).toEqual([]);
    expect(factions(null)).toBeNull();
    const { factions: _omit, ...old } = wire.status;
    expect(parseSkua({ ...wire, status: old })!.status!.factions).toBeNull();
  });
  it('treats a factions value that is not an array as not reported, keeping the rest of the status', () => {
    for (const bad of ['x', 5, true, good]) {
      const s = parseSkua(withFactions(bad))!;
      expect(s.status!.factions).toBeNull();
      expect(s.status!.character).toBe('Hero');
    }
  });
  it('drops a malformed entry and keeps the others', () => {
    const bad = [
      null, 'f', 7, [], { ...good, id: -1 }, { ...good, rank: -1 }, { ...good, totalRep: -1 }, { ...good, rep: -1 }, { ...good, requiredRep: -1 },
      { ...good, id: 1.5 }, { ...good, rep: 0.5 }, { ...good, id: '7' }, { ...good, rank: '3' }, { ...good, totalRep: 2_147_483_648 },
      { ...good, rep: Number.NaN }, { ...good, requiredRep: Infinity }, { ...good, name: null }, { ...good, name: 5 },
      { id: 1, rank: 1, totalRep: 1, rep: 1, requiredRep: 1 }, { id: 1, name: 'n', totalRep: 1, rep: 1, requiredRep: 1 }, { id: 1, name: 'n', rank: 1, rep: 1, requiredRep: 1 },
      { id: 1, name: 'n', rank: 1, totalRep: 1, requiredRep: 1 }, { id: 1, name: 'n', rank: 1, totalRep: 1, rep: 1 },
    ];
    expect(factions([...bad, good])).toEqual([good]);
  });
  it('keeps the largest int32 values', () => {
    const max = faction(2_147_483_647, 'Max', 2_147_483_647, 2_147_483_647, 2_147_483_647, 2_147_483_647);
    expect(factions([max])).toEqual([max]);
  });
  it('keeps at most 60 factions and cuts a long name to 300 without splitting a surrogate pair', () => {
    const list = factions(Array.from({ length: 70 }, (_, i) => faction(i, `F${i}`, 1, 1, 900)))!;
    expect(list).toHaveLength(60);
    expect(list[59]!.id).toBe(59);
    expect(factions([{ ...good, name: 'n'.repeat(400) }])![0]!.name).toHaveLength(300);
    expect(factions([{ ...good, name: 'a'.repeat(299) + '\u{1F600}tail' }])![0]!.name).toBe('a'.repeat(299));
  });
  it('never throws on hostile input', () => {
    expect(() => parseSkua(withFactions([{ ...good, name: { toString: 1 } }, Object.create(null)]))).not.toThrow();
  });
});

// factionRows (the Skua tab's list) left with the tab; the same ordering and math are factionRowsOf, used by the Overview.
describe('factionRowsOf', () => {
  const rows = (list: SkuaFaction[]) => factionRowsOf(list);

  it('orders by what is missing for the next rank, max rank last, then by name ignoring case, like the capture list', () => {
    const list = [faction(1, 'zeta', 2, 100, 1000), faction(2, 'Good', 10, 0, 0), faction(3, 'alpha', 5, 950, 1000), faction(4, 'Beta', 3, 900, 1000), faction(5, 'Evil', 10, 0, 0), faction(6, 'gamma', 4, 50, 100)];
    expect(rows(list).map((r) => r.name)).toEqual(['alpha', 'gamma', 'Beta', 'zeta', 'Evil', 'Good']);
  });
  it('computes rank, the share of the rank, what is left and the rank text', () => {
    expect(rows([faction(1, 'Nation', 3, 300, 1200)])).toEqual([{ name: 'Nation', rank: 3, max: false, fraction: 0.25, toNext: 900 }]);
  });
  it('treats a step of 0 as the maximum rank: full bar, nothing to next rank', () => {
    expect(rows([faction(1, 'Good', 10, 0, 0)])).toEqual([{ name: 'Good', rank: 10, max: true, fraction: 1, toNext: 0 }]);
  });
  it('is the maximum rank at rank 10 or with a step of 0, and otherwise not', () => {
    expect(rows([faction(1, 'Ten', 10, 40, 500)])[0]).toMatchObject({ max: true, fraction: 1, toNext: 0 });
    expect(rows([faction(1, 'Zero', 4, 0, 0)])[0]).toMatchObject({ max: true, fraction: 1, toNext: 0 });
    expect(rows([faction(1, 'Nine', 9, 40, 500)])[0]).toMatchObject({ max: false, toNext: 460 });
    expect(rows([faction(1, 'Alpha', 9, 400, 500), faction(2, 'Ten', 10, 499, 500), faction(3, 'Beta', 1, 0, 0)]).map((r) => r.name)).toEqual(['Alpha', 'Beta', 'Ten']);
  });
  it('clamps rep beyond the step to a full bar with nothing left', () => {
    expect(rows([faction(1, 'Odd', 4, 1500, 1000)])[0]).toMatchObject({ max: false, fraction: 1, toNext: 0 });
    expect(rows([faction(1, 'Zero', 1, 0, 1000)])[0]).toMatchObject({ fraction: 0, toNext: 1000 });
  });
  it('never gives a NaN fraction', () => {
    for (const f of [faction(1, 'a', 1, Number.NaN, 1000), faction(1, 'b', 1, 5, Number.NaN), faction(1, 'c', 1, Infinity, Infinity)]) {
      const fraction = rows([f])[0]!.fraction;
      expect(Number.isFinite(fraction)).toBe(true);
      expect(fraction).toBeGreaterThanOrEqual(0);
      expect(fraction).toBeLessThanOrEqual(1);
    }
  });
  it('is empty when there are none', () => expect(rows([])).toEqual([]));
});

describe('the character reputation', () => {
  const live = [faction(1, 'zeta', 2, 100, 1000, 5100), faction(2, 'Good', 10, 0, 0, 9000), faction(3, 'alpha', 5, 950, 1000, 4950)];
  const hero = (factionsLive: boolean): PhoneCharacter => ({
    name: 'Hero', hasCharacter: true, instanceId: null, live: factionsLive, lastSeenUtc: '2026-11-11T12:00:00Z', profile: null, factions: live, factionsLive,
    hardFarmsState: 'NoList', hardFarms: null, inventoryAtUtc: null, inventoryTruncated: false,
  });
  const m = (c: PhoneCharacter): Model => ({ state: { snapshot: null, notices: [], characters: [c] }, nowMs: NOW, showAll: false, hfOpen: false, installHint: null });

  it('orders the rows like the Skua list: closest to the next rank first, the maximum rank last', () => {
    expect(factionRowsOf(live)).toEqual([
      { name: 'alpha', rank: 5, toNext: 50, fraction: 0.95, max: false },
      { name: 'zeta', rank: 2, toNext: 900, fraction: 0.1, max: false },
      { name: 'Good', rank: 10, toNext: 0, fraction: 1, max: true },
    ]);
  });
  it('says Live from Skua once, and only for a live character', () => {
    const fromSkua = texts(characterView(m(hero(true)), 'Hero'));
    expect(fromSkua).toEqual(expect.arrayContaining(['Live from Skua', 'alpha']));
    expect(fromSkua.filter((x) => x === 'Live from Skua')).toHaveLength(1);
    const stored = texts(characterView(m(hero(false)), 'Hero'));
    expect(stored).toContain('alpha');
    expect(stored).not.toContain('Live from Skua');
  });
});

describe('state, route and storage', () => {
  const base = { snapshot: null, notices: [] };
  it('treats a payload without skua as null and degrades a string skua to null', () => {
    expect(parseState(JSON.stringify(base))!.skua).toBeNull();
    expect(isUsableState({ ...base, skua: 'x' } as unknown as RemoteState)).toBe(true);
    expect(parseState(JSON.stringify({ ...base, skua: 'x' }))).toEqual({ snapshot: null, notices: [], skua: null });
    expect(parseState(JSON.stringify({ ...base, skua: [1] }))!.skua).toBeNull();
    expect(parseState(JSON.stringify({ ...base, skua: wire }))!.skua).toEqual(wire);
  });
  it('keeps skua through the stored round trip and loads an old stored value', () => {
    const state = parseState(JSON.stringify({ ...base, skua: wire }))!;
    expect(recallKept(keepState(state, NOW - 1000), NOW)!.state.skua).toEqual(wire);
    expect(recallKept(JSON.stringify({ receivedAtMs: NOW - 1000, state: base }), NOW)!.state.skua).toBeNull();
  });
  it('loads a stored value with a garbage skua as null', () => {
    const text = JSON.stringify({ receivedAtMs: NOW - 1000, state: { ...base, skua: { connection: 7 } } });
    expect(recallKept(text, NOW)!.state.skua).toBeNull();
  });
  it('routes #/skua only', () => {
    expect(parseRoute('#/skua')).toEqual({ view: 'skua' });
    expect(parseRoute('#/skua/x')).toEqual({ view: 'list' });
  });
});

// Moved from instances.test.ts (deleted with the instance selector): the instance list parsing, the state words, the Skua notices
// and the remembered label (still read for the #/skua redirect).
const status = (over: object = {}) => ({
  loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: false, runningSince: null, quests: null, factions: null,
  control: 'idle', settleSecondsLeft: null, optionsStuck: false, ...over,
});
const wireInstance = (label: string, over: Record<string, unknown> = {}, st: object = {}) => ({
  instanceId: `id-${label}`, label, connection: 'Connected', disconnected: false, hello: { pluginVersion: '0.2.0', skuaVersion: '1.4.5.0', instanceId: `id-${label}` },
  status: status({ character: label, ...st }), log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: `Sold by ${label}` }], ...over,
});
const inst = (label: string, over: Record<string, unknown> = {}, st: object = {}): SkuaInstance => parseSkuaInstances([wireInstance(label, over, st)])![0]!;

describe('parseSkuaInstances', () => {
  it('reads the server shape', () => {
    const [i] = parseSkuaInstances([wireInstance('Hero', {}, { control: 'settling', settleSecondsLeft: 7 })])!;
    expect(i).toMatchObject({ instanceId: 'id-Hero', label: 'Hero', connection: 'Connected', disconnected: false });
    expect(i!.status).toMatchObject({ control: 'settling', settleSecondsLeft: 7 });
    expect(i!.status!.optionsStuck).toBeFalsy();
    expect(i!.log).toHaveLength(1);
  });
  it.each([[null], [undefined], ['x'], [{}]])('is undefined for %j (an older app, or no Skua feed)', (v) => expect(parseSkuaInstances(v)).toBeUndefined());
  it('keeps an empty list as empty', () => expect(parseSkuaInstances([])).toEqual([]));
  it('drops damaged entries on their own and keeps order', () => {
    const r = parseSkuaInstances([wireInstance('A'), { label: 'no id' }, 7, wireInstance('B', { connection: 'Maybe' }), wireInstance('C')])!;
    expect(r.map((i) => i.label)).toEqual(['A', 'C']);
  });
  it('caps at 8 instances and cuts long labels', () => {
    expect(parseSkuaInstances(Array.from({ length: 12 }, (_, i) => wireInstance(`H${i}`)))).toHaveLength(8);
    expect(parseSkuaInstances([wireInstance('x'.repeat(1000))])![0]!.label.length).toBeLessThanOrEqual(300);
  });
  it('ignores an unknown control value and a bad settle count', () => {
    const s = inst('A', {}, { control: 'dancing', settleSecondsLeft: -3, optionsStuck: 'yes' }).status!;
    expect(s.control).toBeUndefined();
    expect(s.settleSecondsLeft).toBeUndefined();
    expect(s.optionsStuck).toBeFalsy();
  });
  it('is read by parseState next to the old skua field', () => {
    const s = parseState(JSON.stringify({ snapshot: null, notices: [], skua: wireInstance('Hero'), skuaInstances: [wireInstance('Hero'), wireInstance('Mule')] }))!;
    expect(s.skuaInstances?.map((i) => i.label)).toEqual(['Hero', 'Mule']);
    expect(s.skua?.connection).toBe('Connected');
    expect(parseState('{"snapshot":null,"notices":[]}')!.skuaInstances).toBeUndefined();
  });
});

describe('the remembered Skua label', () => {
  const mem = (initial?: string) => ({ getItem: (k: string) => (k === SKUA_INSTANCE_KEY && initial !== undefined ? initial : null), setItem: () => undefined });
  it('uses the key sl-skua-instance', () => expect(SKUA_INSTANCE_KEY).toBe('sl-skua-instance'));
  it('reads the stored label', () => expect(loadInstance(mem('Hero2'))).toBe('Hero2'));
  it('survives blocked storage and a missing store', () => {
    const blocked = { getItem: () => { throw new Error('x'); }, setItem: () => { throw new Error('x'); } };
    expect(loadInstance(blocked)).toBeNull();
    expect(loadInstance(null)).toBeNull();
  });
  it('ignores an oversized stored value', () => expect(loadInstance(mem('x'.repeat(5000)))).toBeNull());
});

describe('instanceStateText', () => {
  it.each([
    ['Running', {}, { control: 'running', scriptRunning: true }],
    ['Idle', {}, {}],
    ['Stopping', {}, { control: 'stopping' }],
    ['Settling 7 s', {}, { control: 'settling', settleSecondsLeft: 7 }],
    ['Settling', {}, { control: 'settling', settleSecondsLeft: null }],
    ['Waiting for you', {}, { control: 'awaitingOptions' }],
    ['Waiting for you', {}, { optionsStuck: true }],
    ['Not logged in', {}, { loggedIn: false }],
    ['Disconnected', { disconnected: true, connection: 'NotConnected' }, {}],
    ['Disconnected', { disconnected: true }, { control: 'running' }],
  ])('says %s', (text, over, st) => expect(instanceStateText(inst('A', over, st))).toBe(text));
  it('falls back to scriptRunning for an older plugin without control', () => {
    expect(instanceStateText(inst('A', {}, { control: undefined, scriptRunning: true }))).toBe('Running');
  });
  it('says Not logged in when there is no status yet', () => expect(instanceStateText(inst('A', { status: null }))).toBe('Not logged in'));
  it('says Update needed on a version mismatch', () => expect(instanceStateText(inst('A', { connection: 'VersionMismatch' }))).toBe('Update needed'));
});

describe('the Skua notice kinds', () => {
  const n = (kind: string, character: string | null) => ({ id: '1', kind, character, title: 'server title', body: 'server body', atUtc: new Date(NOW - 120_000).toISOString() }) as never;
  it('render with catalog texts and the label', () => {
    expect(noticeLine(n('SkuaDisconnected', 'Hero'), NOW)).toMatchObject({ title: 'Skua disconnected', body: 'Hero: Skua closed or the link was lost.' });
    expect(noticeLine(n('SkuaReconnected', 'Hero'), NOW)).toMatchObject({ title: 'Skua is back', body: 'Hero: Skua is connected again.' });
    expect(noticeLine(n('SkuaOptionsWaiting', 'Hero'), NOW)).toMatchObject({ title: 'Waiting for you on the PC', body: "Hero: close the script's options window on the computer." });
  });
  it('keep the server text for older kinds and when no label is known', () => {
    expect(noticeLine(n('RankUp', 'Hero'), NOW).title).toBe('server title');
    expect(noticeLine(n('SkuaDisconnected', null), NOW).title).toBe('server title');
  });
});
