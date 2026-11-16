import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { en } from '../src/i18n/en';
import { characterView, fallbackNav, h, listView, pairNeededView, type Model, type VNode } from '../src/views';
import type { PhoneCharacter, RemoteState, SkuaInstance } from '../src/types';

const NOW = Date.parse('2026-11-15T12:00:00Z');
const iso = (minutesAgo: number) => new Date(NOW - minutesAgo * 60_000).toISOString();
const profile = { level: 60, xp: 1609986, xpToLevel: 5590000, gold: 93650135, coins: 777, classRank: 10, isMember: true, className: 'Void Highlord', classId: 31, classPoints: 302500, guild: 'Heroes', bagSlots: 140, bankSlots: 300, houseSlots: 50 };
const hero = (over: Partial<PhoneCharacter> = {}): PhoneCharacter => ({
  name: 'Hero <b>x</b>', hasCharacter: true, instanceId: 'a', live: true, lastSeenUtc: iso(0), profile, factions: [], factionsLive: true,
  hardFarmsState: 'Ready', hardFarms: { owned: 1, total: 2, missing: ['X'] }, inventoryAtUtc: iso(3), inventoryTruncated: false, ...over,
});
const instance = (over: Partial<SkuaInstance> = {}): SkuaInstance => ({
  instanceId: 'a', label: 'Hero <b>x</b>', disconnected: false, connection: 'Connected', hello: null, log: [],
  status: { loggedIn: true, character: 'Hero <b>x</b>', server: null, map: null, script: null, scriptRunning: true, runningSince: null, quests: [], factions: [], control: 'running' }, ...over,
});
const notice = { id: 'n1', kind: 'RankUp' as const, character: 'Hero <b>x</b>', title: 'Rank up', body: 'Yokai reached rank 8.', atUtc: iso(2) };
const stateWith = (characters: PhoneCharacter[], instances: SkuaInstance[] = [instance()], notices = [notice]): RemoteState => ({ snapshot: null, notices, characters, skuaInstances: instances });

const model = (s: RemoteState | null, over: Partial<Model> = {}): Model => ({ state: s, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, ...over });

function all(node: VNode): VNode[] {
  return [node, ...node.children.flatMap(all)];
}
const texts = (node: VNode) => all(node).map((n) => n.text).filter((t): t is string => t !== undefined);
const byClass = (node: VNode, cls: string) => all(node).filter((n) => n.className === cls);

describe('list view', () => {
  it('draws one row per character with its badge, never as HTML', () => {
    const v = listView(model(stateWith([hero(), hero({ name: 'Alt', live: false, instanceId: null })])));
    expect(byClass(v, 'row')).toHaveLength(2);
    expect(texts(v)).toEqual(expect.arrayContaining(['Hero <b>x</b>', 'Running']));
    expect(byClass(v, 'row')[0]!.attrs!.href).toBe('#/character/Hero%20%3Cb%3Ex%3C%2Fb%3E');
  });
  it('draws a character-less Skua row as plain text, not a link', () => {
    const v = listView(model(stateWith([hero({ name: 'Skua 1', hasCharacter: false, profile: null })])));
    expect(all(v).filter((n) => n.tag === 'a' && n.className === 'row')).toHaveLength(0);
  });
  it('links each row to the section remembered for it', () => {
    const v = listView(model(stateWith([hero()]), { sectionOf: () => 'bot' }));
    expect(byClass(v, 'row')[0]!.attrs!.href).toBe('#/character/Hero%20%3Cb%3Ex%3C%2Fb%3E/bot');
  });
  it('says what to do when no character is known', () => {
    expect(texts(listView(model(null)))).toContain('No characters yet. Open Skua with the SkuaLink plugin and log in.');
  });
  it('lists recent notices with their age', () => {
    const t = texts(listView(model(stateWith([hero()]))));
    expect(t).toContain('Rank up');
    expect(t).toContain('Yokai reached rank 8.');
    expect(t.some((x) => x.endsWith('2 min ago'))).toBe(true);
  });
  it('says nothing has happened when there are no notices', () => {
    expect(texts(listView(model(stateWith([hero()], [instance()], []))))).toContain('Nothing has happened yet.');
  });
  it('ends with the install hint when there is one, and only then', () => {
    const hint = 'Install: tap Share, then Add to Home Screen.';
    const view = listView(model(stateWith([hero()]), { installHint: hint }));
    const last = view.children[view.children.length - 1]!;
    expect(last).toMatchObject({ className: 'note', text: hint });
    expect(texts(listView(model(stateWith([hero()]))))).not.toContain(hint);
    expect(texts(listView(model(null, { installHint: hint })))).toContain(hint);
  });
});

describe('character Overview', () => {
  it('the Overview shows numbers, reputation and hard farms of the character', () => {
    const v = characterView(model(stateWith([hero({ factions: [{ id: 2, name: 'Nation', rank: 3, totalRep: 5000, rep: 300, requiredRep: 1200 }] })])), 'Hero <b>x</b>');
    expect(texts(v)).toEqual(expect.arrayContaining(['Level', '60', 'Reputation', 'Nation', 'Hard Farms', '1 of 2 owned']));
  });
  it('shows the figures with grouped digits and an experience bar', () => {
    const v = characterView(model(stateWith([hero()])), 'Hero <b>x</b>');
    expect(texts(v)).toEqual(expect.arrayContaining(['93,650,135', '1,609,986 / 5,590,000', 'Live from Skua']));
    const bar = byClass(v, 'bar')[0]!;
    expect(bar.attrs!.role).toBe('progressbar');
    expect(bar.attrs!['aria-valuenow']).toBe('29');
  });
  it('says when a remembered character was last seen', () => {
    expect(texts(characterView(model(stateWith([hero({ live: false, lastSeenUtc: iso(90) })])), 'Hero <b>x</b>'))).toContain('Last seen 1 h ago');
  });
  it.each([
    ['NoInventory', 'Hard Farms: inventory not reported by this plugin version. Update the Skua plugin to the same release as SkuaLink.'],
    ['NoList', 'Hard Farms are not loaded yet. Import the list in SkuaLink.'],
  ] as const)('the Overview explains a missing Hard Farms result (%s)', (hardFarmsState, text) => {
    const v = characterView(model(stateWith([hero({ hardFarmsState, hardFarms: null })])), 'Hero <b>x</b>');
    expect(texts(v)).toContain(text);
  });
  it('the Overview says when the inventory list was cut', () => {
    const v = characterView(model(stateWith([hero({ inventoryTruncated: true })])), 'Hero <b>x</b>');
    expect(texts(v)).toContain('The inventory list was cut, so some items may show as missing.');
  });
  it('says how old the inventory is and how the bank counts', () => {
    const t = texts(characterView(model(stateWith([hero()])), 'Hero <b>x</b>'));
    expect(t).toContain('Inventory read 3 min ago');
    expect(t).toContain('Bank items count once the bank was opened in Skua during the session.');
  });
  it('a character without numbers asks to update the plugin, an unknown one says so', () => {
    expect(texts(characterView(model(stateWith([hero({ profile: null })])), 'Hero <b>x</b>'))).toContain(en['character.noProfile']);
    expect(texts(characterView(model(stateWith([])), 'Nobody'))).toContain(en['character.notKnown']);
  });
  it('keeps the missing list collapsed and offers the rest on request', () => {
    const missing = Array.from({ length: 30 }, (_, i) => `Item ${i}`);
    const s = stateWith([hero({ hardFarms: { owned: 1, total: 40, missing } })]);
    const view = characterView(model(s), 'Hero <b>x</b>');
    expect(byClass(view, 'missing')[0]!.children).toHaveLength(20);
    expect(all(view).some((n) => n.attrs?.['data-action'] === 'toggle-all' && n.text === 'Show 10 more')).toBe(true);
    const full = characterView(model(s, { showAll: true }), 'Hero <b>x</b>');
    expect(byClass(full, 'missing')[0]!.children).toHaveLength(30);
    expect(all(full).some((n) => n.attrs?.['data-action'] === 'toggle-all' && n.text === 'Show fewer')).toBe(true);
  });
  it('credits the AQW Wiki and its licence in a muted line of the Hard Farms card, whatever the result', () => {
    for (const c of [hero(), hero({ hardFarmsState: 'NoList', hardFarms: null })]) {
      const credit = all(characterView(model(stateWith([c])), 'Hero <b>x</b>')).find((n) => n.text === 'The Hard Farms list comes from the AQW Wiki (CC BY-SA 3.0).');
      expect(credit?.className).toBe('muted');
    }
  });
});

describe('character Overview edge cases', () => {
  it('writes no half sentence when a time does not parse', () => {
    const t = texts(characterView(model(stateWith([hero({ live: false, lastSeenUtc: 'not a time', inventoryAtUtc: 'never' })])), 'Hero <b>x</b>'));
    expect(t.some((x) => x.startsWith('Last seen'))).toBe(false);
    expect(t.some((x) => x.startsWith('Inventory read'))).toBe(false);
  });
  it('a Ready result whose numbers were damaged does not claim the inventory is missing', () => {
    const t = texts(characterView(model(stateWith([hero({ hardFarmsState: 'Ready', hardFarms: null })])), 'Hero <b>x</b>'));
    expect(t).not.toContain(en['hardFarms.noInventory']);
    expect(t).toContain('Hard Farms');
  });
  it('says Live from Skua once on a live Overview', () => {
    const t = texts(characterView(model(stateWith([hero({ factions: [{ id: 2, name: 'Nation', rank: 3, totalRep: 5000, rep: 300, requiredRep: 1200 }] })])), 'Hero <b>x</b>'));
    expect(t.filter((x) => x === 'Live from Skua')).toHaveLength(1);
  });
  it('opens a character, not a character-less Skua row of the same name', () => {
    const s = stateWith([hero({ name: 'Hero <b>x</b>', hasCharacter: false, profile: null }), hero()]);
    expect(texts(characterView(model(s), 'Hero <b>x</b>'))).toContain('Level');
  });
});

describe('character sections', () => {
  it('the character screen has an Overview and a Bot link, the current one marked', () => {
    const nav = all(characterView(model(stateWith([hero()])), 'Hero <b>x</b>', 'bot')).find((n) => n.className === 'segmented')!;
    expect(nav.tag).toBe('nav');
    expect(nav.attrs!['aria-label']).toBe('Character sections');
    const links = nav.children;
    expect(links.map((l) => [l.text, l.attrs!.href, l.attrs!['aria-current'] ?? null])).toEqual([
      ['Overview', '#/character/Hero%20%3Cb%3Ex%3C%2Fb%3E', null],
      ['Bot', '#/character/Hero%20%3Cb%3Ex%3C%2Fb%3E/bot', 'page'],
    ]);
  });
  it('the Overview is the default section', () => {
    const nav = all(characterView(model(stateWith([hero()])), 'Hero <b>x</b>')).find((n) => n.className === 'segmented')!;
    expect(nav.children.map((l) => l.attrs!['aria-current'] ?? null)).toEqual(['page', null]);
  });
  it('the Bot section of a live character shows the bot views once loaded, a loading card before', () => {
    const s = stateWith([hero()], [instance()]);
    expect(texts(characterView(model(s), 'Hero <b>x</b>', 'bot'))).toContain('Loading...');
    const drawn = characterView(model(s, { botSection: () => h('p', { text: 'BOT' }) }), 'Hero <b>x</b>', 'bot');
    expect(texts(drawn)).toContain('BOT');
    expect(texts(drawn)).not.toContain('Level');
  });
  it('the Bot section of a character without a live Skua is a calm card with the last state and no controls', () => {
    const s = stateWith([hero({ live: false, lastSeenUtc: iso(30) })], [instance({ disconnected: true, connection: 'NotConnected' })]);
    const v = characterView(model(s, { botSection: () => h('p', { text: 'BOT' }) }), 'Hero <b>x</b>', 'bot');
    expect(texts(v)).toEqual(expect.arrayContaining(['Skua is not connected']));
    expect(texts(v)).not.toContain('BOT');
    expect(all(v).some((n) => n.attrs?.['data-action']?.startsWith('control-'))).toBe(false);
  });
  it('the not-connected card says how old the data is and what the bot did last', () => {
    const st = { ...instance().status!, script: 'Farm <b>.cs</b>', map: 'battleon', scriptRunning: true };
    const s = stateWith([hero({ live: false, lastSeenUtc: iso(30) })], [instance({ disconnected: true, status: st })]);
    const t = texts(characterView(model(s), 'Hero <b>x</b>', 'bot'));
    expect(t).toContain('Last data 30 min ago. Open Skua with the SkuaLink plugin to see and control this bot.');
    expect(t).toContain('Last: Farm <b>.cs</b> · battleon');
    const idle = stateWith([hero({ live: false, lastSeenUtc: iso(30) })], [instance({ disconnected: true, status: { ...st, scriptRunning: false } })]);
    expect(texts(characterView(model(idle), 'Hero <b>x</b>', 'bot'))).toContain('Last: No script running · battleon');
    const unknown = stateWith([hero({ live: false, lastSeenUtc: 'odd', instanceId: null })], []);
    const u = texts(characterView(model(unknown), 'Hero <b>x</b>', 'bot'));
    expect(u).toContain('Skua is not connected');
    expect(u.some((x) => x.startsWith('Last data') || x.startsWith('Last:'))).toBe(false);
  });
  it('the list has no Skua card and the fallback navigation has only Characters', () => {
    expect(texts(listView(model(stateWith([hero()]))))).not.toContain('Skua');
    expect(all(listView(model(stateWith([hero()])))).some((n) => n.attrs?.href === '#/skua')).toBe(false);
    expect(fallbackNav({ view: 'list' }, false)!.children.map((c) => c.text)).toEqual(['Characters']);
  });
});

describe('section switch styles', () => {
  const css = readFileSync(new URL('../src/styles/app.css', import.meta.url), 'utf8');
  const tokens = readFileSync(new URL('../src/styles/tokens.css', import.meta.url), 'utf8');
  const rule = (sel: string) => css.match(new RegExp(`(?:^|\\n)${sel}\\s*\\{([^}]*)\\}`))?.[1] ?? '';
  const block = (t: string) => Object.fromEntries([...t.matchAll(/--([a-z-]+):\s*(#[0-9A-Fa-f]{6})\s*;/g)].map((m) => [m[1]!, m[2]!]));
  const body = (re: RegExp) => tokens.match(re)![1]!;
  const light = block(body(/^:root\s*\{([^}]*)\}/m));
  const sysDark = { ...light, ...block(body(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme='light'\]\)\s*\{([^}]*)\}/)) };
  const forcedDark = { ...light, ...block(body(/^:root\[data-theme='dark'\]\s*\{([^}]*)\}/m)) };
  const lum = (hex: string) => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)) as [number, number, number];
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const ratio = (a: string, b: string) => {
    const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x) as [number, number];
    return (hi + 0.05) / (lo + 0.05);
  };
  const decl = (sel: string, prop: string) => rule(sel).match(new RegExp(`(?:^|[;\\s])${prop}:\\s*var\\(--([a-z-]+)\\)`))?.[1];
  const pairs: [string, string, string, string, number][] = [
    ['section link', '\\.seg', 'color', 'background', 4.5],
    ['current section', '\\.seg\\[aria-current="page"\\]', 'color', 'background', 7],
  ];
  describe.each([['light', light], ['system dark', sysDark], ['forced dark', forcedDark]] as const)('%s', (_n, t) => {
    it.each(pairs)('%s reads at its ratio', (_label, sel, fg, bg, min) => {
      const fgToken = decl(sel, fg)!;
      const bgToken = decl(sel, bg)!;
      expect(fgToken, `${sel} ${fg}`).toBeTruthy();
      expect(bgToken, `${sel} ${bg}`).toBeTruthy();
      expect(ratio(t[fgToken]!, t[bgToken]!)).toBeGreaterThanOrEqual(min);
    });
  });
  it('keeps the section links 3rem tall and marks the current one by more than colour', () => {
    expect(rule('\\.seg')).toMatch(/min-height:\s*3rem/);
    expect(rule('\\.seg\\[aria-current="page"\\]')).toMatch(/border-width:\s*3px/);
    expect(rule('\\.segmented')).toMatch(/display:\s*flex/);
  });
  it('has no rule of the removed instance selector', () => {
    expect(css).not.toMatch(/\.chip|\.inst-/);
  });
});

describe('pairing view', () => {
  it('tells the player how to pair', () => {
    expect(texts(pairNeededView())).toContain('On your PC, open SkuaLink, then Settings, "Watch my characters from my phone", "Pair a phone...", and scan the code or open the link here.');
  });
});

describe('h', () => {
  it('drops falsy children', () => {
    expect(h('div', {}, null, false, h('p')).children).toHaveLength(1);
  });
});
