import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { pushEn as en } from '../src/i18n/pushEn';
import { t } from '../src/i18n';
import { keyBytes, type PushKind, parsePushState, pushApi, pushSupport, PUSH_KINDS, type CallResult, type PushApi, type PushEnv } from '../src/push';
import { PushController, pushBlockView, type PushBrowser, type PushModel } from '../src/pushUi';
import type { VNode } from '../src/views';

const settle = async (): Promise<void> => {
  for (let i = 0; i < 30; i++) await Promise.resolve();
};
const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];
const texts = (n: VNode): string[] => all(n).map((x) => x.text).filter((x): x is string => x !== undefined);

const env = (over: Partial<PushEnv> = {}): PushEnv => ({ ios: false, standalone: false, hasServiceWorker: true, hasPushManager: true, hasNotification: true, permission: 'default', ...over });

describe('pushSupport', () => {
  it('iPhone outside the Home Screen needs it, even when the APIs exist', () => expect(pushSupport(env({ ios: true, standalone: false }))).toBe('needs-home-screen'));
  it('iPhone from the Home Screen is ready', () => expect(pushSupport(env({ ios: true, standalone: true }))).toBe('ready'));
  it('no PushManager is unsupported', () => expect(pushSupport(env({ hasPushManager: false }))).toBe('unsupported'));
  it('a blocked permission is denied', () => expect(pushSupport(env({ permission: 'denied' }))).toBe('denied'));
  it('Android Chrome is ready', () => expect(pushSupport(env())).toBe('ready'));
});

describe('parsePushState', () => {
  it('reads a valid state', () => expect(parsePushState({ subscribed: true, kinds: ['RankUp', 'ScriptError'], lastError: 'x' })).toEqual({ subscribed: true, kinds: ['ScriptError', 'RankUp'], lastError: 'x' }));
  it('drops unknown kinds', () => expect(parsePushState({ subscribed: true, kinds: ['RankUp', 'Nope'], lastError: null })!.kinds).toEqual(['RankUp']));
  it('kinds not an array is null', () => expect(parsePushState({ subscribed: true, kinds: 'RankUp' })).toBeNull());
  it('subscribed not boolean is null', () => expect(parsePushState({ subscribed: 'yes', kinds: [] })).toBeNull());
  it('cuts lastError to 200 characters', () => expect(parsePushState({ subscribed: true, kinds: [], lastError: 'a'.repeat(500) })!.lastError).toHaveLength(200));
  it('a numeric lastError is null', () => expect(parsePushState({ subscribed: true, kinds: [], lastError: 5 })!.lastError).toBeNull());
  it('not an object is null', () => expect(parsePushState(null)).toBeNull());
});

describe('keyBytes', () => {
  it('round trips a 65-byte key', () => {
    const bytes = Uint8Array.from([4, ...Array.from({ length: 64 }, (_, i) => (i * 7 + 1) % 256)]);
    const text = btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    expect(Array.from(keyBytes(text))).toEqual(Array.from(bytes));
  });
  it('throws on garbage', () => {
    expect(() => keyBytes('not base64!')).toThrow();
    expect(() => keyBytes('')).toThrow();
  });
});

describe('pushApi', () => {
  const real = globalThis.fetch;
  afterEach(() => {
    globalThis.fetch = real;
  });
  const fake = (): ReturnType<typeof vi.fn> => {
    const f = vi.fn(async () => new Response(JSON.stringify({ ok: 1 }), { status: 200 }));
    globalThis.fetch = f as unknown as typeof fetch;
    return f;
  };
  const headers = (init: RequestInit): Headers => new Headers(init.headers);

  it('subscribe PUTs the endpoint and keys, with no kinds', async () => {
    const f = fake();
    const r = await pushApi.subscribe({ endpoint: 'https://p/e', p256dh: 'K', auth: 'A' });
    const [path, init] = f.mock.calls[0] as [string, RequestInit];
    expect(path).toBe('/api/push/subscription');
    expect(init.method).toBe('PUT');
    expect(headers(init).get('X-Remote')).toBe('1');
    expect(headers(init).get('Content-Type')).toBe('application/json');
    expect(init.body).toBe('{"endpoint":"https://p/e","keys":{"p256dh":"K","auth":"A"}}');
    expect(init.body).not.toContain('kinds');
    expect(r).toEqual({ status: 200, data: { ok: 1 } });
  });
  it('setKinds, unsubscribe, test and the two reads use their routes', async () => {
    const f = fake();
    await pushApi.setKinds(['RankUp']);
    await pushApi.unsubscribe();
    await pushApi.test();
    await pushApi.state();
    await pushApi.key();
    const calls = f.mock.calls as [string, RequestInit][];
    expect(calls.map(([p, i]) => `${i.method ?? 'GET'} ${p}`)).toEqual(['PUT /api/push/kinds', 'DELETE /api/push/subscription', 'POST /api/push/test', 'GET /api/push/state', 'GET /api/push/key']);
    expect(calls[0]![1].body).toBe('{"kinds":["RankUp"]}');
  });
  it('a rejected fetch is status 0 with no data', async () => {
    globalThis.fetch = vi.fn(async () => {
      throw new Error('offline');
    }) as unknown as typeof fetch;
    expect(await pushApi.state()).toEqual({ status: 0, data: null });
  });
});

const res = (status: number, data: unknown = null): CallResult => ({ status, data });
const SUB = { endpoint: 'https://push.example/abc', p256dh: 'P', auth: 'A' };
const OFFSTATE = { subscribed: false, kinds: [], lastError: null };
const ONSTATE = { subscribed: true, kinds: [...PUSH_KINDS], lastError: null };

function rig(over: { env?: Partial<PushEnv>; api?: Partial<Record<keyof PushApi, CallResult | (() => Promise<CallResult>)>>; permission?: NotificationPermission; sub?: typeof SUB | null } = {}) {
  const log: string[] = [];
  const answer = (name: keyof PushApi, fallback: CallResult) => async (..._a: unknown[]): Promise<CallResult> => {
    log.push(name);
    const v = over.api?.[name] ?? fallback;
    return typeof v === 'function' ? v() : v;
  };
  const api = {
    state: answer('state', res(200, OFFSTATE)),
    key: answer('key', res(200, { publicKey: 'BAEC' })),
    subscribe: answer('subscribe', res(200, ONSTATE)),
    unsubscribe: answer('unsubscribe', res(200, OFFSTATE)),
    setKinds: answer('setKinds', res(200, ONSTATE)),
    test: answer('test', res(200, {})),
  } as unknown as PushApi;
  const setKindsSpy = vi.fn(api.setKinds);
  api.setKinds = setKindsSpy as unknown as PushApi['setKinds'];
  const browser = {
    requestPermission: vi.fn(async () => {
      log.push('permission');
      return over.permission ?? 'granted';
    }),
    subscribe: vi.fn(async (_k: Uint8Array) => {
      log.push('browser.subscribe');
      return over.sub === undefined ? SUB : over.sub;
    }),
    unsubscribe: vi.fn(async () => {
      log.push('browser.unsubscribe');
    }),
  } satisfies PushBrowser;
  const onChange = vi.fn();
  const controller = new PushController({ env: () => env(over.env), api, browser, onChange });
  return { controller, browser, onChange, log, setKindsSpy };
}

describe('PushController.enable', () => {
  it('runs permission, key, browser subscribe, PC subscribe, in that order', async () => {
    const r = rig();
    await r.controller.enable();
    expect(r.log).toEqual(['permission', 'key', 'browser.subscribe', 'subscribe']);
    expect(r.browser.subscribe).toHaveBeenCalledWith(keyBytes('BAEC'));
    expect(r.controller.model.state).toEqual(ONSTATE);
    expect(r.controller.model.busy).toBe(false);
  });
  it('asks for the permission synchronously, inside the tap', () => {
    const r = rig();
    void r.controller.enable();
    expect(r.browser.requestPermission).toHaveBeenCalledTimes(1);
  });
  it('a refused permission is denied and calls no API', async () => {
    const r = rig({ permission: 'denied' });
    await r.controller.enable();
    expect(r.controller.model.support).toBe('denied');
    expect(r.log).toEqual(['permission']);
    expect(r.controller.model.busy).toBe(false);
  });
  it('a key failure is push.pcFailed', async () => {
    const r = rig({ api: { key: res(503) } });
    await r.controller.enable();
    expect(r.controller.model.note).toEqual({ key: 'push.pcFailed' });
    expect(r.log).not.toContain('browser.subscribe');
  });
  it('a key answer without publicKey is push.pcFailed', async () => {
    const r = rig({ api: { key: res(200, {}) } });
    await r.controller.enable();
    expect(r.controller.model.note).toEqual({ key: 'push.pcFailed' });
  });
  it('a browser that gives no subscription is push.unsupported', async () => {
    const r = rig({ sub: null });
    await r.controller.enable();
    expect(r.controller.model.note).toEqual({ key: 'push.unsupported' });
  });
  it('a refused PC subscription undoes the browser one', async () => {
    const r = rig({ api: { subscribe: res(400) } });
    await r.controller.enable();
    expect(r.browser.unsubscribe).toHaveBeenCalled();
    expect(r.controller.model.note).toEqual({ key: 'push.pcFailed' });
    expect(r.controller.model.state).toBeNull();
  });
  it('an exception is push.pcFailed and not busy', async () => {
    const r = rig({ api: { key: () => Promise.reject(new Error('boom')) } });
    await r.controller.enable();
    expect(r.controller.model.note).toEqual({ key: 'push.pcFailed' });
    expect(r.controller.model.busy).toBe(false);
  });
  it('two simultaneous taps make one run', async () => {
    const r = rig();
    const a = r.controller.enable();
    const b = r.controller.enable();
    await Promise.all([a, b]);
    expect(r.browser.requestPermission).toHaveBeenCalledTimes(1);
    expect(r.log.filter((x) => x === 'subscribe')).toHaveLength(1);
  });
  it('subscribe again with a lastError re-runs the same flow', async () => {
    const r = rig();
    await r.controller.enable();
    await r.controller.enable();
    expect(r.log.filter((x) => x === 'subscribe')).toHaveLength(2);
  });
});

describe('PushController other actions', () => {
  it('disable tries both and ends off', async () => {
    const r = rig({ api: { unsubscribe: res(500) } });
    await r.controller.enable();
    await r.controller.disable();
    expect(r.log.slice(-2)).toEqual(['unsubscribe', 'browser.unsubscribe']);
    expect(r.controller.model.state).toEqual({ subscribed: false, kinds: [], lastError: null });
  });
  it('toggle removes and adds in PUSH_KINDS order', async () => {
    const r = rig();
    await r.controller.enable();
    await r.controller.toggle('ScriptError', false);
    expect(r.setKindsSpy).toHaveBeenLastCalledWith(PUSH_KINDS.filter((k) => k !== 'ScriptError'));
    r.controller.model = { ...r.controller.model, state: { subscribed: true, kinds: ['RankUp'], lastError: null } };
    await r.controller.toggle('ScriptStopped', true);
    expect(r.setKindsSpy).toHaveBeenLastCalledWith(['ScriptStopped', 'RankUp']);
  });
  it('a failed toggle keeps the state and notes pcFailed', async () => {
    const r = rig({ api: { setKinds: res(500) } });
    await r.controller.enable();
    const before = r.controller.model.state;
    await r.controller.toggle('RankUp', false);
    expect(r.controller.model.state).toEqual(before);
    expect(r.controller.model.note).toEqual({ key: 'push.pcFailed' });
  });
  it('a good toggle takes the answered state', async () => {
    const r = rig({ api: { setKinds: res(200, { subscribed: true, kinds: ['RankUp'], lastError: null }) } });
    await r.controller.enable();
    await r.controller.toggle('ScriptStopped', false);
    expect(r.controller.model.state!.kinds).toEqual(['RankUp']);
  });
  it.each([
    [200, 'push.testSent'],
    [429, 'push.testWait'],
    [410, 'push.testGone'],
  ] as const)('test %i gives %s', async (status, key) => {
    const r = rig({ api: { test: res(status) } });
    await r.controller.test();
    expect(r.controller.model.note).toEqual({ key });
    expect(r.controller.model.busy).toBe(false);
  });
  it('test 410 refreshes the state', async () => {
    const r = rig({ api: { test: res(410) } });
    await r.controller.test();
    expect(r.log).toEqual(['test', 'state']);
  });
  it('another failure shows the cut server error and refreshes the state', async () => {
    const r = rig({ api: { test: res(502, { error: 'e'.repeat(300) }) } });
    await r.controller.test();
    expect(r.controller.model.note).toEqual({ key: 'push.testFailed', params: { error: 'e'.repeat(200) } });
    expect(r.log).toEqual(['test', 'state']);
  });
  it('a failure without an error text says status N', async () => {
    const r = rig({ api: { test: res(0) } });
    await r.controller.test();
    expect(r.controller.model.note).toEqual({ key: 'push.testFailed', params: { error: 'status 0' } });
  });
  it('refresh loads the state when ready, and flags a failure', async () => {
    const r = rig({ api: { state: res(200, ONSTATE) } });
    await r.controller.refresh();
    expect(r.controller.model.state).toEqual(ONSTATE);
    const bad = rig({ api: { state: res(0) } });
    await bad.controller.refresh();
    expect(bad.controller.model.loadFailed).toBe(true);
  });
  it('refresh on an unsupported browser makes no API call', async () => {
    const r = rig({ env: { hasPushManager: false } });
    await r.controller.refresh();
    expect(r.log).toEqual([]);
    expect(r.controller.model.support).toBe('unsupported');
  });
  it('calls onChange on changes', async () => {
    const r = rig();
    await r.controller.enable();
    expect(r.onChange.mock.calls.length).toBeGreaterThanOrEqual(2);
    await settle();
  });
});

const model = (over: Partial<PushModel> = {}): PushModel => ({ support: 'ready', state: null, busy: false, note: null, loadFailed: false, ...over });
const on = (kinds: PushKind[], lastError: string | null = null) => ({ subscribed: true, kinds, lastError });

describe('pushBlockView', () => {
  it.each([
    ['needs-home-screen', 'push.needsHomeScreen'],
    ['unsupported', 'push.unsupported'],
    ['denied', 'push.denied'],
  ] as const)('%s shows %s and no button', (support, key) => {
    const v = pushBlockView(model({ support }));
    expect(texts(v)).toContain(t(key));
    expect(all(v).filter((n) => n.tag === 'button')).toHaveLength(0);
  });
  it('ready with no state shows working, or loadFailed', () => {
    expect(texts(pushBlockView(model()))).toContain(t('push.working'));
    expect(texts(pushBlockView(model({ loadFailed: true })))).toContain(t('push.loadFailed'));
  });
  it('not subscribed shows the intro, the enable button and the limits', () => {
    const v = pushBlockView(model({ state: { subscribed: false, kinds: [], lastError: null } }));
    expect(texts(v)).toEqual(expect.arrayContaining([t('push.intro'), t('push.enable'), t('push.limits')]));
    const b = all(v).filter((n) => n.tag === 'button');
    expect(b.map((x) => x.attrs!['data-action'])).toEqual(['push-enable']);
    expect(all(v).some((n) => n.tag === 'input')).toBe(false);
  });
  it('busy disables the enable button', () => {
    const v = pushBlockView(model({ busy: true, state: { subscribed: false, kinds: [], lastError: null } }));
    expect(all(v).find((n) => n.tag === 'button')!.attrs).toHaveProperty('disabled');
    const idle = pushBlockView(model({ state: { subscribed: false, kinds: [], lastError: null } }));
    expect(all(idle).find((n) => n.tag === 'button')!.attrs).not.toHaveProperty('disabled');
  });
  it('subscribed shows nine switches, checked only for the enabled kinds', () => {
    const v = pushBlockView(model({ state: on(['RankUp', 'ScriptError']), note: { key: 'push.testSent' } }));
    const inputs = all(v).filter((n) => n.tag === 'input');
    expect(inputs).toHaveLength(9);
    expect(inputs.map((i) => i.attrs!['data-value'])).toEqual([...PUSH_KINDS]);
    for (const i of inputs) {
      expect(i.attrs).toMatchObject({ type: 'checkbox', role: 'switch', 'data-action': 'push-kind' });
      expect('checked' in i.attrs!).toBe(['RankUp', 'ScriptError'].includes(i.attrs!['data-value']!));
    }
    expect(texts(v)).toEqual(expect.arrayContaining([t('push.on'), t('push.testSent'), t('push.limits'), ...PUSH_KINDS.map((k) => en[`push.kind.${k}` as keyof typeof en])]));
    const buttons = all(v).filter((n) => n.tag === 'button');
    expect(buttons.map((x) => x.attrs!['data-action'])).toEqual(['push-test', 'push-disable']);
    expect(all(v).find((n) => n.attrs?.role === 'status')!.text).toBe(t('push.testSent'));
  });
  it('every button has type=button', () => {
    for (const m of [model({ state: on([]) }), model({ state: { subscribed: false, kinds: [], lastError: null } })])
      for (const b of all(pushBlockView(m)).filter((n) => n.tag === 'button')) expect(b.attrs!.type).toBe('button');
  });
  it('a lastError offers Subscribe again and is only ever a text field', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    const v = pushBlockView(model({ state: on([], hostile), note: { key: 'push.testFailed', params: { error: hostile } } }));
    expect(texts(v)).toContain(t('push.lastError', { error: hostile }));
    expect(texts(v)).toContain(t('push.testFailed', { error: hostile }));
    for (const n of all(v)) for (const val of Object.values(n.attrs ?? {})) expect(val).not.toContain(hostile);
    expect(all(v).filter((n) => n.tag === 'button').map((x) => x.attrs!['data-action'])).toEqual(['push-test', 'push-disable', 'push-enable']);
  });
  it('never contains the endpoint', () => expect(JSON.stringify(pushBlockView(model({ state: on([...PUSH_KINDS]) })))).not.toContain('endpoint'));
});

describe('stylesheet and catalog', () => {
  const css = readFileSync(new URL('../src/styles/dock.css', import.meta.url), 'utf8');
  it('rows are touch sized and the panel scrolls', () => {
    expect(css).toMatch(/\.push-row\s*\{[^}]*min-height:\s*3rem/);
    expect(css).toMatch(/#dock-settings\s*\{[^}]*max-height/);
    expect(css).toMatch(/#dock-settings\s*\{[^}]*overflow-y:\s*auto/);
  });
  it('declares no colour in the new rules', () => expect(css).not.toMatch(/\.push[^{]*\{[^}]*(color|background)/));
  it('has a label for every kind and the kinds are the nine of the PC', () => {
    expect([...PUSH_KINDS]).toEqual(['ScriptStopped', 'ScriptError', 'SkuaDisconnected', 'CharacterKicked', 'SkuaOptionsWaiting', 'RankUp', 'HardFarmDrop', 'NoProgress', 'DyingOften']);
    for (const k of PUSH_KINDS) expect(en).toHaveProperty(`push.kind.${k}`);
  });
});

describe('the lazy catalog', () => {
  it('is not in the first-load catalog', async () => {
    const { en: first } = await import('../src/i18n/en');
    expect(Object.keys(first).filter((k) => k.startsWith('push.'))).toEqual([]);
  });
  it('registers its texts when pushUi loads, so the view reads them', () => {
    for (const key of Object.keys(en)) expect(t(key as keyof typeof en)).toBe(en[key as keyof typeof en]);
  });
  it('dockUi loads pushUi lazily, only through import(), once the Settings panel is mounted', () => {
    const src = readFileSync(new URL('../src/dockUi.ts', import.meta.url), 'utf8');
    expect(src).toMatch(/import\('\.\/pushUi'\)/);
    expect(src).not.toMatch(/^import .*pushUi/m);
    expect(src).toMatch(/if \(theme !== null\) attachPush\(\)/);
  });
});
