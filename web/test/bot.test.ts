import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { filterOf, formatDuration, LOG_FILTERS, parseLogFilter, questRows, skuaEmptyText, skuaLogRows, skuaNow, type LogFilter } from '../src/bot';
import { botSection } from '../src/botViews';
import { parseSkua } from '../src/skua';
import { plural } from '../src/i18n';
import type { SkuaInstance, SkuaState } from '../src/types';
import type { VNode } from '../src/views';

// Moved from skua.test.ts with the helpers (bot.ts) and the cards (botViews.ts); the Skua tab's views became the Bot section.
const NOW = Date.parse('2026-11-11T12:12:00Z');
const wire = {
  connection: 'Connected',
  hello: { pluginVersion: '0.1.0', skuaVersion: '1.4.5.0' },
  status: { loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: true, runningSince: '2026-11-11T12:00:00+00:00', quests: null, factions: null },
  log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: 'Sold Blood Orb x50 for 2,500 gold' }],
};
const skua = (over: Partial<SkuaState> = {}): SkuaState => ({ ...parseSkua(wire)!, ...over });
const withStatus = (over: object, s: Partial<SkuaState> = {}) => skua({ ...s, status: { ...wire.status, ...over } });
const asInstance = (s: SkuaState): SkuaInstance => ({ ...s, instanceId: 'a', label: 'Hero', disconnected: false });
const bot = (s: SkuaState, skuaFilter: LogFilter = 'all') => botSection(asInstance(s), { nowMs: NOW, skuaFilter });
const quest = (id: number, name: string, ready: boolean) => ({ id, name, ready });
const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];
const texts = (n: VNode) => all(n).map((x) => x.text).filter((t): t is string => t !== undefined);

describe('formatDuration', () => {
  it.each([[0, 'just now'], [59_000, 'just now'], [180_000, '3 min'], [3_900_000, '1 h 5 min'], [7_200_000, '2 h'], [90_000_000, '1 d 1 h'], [Number.NaN, 'just now'], [-5000, 'just now'], [Infinity, 'just now']])('%s -> %s', (ms, text) =>
    expect(formatDuration(ms)).toBe(text));
});

describe('bot view model', () => {
  it('has the Now lines for a running script', () => {
    expect(skuaNow(skua(), NOW)).toEqual({
      loggedIn: true, character: 'Hero', where: 'Artix · battleon', script: { running: true, text: 'Running for 12 min', name: 'Farm.cs' },
    });
  });
  it('says no script running, and is null when not connected or without a status', () => {
    expect(skuaNow(withStatus({ scriptRunning: false }), NOW)!.script).toEqual({ running: false, text: 'No script running', name: null });
    expect(skuaNow(skua({ connection: 'NotConnected' }), NOW)).toBeNull();
    expect(skuaNow(skua({ status: null }), NOW)).toBeNull();
  });
  it('leaves missing parts out of Server · Map and falls back when the start time is unknown', () => {
    expect(skuaNow(withStatus({ server: null }), NOW)!.where).toBe('battleon');
    expect(skuaNow(withStatus({ server: null, map: '' }), NOW)!.where).toBeNull();
    expect(skuaNow(withStatus({ runningSince: null }), NOW)!.script.text).toBe('Running Farm.cs');
    expect(skuaNow(withStatus({ runningSince: null, script: null }), NOW)!.script.text).toBe('Running a script');
  });
  it('shows no character or place while not logged in', () => {
    expect(skuaNow(withStatus({ loggedIn: false }), NOW)).toMatchObject({ loggedIn: false, character: null, where: null });
  });
  it('keeps log order and words the time like a notice', () => {
    const s = skua({ log: [{ at: '2026-11-11T12:10:00Z', kind: 'a', text: 'new' }, { at: '2026-11-11T12:00:00Z', kind: 'a', text: 'old' }] });
    expect(skuaLogRows(s, NOW)).toEqual([{ when: '2 min ago', text: 'new' }, { when: '12 min ago', text: 'old' }]);
  });
  it('has the calm texts', () => {
    expect(skuaEmptyText(null)).toBe('Skua is not connected.');
    expect(skuaEmptyText(skua({ connection: 'NotConnected' }))).toBe('Skua is not connected.');
    expect(skuaEmptyText(skua({ log: [] }))).toBe('Nothing to show yet.');
    expect(skuaEmptyText(skua({ connection: 'VersionMismatch' }))).toBe('Update the Skua plugin and SkuaLink to the same release.');
    expect(skuaEmptyText(skua())).toBe('');
  });
});

describe('log filters', () => {
  it.each([
    ['Bought', 'shop'], ['Sold', 'shop'], ['Dropped', 'drops'], ['MapChanged', 'maps'], ['ScriptStarted', 'script'], ['ScriptStopped', 'script'],
    ['ScriptError', 'script'], ['Died', 'session'], ['LoggedOut', 'session'], ['Relogin', 'session'],
    ['QuestAccepted', null], ['QuestTurnedIn', null], ['Mystery', null], ['', null], ['constructor', null], ['__proto__', null],
  ])('%s -> %s', (kind, filter) => expect(filterOf(kind)).toBe(filter));

  it('places every log kind SkuaLink knows (the shared C# fixture), so a new kind cannot fall under Everything unnoticed', () => {
    const kinds = JSON.parse(readFileSync(new URL('../../tests/AqwCompanion.Tests/Skua/fixtures/log-kinds.json', import.meta.url), 'utf8')) as string[];
    const onlyUnderEverything = ['QuestAccepted', 'QuestTurnedIn'];
    expect(kinds.length).toBeGreaterThan(0);
    for (const kind of kinds) {
      if (onlyUnderEverything.includes(kind)) expect(filterOf(kind), kind).toBeNull();
      else expect(filterOf(kind), kind).not.toBeNull();
    }
  });

  it('lists the filters in order, parsing anything else as Everything', () => {
    expect(LOG_FILTERS).toEqual(['all', 'shop', 'drops', 'maps', 'script', 'session']);
    for (const f of LOG_FILTERS) expect(parseLogFilter(f)).toBe(f);
    for (const bad of [null, '', 'x', 'ALL', 'constructor']) expect(parseLogFilter(bad)).toBe('all');
  });

  const kinds = ['Sold', 'Bought', 'Dropped', 'MapChanged', 'ScriptStarted', 'Died', 'QuestAccepted', 'QuestTurnedIn', 'Mystery'];
  const log = kinds.map((kind, i) => ({ at: '2026-11-11T12:00:00Z', kind, text: `${kind} ${i}` }));
  const rows = (f: LogFilter) => skuaLogRows(skua({ log }), NOW, f).map((r) => r.text);

  it('shows everything, including quest and unknown kinds, only under Everything', () => {
    expect(rows('all')).toHaveLength(9);
    expect(skuaLogRows(skua({ log }), NOW)).toHaveLength(9);
    expect(rows('shop')).toEqual(['Sold 0', 'Bought 1']);
    expect(rows('drops')).toEqual(['Dropped 2']);
    expect(rows('maps')).toEqual(['MapChanged 3']);
    expect(rows('script')).toEqual(['ScriptStarted 4']);
    expect(rows('session')).toEqual(['Died 5']);
    const named = new Set(LOG_FILTERS.filter((f) => f !== 'all').flatMap(rows));
    expect([...named].some((x) => /Quest|Mystery/.test(x))).toBe(false);
  });
  it('says Nothing here yet. for an empty filter, Nothing to show yet. for an empty log', () => {
    const only = skua({ log: [log[0]!] });
    expect(skuaEmptyText(only, 'drops')).toBe('Nothing here yet.');
    expect(skuaEmptyText(only, 'shop')).toBe('');
    expect(skuaEmptyText(only)).toBe('');
    expect(skuaEmptyText(skua({ log: [] }), 'drops')).toBe('Nothing to show yet.');
  });
});

describe('questRows', () => {
  const list = [quest(1, 'A', false), quest(2, 'B', true), quest(3, 'C', false), quest(4, 'D', true)];
  it('puts ready quests first and keeps the game order otherwise', () => {
    expect(questRows(withStatus({ quests: list }))!.map((q) => q.name)).toEqual(['B', 'D', 'A', 'C']);
  });
  it('is null when not reported or not connected, and empty when none are active', () => {
    expect(questRows(withStatus({ quests: null }))).toBeNull();
    expect(questRows(skua({ status: null }))).toBeNull();
    expect(questRows(withStatus({ quests: list }, { connection: 'VersionMismatch' }))).toBeNull();
    expect(questRows(withStatus({ quests: [] }))).toEqual([]);
  });
});

describe('the Bot section', () => {
  const sections = (v: VNode) => all(v).filter((n) => n.tag === 'section');
  const headings = (v: VNode) => all(v).filter((n) => n.tag === 'h2').map((n) => n.text);
  const buttons = (v: VNode) => all(v).filter((n) => n.tag === 'button');

  it('shows the Now, Active quests and Log cards in that order, with no back link of its own', () => {
    const view = bot(withStatus({ quests: [quest(1, 'Slay the Beast', true), quest(2, 'Gather Bones', false)] }));
    expect(headings(view)).toEqual(['Now', 'Active quests · 2', 'Log']);
    expect(view.children[0]!.className).toBe('card now-card');
    expect(all(view).some((n) => n.className === 'back')).toBe(false);
    expect(texts(view)).toEqual(expect.arrayContaining(['Hero', 'Artix · battleon', 'Running for 12 min', 'Farm.cs', 'Sold Blood Orb x50 for 2,500 gold', '11 min ago']));
  });
  it('puts the Control card after Now when one is passed in', () => {
    const view = botSection(asInstance(skua()), { nowMs: NOW, skuaFilter: 'all', controlCard: () => ({ tag: 'section', className: 'card', children: [{ tag: 'h2', text: 'Control', children: [] }] }) });
    expect(headings(view)).toEqual(['Now', 'Control', 'Log']);
  });
  it('writes the quest count with plural() and lists the quests, ready first, with a shape and words', () => {
    expect(plural('skua.quests.title', 1)).toBe('Active quests · 1');
    expect(plural('skua.quests.title', 3)).toBe('Active quests · 3');
    expect(plural('skua.quests.title', 30)).toBe('Active quests · 30');
    const view = bot(withStatus({ quests: [quest(1, 'Gather Bones', false), quest(2, 'Slay the Beast', true)] }));
    const card = sections(view)[1]!;
    const items = all(card).filter((n) => n.tag === 'li');
    expect(items.map((li) => texts(li)[0])).toEqual(['Slay the Beast', 'Gather Bones']);
    expect(texts(items[0]!)).toContain('Ready to turn in');
    expect(all(items[0]!).some((n) => n.attrs?.['data-kind'] === 'ready' && n.attrs['aria-hidden'] === 'true')).toBe(true);
    expect(texts(items[1]!)).not.toContain('Ready to turn in');
    expect(all(card).some((n) => n.tag === 'ul')).toBe(true);
  });
  it('says No active quests. for an empty list and shows no quests card when not reported', () => {
    const none = bot(withStatus({ quests: [] }));
    expect(headings(none)).toEqual(['Now', 'Active quests · 0', 'Log']);
    expect(texts(none)).toContain('No active quests.');
    expect(headings(bot(withStatus({ quests: null })))).toEqual(['Now', 'Log']);
  });
  it('has no Reputation card (the Overview has the one Reputation card)', () => {
    expect(headings(bot(withStatus({ factions: [{ id: 1, name: 'N', rank: 1, totalRep: 1, rep: 1, requiredRep: 2 }] })))).not.toContain('Reputation');
  });
  it('builds the filter row: a labelled group of pressed-state buttons, Everything pressed by default', () => {
    const view = bot(skua());
    const group = all(view).find((n) => n.attrs?.role === 'group')!;
    expect(group.attrs!['aria-label']).toBe('Filter the log');
    expect(group.className).toBe('filters');
    const logCard = sections(view)[1]!;
    expect(logCard.children[0]!.text).toBe('Log');
    expect(logCard.children[1]).toBe(group);
    const bs = buttons(group);
    expect(bs.map((b) => b.text)).toEqual(['Everything', 'Shop', 'Drops', 'Maps', 'Script', 'Session']);
    expect(bs.map((b) => b.attrs!['data-value'])).toEqual([...LOG_FILTERS]);
    for (const b of bs) {
      expect(b.attrs!['data-action']).toBe('skua-filter');
      expect(b.attrs!.type).toBe('button');
      expect(b.className).toBe('filter-btn');
    }
    expect(bs.map((b) => b.attrs!['aria-pressed'])).toEqual(['true', 'false', 'false', 'false', 'false', 'false']);
  });
  it('presses the chosen filter, narrows only the log and never hides the other cards', () => {
    const s = withStatus({ quests: [quest(1, 'Q', true)] }, { log: [{ at: '2026-11-11T12:00:00Z', kind: 'Sold', text: 'sold it' }, { at: '2026-11-11T12:00:00Z', kind: 'Died', text: 'died' }] });
    const view = bot(s, 'session');
    expect(buttons(view).map((b) => b.attrs!['aria-pressed'])).toEqual(['false', 'false', 'false', 'false', 'false', 'true']);
    expect(texts(view)).toContain('died');
    expect(texts(view)).not.toContain('sold it');
    expect(headings(view)).toEqual(['Now', 'Active quests · 1', 'Log']);
    expect(texts(bot(s, 'drops'))).toContain('Nothing here yet.');
    expect(headings(bot(s, 'drops'))).toEqual(['Now', 'Active quests · 1', 'Log']);
  });
  it('gives all three cards the card class', () => {
    const cards = sections(bot(withStatus({ quests: [] })));
    expect(cards).toHaveLength(3);
    for (const c of cards) expect(c.className?.split(' ')).toContain('card');
  });
  it('is stable across redraws: the same data gives the same tree', () => {
    expect(JSON.stringify(bot(skua()))).toBe(JSON.stringify(bot(skua())));
  });
  it('says so in the Now card when connected without a character', () => {
    const view = bot(withStatus({ loggedIn: false, character: null, server: null, map: null, scriptRunning: false, script: null, runningSince: null, quests: [] }));
    expect(texts(view)).toEqual(expect.arrayContaining(['Not logged in to a character.', 'No script running']));
    expect(texts(view)).not.toContain('Hero');
  });
  it('shows no Now card and no quests card when connected without a status', () => {
    expect(headings(bot(skua({ status: null })))).toEqual(['Log']);
  });
  it('shows no empty time line for an unreadable time', () => {
    const view = bot(skua({ log: [{ at: 'not a date', kind: 'x', text: 'odd' }] }));
    expect(texts(view)).toContain('odd');
    expect(all(view).some((n) => n.className === 'muted')).toBe(false);
  });
  it('shows Nothing to show yet. for a connected empty log', () => {
    expect(texts(bot(skua({ log: [] })))).toContain('Nothing to show yet.');
  });
  it('uses no style or on* attributes and writes server text as text', () => {
    const s = skua({ log: [{ at: '2026-11-11T12:00:00Z', kind: 'x', text: '<b>hi</b>' }] });
    for (const n of all(bot(s))) for (const name of Object.keys(n.attrs ?? {})) expect(name === 'style' || name.startsWith('on')).toBe(false);
    expect(texts(bot(s))).toContain('<b>hi</b>');
    const hostile = withStatus({ character: '<img onerror=x>', quests: [quest(1, '<script>', true)] });
    expect(texts(bot(hostile))).toEqual(expect.arrayContaining(['<img onerror=x>', '<script>']));
  });
});
