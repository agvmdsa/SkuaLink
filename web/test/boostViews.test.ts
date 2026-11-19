import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { rowText, parseBoosts } from '../src/boosts';
import { botSection } from '../src/botViews';
import { t } from '../src/i18n';
import { queueEn } from '../src/i18n/queueEn';
import { QueueUi } from '../src/queue';
import { boostCard, startQueue } from '../src/queueViews';
import { parseSkuaInstances } from '../src/skua';
import type { SkuaInstance } from '../src/types';
import type { VNode } from '../src/views';

const all = (n: VNode | null): VNode[] => (n ? [n, ...n.children.flatMap(all)] : []);
const texts = (n: VNode | null) => all(n).map((x) => x.text).filter((x): x is string => x !== undefined);
const settle = async (): Promise<void> => {
  for (let i = 0; i < 30; i++) await Promise.resolve();
};
type Click = (event: unknown) => void;
const fakeApp = () => {
  const app = { handler: null as Click | null, listeners: 0, addEventListener: (_type: string, fn: Click) => { app.listeners++; app.handler = fn; } };
  return app;
};
const tapItem = (action: string, item: number | null = null) => ({
  target: { closest: () => ({ getAttribute: (n: string) => (n === 'data-action' ? action : item === null ? null : String(item)) }) },
});
const noop = () => undefined;
startQueue(fakeApp() as unknown as HTMLElement, { instance: () => null, queues: () => [], redraw: noop, onUnpaired: noop }); // registers the catalog

const READ_AT = 1_763_481_234_567;
const inst = (over: Record<string, unknown> = {}): SkuaInstance =>
  parseSkuaInstances([{
    instanceId: 'i-1', label: 'Hero', connection: 'Connected', disconnected: false, hello: null, log: [],
    status: { loggedIn: true, character: 'hero', server: null, map: null, script: null, scriptRunning: false, runningSince: null, quests: null, factions: null, control: 'idle' }, ...over,
  }])![0]!;
const item = (over: Record<string, unknown> = {}) => ({ itemId: 17439, name: 'GOLD Boost! (1 hr)', quantity: 33, canUse: true, ...over });
const entry = (over: Record<string, unknown> = {}) => ({ instanceId: 'i-1', online: true, readAt: READ_AT, gold: 840, class: -1, rep: 0, xp: 0, items: [item()], canRelogin: false, ...over });
const okUi = () => new QueueUi({ command: vi.fn() }, noop, noop);

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('boostCard', () => {
  const ui = okUi();
  it('is null when not live, when the list has no entry and for damaged input', () => {
    expect(boostCard(inst({ connection: 'NotConnected' }), ui, [entry()], READ_AT)).toBeNull();
    expect(boostCard(inst({ disconnected: true }), ui, [entry()], READ_AT)).toBeNull();
    expect(boostCard(inst(), ui, [entry({ instanceId: 'other' })], READ_AT)).toBeNull();
    for (const v of [undefined, 'x', {}, null, [null], [{}]]) expect(boostCard(inst(), ui, v, READ_AT)).toBeNull();
  });
  it('is a card with the heading and the four rows', () => {
    const v = boostCard(inst(), ui, [entry({ gold: 840, class: -1 })], READ_AT)!;
    expect(v.tag).toBe('section');
    expect(v.className).toBe('card queue-card');
    expect(all(v).find((n) => n.tag === 'h2')!.text).toBe('Boosts');
    const rows = v.children.filter((n) => n.tag === 'p').map((n) => n.text);
    expect(rows).toEqual(['Gold: 14 min left', 'Class: active', 'Reputation: inactive', 'XP: inactive']);
  });
  it('lists an item with Use, disabled exactly when canUse is false', () => {
    const v = boostCard(inst(), ui, [entry({ items: [item(), item({ itemId: 17437, name: 'CLASS Boost! (1 hr)', canUse: false })] })], READ_AT)!;
    const rows = all(v).filter((n) => n.className === 'queue-row');
    expect(rows).toHaveLength(2);
    expect(texts(rows[0]!)).toContain('GOLD Boost! (1 hr) x33');
    const b = rows.map((r) => all(r).find((n) => n.tag === 'button')!);
    expect(b[0]!.text).toBe('Use');
    expect(b[0]!.attrs).toMatchObject({ 'data-action': 'boost-use', 'data-item': '17439' });
    expect('disabled' in b[0]!.attrs!).toBe(false);
    expect('disabled' in b[1]!.attrs!).toBe(true);
  });
  it('shows hostile names only as text', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    const long = 'x'.repeat(5000) + '\ud800';
    const v = boostCard(inst(), ui, [entry({ items: [item({ name: hostile }), item({ itemId: 2, name: long })] })], READ_AT)!;
    expect(texts(v)).toContain(`${hostile} x33`);
    for (const n of all(v)) for (const val of Object.values(n.attrs ?? {})) expect(val).not.toContain('<img');
    expect(JSON.stringify(v.attrs ?? {})).not.toContain('<img');
  });
  it('shows Log in again only when canRelogin', () => {
    const has = (v: VNode) => all(v).some((n) => n.attrs?.['data-action'] === 'boost-relogin');
    expect(has(boostCard(inst(), ui, [entry({ canRelogin: true })], READ_AT)!)).toBe(true);
    expect(has(boostCard(inst(), ui, [entry()], READ_AT)!)).toBe(false);
  });
  it('shows the working line while pending and the error line after setError, otherwise neither', () => {
    const calm = boostCard(inst(), ui, [entry()], READ_AT)!;
    expect(all(calm).some((n) => n.attrs?.role)).toBe(false);
    const u = okUi();
    u.setError('i-1', 'x');
    const err = boostCard(inst(), u, [entry()], READ_AT)!;
    expect(all(err).find((n) => n.attrs?.role === 'alert')!.text).toBe('x');
    const slow = new QueueUi({ command: vi.fn() }, noop, noop);
    void slow.call('i-1', () => new Promise(noop));
    const busy = boostCard(inst(), slow, [entry()], READ_AT)!;
    expect(all(busy).find((n) => n.attrs?.role === 'status')!.text).toBe('Working...');
  });
  it('turns a unix-ms readAt into minutes (no int32 cap)', () => {
    const b = parseBoosts([entry({ gold: 600 })])[0]!;
    expect(rowText(b, 'gold', READ_AT + 60_000)).toBe('Gold: 9 min left');
  });
});

describe('tapping', () => {
  const stub = () => {
    const spy = vi.fn(async (_p: string, init?: RequestInit) => ({ status: 200, json: async () => ({ ok: true }), init }) as unknown as Response);
    vi.stubGlobal('fetch', spy);
    return spy;
  };
  const wire = (boosts: () => unknown, confirm?: (w: { title: string; body: string; confirm: string }, go: () => void) => void, instance: SkuaInstance | null = inst()) => {
    const app = fakeApp();
    startQueue(app as unknown as HTMLElement, { instance: () => instance, queues: () => [], boosts, redraw: noop, onUnpaired: noop, confirm });
    return app;
  };

  it('relogin sends once and two quick taps send one', async () => {
    const spy = stub();
    const app = wire(() => [entry({ canRelogin: true })]);
    app.handler!(tapItem('boost-relogin'));
    app.handler!(tapItem('boost-relogin'));
    await settle();
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0]![0]).toBe('/api/skua/i-1/relogin');
  });
  it('use opens the confirmation, sends nothing until go and then posts the id once', async () => {
    const spy = stub();
    let words: unknown;
    let go: () => void = noop;
    const app = wire(() => [entry()], (w, g) => { words = w; go = g; });
    app.handler!(tapItem('boost-use', 17439));
    await settle();
    expect(words).toEqual({ title: 'Use GOLD Boost! (1 hr)? You have 33.', body: '', confirm: 'Use' });
    expect(spy).not.toHaveBeenCalled();
    go();
    await settle();
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0]![0]).toBe('/api/skua/i-1/boosts/use');
    expect(spy.mock.calls[0]![1]!.body).toBe('{"itemId":17439}');
  });
  it('opens nothing for an unknown id, null, junk and a stale tap on an item that cannot be used', async () => {
    const spy = stub();
    const confirm = vi.fn();
    const app = wire(() => [entry({ items: [item(), item({ itemId: 5, canUse: false })] })], confirm);
    for (const id of ['999', null, 'abc', '1e3', '17439.5', '5']) {
      app.handler!({ target: { closest: () => ({ getAttribute: (n: string) => (n === 'data-action' ? 'boost-use' : id) }) } });
    }
    await settle();
    expect(confirm).not.toHaveBeenCalled();
    expect(spy).not.toHaveBeenCalled();
  });
  it('sends nothing when the boost is gone or no longer usable at Confirm', async () => {
    const spy = stub();
    let current: unknown = [entry()];
    let go: () => void = noop;
    const app = wire(() => current, (_w, g) => { go = g; });
    app.handler!(tapItem('boost-use', 17439));
    current = [entry({ items: [item({ canUse: false })] })];
    go();
    await settle();
    expect(spy).not.toHaveBeenCalled();
  });
  it('sends the id captured when the sheet opened', async () => {
    const spy = stub();
    let go: () => void = noop;
    const app = wire(() => [entry({ items: [item(), item({ itemId: 17437, name: 'CLASS Boost! (1 hr)' })] })], (_w, g) => { go = g; });
    app.handler!(tapItem('boost-use', 17437));
    go();
    await settle();
    expect(spy.mock.calls[0]![1]!.body).toBe('{"itemId":17437}');
  });
  it('does nothing without an instance, still serves the queue actions and registers one listener', async () => {
    const spy = stub();
    const none = wire(() => [entry()], undefined, null);
    none.handler!(tapItem('boost-relogin'));
    await settle();
    expect(spy).not.toHaveBeenCalled();
    const app = wire(() => [entry()]);
    expect(app.listeners).toBe(1);
    app.handler!(tapItem('queue-start'));
    await settle();
    expect(spy.mock.calls[0]![0]).toContain('/api/skua/i-1/');
  });
  it('a failed fetch never leaves the card pending', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('offline'); }));
    const app = wire(() => [entry({ canRelogin: true })]);
    app.handler!(tapItem('boost-relogin'));
    await settle();
    app.handler!(tapItem('boost-relogin'));
    await settle();
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});

describe('the tick', () => {
  const load = async (hidden = false) => {
    vi.resetModules();
    vi.useFakeTimers();
    vi.stubGlobal('document', { hidden });
    const m = await import('../src/boostViews');
    const i18n = await import('../src/i18n');
    i18n.addMessages((await import('../src/i18n/queueEn')).queueEn);
    const queue = await import('../src/queue');
    return { ...m, ui: new queue.QueueUi({ command: vi.fn() }, noop, noop) };
  };
  it('redraws each second while counting and stops when the card is no longer drawn', async () => {
    const { boostCard: draw, startBoostTick, ui } = await load();
    const redraw = vi.fn();
    draw(inst(), ui, [entry()], READ_AT);
    startBoostTick(redraw);
    startBoostTick(redraw);
    vi.advanceTimersByTime(2000);
    expect(redraw).toHaveBeenCalledTimes(2);
    expect(vi.getTimerCount()).toBe(1);
    vi.advanceTimersByTime(5000); // nobody drew the card: the timer ends itself
    expect(vi.getTimerCount()).toBe(0);
  });
  it('keeps going while the card keeps being drawn', async () => {
    const { boostCard: draw, startBoostTick, ui } = await load();
    const redraw = vi.fn(() => void draw(inst(), ui, [entry()], READ_AT));
    draw(inst(), ui, [entry()], READ_AT);
    startBoostTick(redraw);
    vi.advanceTimersByTime(10_000);
    expect(redraw).toHaveBeenCalledTimes(10);
  });
  it('does not start when nothing counts, when offline or for an unknown time', async () => {
    for (const over of [{ gold: 0, class: 0 }, { online: false }, { gold: -1, class: -1 }, { readAt: 0 }, { readAt: -1 }]) {
      const { boostCard: draw, startBoostTick, ui } = await load();
      const redraw = vi.fn();
      draw(inst(), ui, [entry(over)], READ_AT);
      startBoostTick(redraw);
      vi.advanceTimersByTime(3000);
      expect(redraw).not.toHaveBeenCalled();
      expect(vi.getTimerCount()).toBe(0);
    }
  });
  it('does not redraw while the page is hidden', async () => {
    const { boostCard: draw, startBoostTick, ui } = await load(true);
    const redraw = vi.fn();
    draw(inst(), ui, [entry()], READ_AT);
    startBoostTick(redraw);
    vi.advanceTimersByTime(2000);
    expect(redraw).not.toHaveBeenCalled();
  });
});

describe('catalog, guard and section', () => {
  it('every boost.* key is in queueEn', () => {
    const src = readFileSync(new URL('../src/boostViews.ts', import.meta.url), 'utf8') + readFileSync(new URL('../src/boosts.ts', import.meta.url), 'utf8');
    for (const m of src.matchAll(/'(boost\.[a-zA-Z]+)'/g)) expect(Object.keys(queueEn)).toContain(m[1]);
    expect(t('boost.title')).toBe('Boosts');
  });
  it('botSection shows the boosts card after the queue card and before the quests; absent hook shows nothing', () => {
    const card = (name: string) => (): VNode => ({ tag: 'section', className: 'card', children: [{ tag: 'h2', text: name, children: [] }] });
    const heads = (v: VNode) => all(v).filter((n) => n.tag === 'h2').map((n) => n.text);
    const i = inst();
    const v = botSection(i, { nowMs: 0, skuaFilter: 'all', queueCard: card('QUEUE'), boostCard: card('BOOSTS') });
    const h = heads(v);
    expect(h.indexOf('BOOSTS')).toBe(h.indexOf('QUEUE') + 1);
    const q = h.findIndex((x) => /quest/i.test(x ?? ''));
    if (q >= 0) expect(h.indexOf('BOOSTS')).toBeLessThan(q);
    expect(heads(botSection(i, { nowMs: 0, skuaFilter: 'all', queueCard: card('QUEUE') }))).not.toContain('BOOSTS');
  });
});
