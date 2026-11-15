import { describe, expect, it } from 'vitest';
import { en } from '../src/i18n/en';
import { characterView, h, listView, pairNeededView, type Model, type VNode } from '../src/views';
import type { PhoneCharacter, RemoteState, SkuaInstance } from '../src/types';

const NOW = Date.parse('2026-11-15T12:00:00Z');
const iso = (minutesAgo: number) => new Date(NOW - minutesAgo * 60_000).toISOString();
const profile = { level: 60, xp: 1609986, xpToLevel: 5590000, gold: 93650135, coins: 777, classRank: 10, isMember: true, className: 'Void Highlord', classId: 31, classPoints: 302500, guild: 'Heroes', bagSlots: 140, bankSlots: 300, houseSlots: 50 };
const hero = (over: Partial<PhoneCharacter> = {}): PhoneCharacter => ({
  name: 'Hero <b>x</b>', hasCharacter: true, instanceId: 'a', live: true, lastSeenUtc: iso(0), profile, factions: [], factionsLive: true,
  hardFarmsState: 'Ready', hardFarms: { owned: 1, total: 2, missing: ['X'] }, inventoryAtUtc: iso(3), inventoryTruncated: false, ...over,
});
const instance: SkuaInstance = {
  instanceId: 'a', label: 'Hero <b>x</b>', disconnected: false, connection: 'Connected', hello: null, log: [],
  status: { loggedIn: true, character: 'Hero <b>x</b>', server: null, map: null, script: null, scriptRunning: true, runningSince: null, quests: [], factions: [], control: 'running' },
};
const notice = { id: 'n1', kind: 'RankUp' as const, character: 'Hero <b>x</b>', title: 'Rank up', body: 'Yokai reached rank 8.', atUtc: iso(2) };
const stateWith = (characters: PhoneCharacter[], notices = [notice]): RemoteState => ({ snapshot: null, notices, characters, skuaInstances: [instance] });

const model = (s: RemoteState | null, over: Partial<Model> = {}): Model => ({ state: s, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, ...over });

function all(node: VNode): VNode[] {
  return [node, ...node.children.flatMap(all)];
}
const texts = (node: VNode) => all(node).map((n) => n.text).filter((t): t is string => t !== undefined);
const byClass = (node: VNode, cls: string) => all(node).filter((n) => n.className === cls);

describe('list view', () => {
  it('draws one row per character with its badge, never as HTML', () => {
    const v = listView(model(stateWith([hero()])));
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
    expect(texts(listView(model(stateWith([hero()], []))))).toContain('Nothing has happened yet.');
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
