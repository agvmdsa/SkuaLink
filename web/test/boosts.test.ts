import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CallResult } from '../src/api';
import { boostApi, boostFor, parseBoosts, rowText, safeInt, type BoostModel } from '../src/boosts';
import { addMessages, t } from '../src/i18n';
import { queueEn } from '../src/i18n/queueEn';
import { QueueUi } from '../src/queue';

addMessages(queueEn);

const FIXTURE = readFileSync(new URL('../../tests/AqwCompanion.Tests/Phone/fixtures/boosts-state.json', import.meta.url), 'utf8');
const READ_AT = 1_763_481_234_567;
const entry = (over: Record<string, unknown> = {}) => ({ instanceId: 'a1', online: true, readAt: READ_AT, gold: 840, class: -1, rep: 0, xp: 0, items: [], canRelogin: false, ...over });
const item = (over: Record<string, unknown> = {}) => ({ itemId: 17439, name: 'GOLD Boost! (1 hr)', quantity: 3, canUse: true, ...over });
const one = (over: Record<string, unknown> = {}): BoostModel => parseBoosts([entry(over)])[0]!;

afterEach(() => vi.unstubAllGlobals());

describe('parseBoosts', () => {
  it('parses the exact JSON the PC writes', () => {
    expect(parseBoosts(JSON.parse(FIXTURE))).toEqual([
      {
        instanceId: 'a1',
        online: true,
        readAt: 1763481234567,
        left: { gold: 840, class: -1, rep: 0, xp: 0 },
        items: [
          { itemId: 17439, name: 'GOLD Boost! (1 hr)', quantity: 33, canUse: false },
          { itemId: 22447, name: 'Daily Login Class Boost (20 min)', quantity: 61, canUse: false },
          { itemId: 17437, name: 'CLASS Boost! (1 hr)', quantity: 70, canUse: false },
        ],
        canRelogin: false,
      },
    ]);
  });
  it('leaves out the fields the page does not use', () => {
    const b = parseBoosts(JSON.parse(FIXTURE))[0]!;
    for (const k of ['character', 'supported', 'type', 'gold', 'class', 'rep', 'xp']) expect(b).not.toHaveProperty(k);
    expect(b.items[0]).not.toHaveProperty('type');
  });
  it('a canUse true item is kept as true', () => {
    expect(one({ items: [item()] }).items[0]!.canUse).toBe(true);
  });
  it('keeps a realistic readAt', () => {
    expect(one().readAt).toBe(1763481234567);
    expect(one({ readAt: Number.MAX_SAFE_INTEGER }).readAt).toBe(Number.MAX_SAFE_INTEGER);
    for (const bad of [Number.MAX_SAFE_INTEGER + 2, -1, 1.5, '1763481234567', null, undefined]) expect(one({ readAt: bad }).readAt).toBe(0);
    const absent = entry();
    delete (absent as Record<string, unknown>).readAt;
    expect(parseBoosts([absent])[0]!.readAt).toBe(0);
  });
  it('the value fields accept -1 and int32 max and read bad ones as 0', () => {
    expect(one({ gold: -1, class: 2147483647 }).left).toEqual({ gold: -1, class: 2147483647, rep: 0, xp: 0 });
    for (const bad of [-2, 1.5, '5', null]) expect(one({ gold: bad, xp: bad }).left).toEqual({ gold: 0, class: -1, rep: 0, xp: 0 });
  });
  it('safeInt', () => {
    expect(safeInt(5)).toBe(5);
    expect(safeInt(0, 1)).toBeNull();
    expect(safeInt(-1, -1)).toBe(-1);
    expect(safeInt(NaN)).toBeNull();
  });
  it('drops what is damaged on its own', () => {
    expect(parseBoosts([{ online: true }, entry({ instanceId: 5 }), entry({ instanceId: 'ok' }), null, 'x'])).toHaveLength(1);
    const items = [
      item({ name: undefined }),
      item({ itemId: 0 }),
      item({ quantity: 1.5 }),
      item({ quantity: -1 }),
      item({ itemId: 2.5 }),
      item({ itemId: 17437, name: 'CLASS Boost! (1 hr)' }),
      item({ itemId: 17438, quantity: 0, canUse: 'true' }),
    ];
    const kept = one({ items }).items;
    expect(kept.map((i) => i.itemId)).toEqual([17437, 17438]);
    expect(kept[1]!.canUse).toBe(false);
  });
  it('anything but an array is none', () => {
    for (const v of [null, {}, 'x', 5, undefined]) expect(parseBoosts(v)).toEqual([]);
  });
  it('is bounded', () => {
    expect(parseBoosts(Array.from({ length: 9 }, (_, i) => entry({ instanceId: 'i' + i })))).toHaveLength(8);
    expect(one({ items: Array.from({ length: 13 }, (_, i) => item({ itemId: i + 1 })) }).items).toHaveLength(12);
    const name = one({ items: [item({ name: 'x'.repeat(5000) })] }).items[0]!.name;
    expect(name.length).toBe(300);
    expect(one({ instanceId: 'y'.repeat(5000) }).instanceId.length).toBeLessThan(5000);
  });
});

describe('rowText', () => {
  const b = one({ gold: 840 });
  it('counts the minutes down from readAt while online', () => {
    expect(rowText(b, 'gold', READ_AT)).toBe('Gold: 14 min left');
    expect(rowText(b, 'gold', READ_AT + 60_000)).toBe('Gold: 13 min left');
    expect(rowText(b, 'gold', READ_AT + 839_000)).toBe('Gold: 1 min left');
    expect(rowText(b, 'gold', READ_AT + 900_000)).toBe('Gold: 0 min left');
  });
  it('stays still while offline', () => {
    const off = one({ gold: 840, online: false });
    for (const now of [0, READ_AT, READ_AT + 900_000, NaN]) expect(rowText(off, 'gold', now)).toBe('Gold: 14 min left');
  });
  it('active without a time, inactive and XP', () => {
    expect(rowText(b, 'class', READ_AT)).toBe('Class: active');
    expect(rowText(b, 'rep', READ_AT)).toBe('Reputation: inactive');
    expect(rowText(b, 'xp', READ_AT)).toBe('XP: inactive');
    expect(rowText(one({ xp: 120 }), 'xp', READ_AT)).toBe('XP: 2 min left');
  });
  it('never NaN and never negative', () => {
    for (const now of [0, NaN, Infinity, -Infinity, READ_AT * 2]) {
      const text = rowText(b, 'gold', now);
      expect(text).not.toContain('NaN');
      expect(text).not.toContain('-');
      expect(text).toMatch(/^Gold: \d+ min left$/);
    }
  });
});

describe('boostFor', () => {
  it('finds by instance id and gives null for none', () => {
    const all = parseBoosts([entry({ instanceId: 'a' }), entry({ instanceId: 'b' })]);
    expect(boostFor(all, 'b')?.instanceId).toBe('b');
    expect(boostFor(all, 'c')).toBeNull();
  });
});

describe('boostApi', () => {
  it('posts the two commands', async () => {
    const calls: { path: string; init: RequestInit | undefined }[] = [];
    vi.stubGlobal('fetch', vi.fn(async (path: string, init?: RequestInit) => {
      calls.push({ path, init });
      return new Response('{}');
    }));
    await boostApi.use('i 1', 17439);
    await boostApi.relogin('i 1');
    expect(calls.map((c) => c.path)).toEqual(['/api/skua/i%201/boosts/use', '/api/skua/i%201/relogin']);
    expect(calls.map((c) => c.init?.method)).toEqual(['POST', 'POST']);
    expect(calls.map((c) => c.init?.body)).toEqual(['{"itemId":17439}', '{}']);
    for (const c of calls) expect(new Headers(c.init?.headers).get('X-Remote')).toBe('1');
  });
});

describe('QueueUi.call with the boost refusal key', () => {
  const ui = (unpaired = () => undefined) => new QueueUi({ command: async () => ({ status: 200, data: null }) }, () => undefined, unpaired);
  it('a refusal uses the boost text', async () => {
    const u = ui();
    await u.call('i-1', async () => ({ status: 409, data: { error: 'A gold boost is already active.' } }), 'boost.refused');
    expect(u.errorOf('i-1')).toBe('Could not do that: A gold boost is already active.');
    expect(u.errorOf('i-1')).toBe(t('boost.refused', { reason: 'A gold boost is already active.' }));
  });
  it.each([
    [504, null, 'control.error.noAnswer'],
    [0, null, 'control.error.unreachable'],
    [404, null, 'control.error.gone'],
    [409, { error: 'busy' }, 'control.error.busy'],
    [409, { error: 'Skua is disconnected' }, 'control.error.disconnected'],
  ] as const)('status %i maps to %s', async (status, data, key) => {
    const u = ui();
    await u.call('i-1', async () => ({ status, data }), 'boost.refused');
    expect(u.errorOf('i-1')).toBe(t(key));
  });
  it('401 calls onUnpaired', async () => {
    const unpaired = vi.fn();
    const u = ui(unpaired);
    await u.call('i-1', async () => ({ status: 401, data: null }), 'boost.refused');
    expect(unpaired).toHaveBeenCalledTimes(1);
    expect(u.errorOf('i-1')).toBeNull();
  });
  it('a second call while one is pending sends nothing', async () => {
    let release: (r: CallResult) => void = () => undefined;
    const send = vi.fn(() => new Promise<CallResult>((r) => { release = r; }));
    const u = ui();
    const first = u.call('i-1', send);
    await u.call('i-1', send);
    expect(send).toHaveBeenCalledTimes(1);
    release({ status: 200, data: {} });
    await first;
  });
});
