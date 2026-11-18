import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { pushEn as en } from '../src/i18n/pushEn';
import { t } from '../src/i18n';
import { keyBytes, type PushKind, parsePushState, pushApi, pushSupport, PUSH_KINDS, type CallResult, type PushApi, type PushEnv } from '../src/push';
import { FocusKeeper, PushController, pushBlockView, pushNoteText, type PushBrowser, type PushModel } from '../src/pushUi';
import { pushLoader } from '../src/dockUi';
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

function rig(over: { env?: Partial<PushEnv>; api?: Partial<Record<keyof PushApi, CallResult | (() => Promise<CallResult>)>>; permission?: NotificationPermission; sub?: typeof SUB | null; has?: boolean | 'throws' } = {}) {
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
    hasSubscription: vi.fn(async () => {
      if (over.has === 'throws') throw new Error('no worker');
      return over.has ?? true;
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
  it('a dismissed prompt is not a block: Enable stays available and no API is called', async () => {
    const r = rig({ permission: 'default' });
    await r.controller.enable();
    expect(r.controller.model.support).toBe('ready');
    expect(r.log).toEqual(['permission']);
    expect(r.controller.model.note).toBeNull();
  });
  it('a refused permission is denied and calls no API', async () => {
    const r = rig({ permission: 'denied' });
    await r.controller.enable();
    expect(r.controller.model.support).toBe('denied');
    expect(r.log).toEqual(['permission']);
    expect(r.controller.model.busy).toBe(false);
  });
  it('a 503 from the PC says to try again in a moment, in key and subscribe', async () => {
    const a = rig({ api: { key: res(503) } });
    await a.controller.enable();
    expect(a.controller.model.note).toEqual({ key: 'push.retryLater' });
    const b = rig({ api: { subscribe: res(503) } });
    await b.controller.enable();
    expect(b.controller.model.note).toEqual({ key: 'push.retryLater' });
    expect(b.browser.unsubscribe).toHaveBeenCalled();
  });
  it('a key failure is push.pcFailed', async () => {
    const r = rig({ api: { key: res(500) } });
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
  it('disable asks the PC, then the browser, and ends off', async () => {
    const r = rig();
    await r.controller.enable();
    await r.controller.disable();
    expect(r.log.slice(-2)).toEqual(['unsubscribe', 'browser.unsubscribe']);
    expect(r.controller.model.state).toEqual({ subscribed: false, kinds: [], lastError: null });
  });
  it.each([
    [500, 'push.pcFailed'],
    [0, 'push.pcFailed'],
    [503, 'push.retryLater'],
  ] as const)('a PC that answers %i to the disable keeps the on state and says so', async (status, key) => {
    const r = rig({ api: { unsubscribe: res(status) } });
    await r.controller.enable();
    await r.controller.disable();
    expect(r.controller.model.state).toEqual(ONSTATE);
    expect(r.controller.model.note).toEqual({ key });
    expect(r.browser.unsubscribe).not.toHaveBeenCalled();
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
  it('a 502 says the PC could not reach the push service, with the cut server error, and refreshes the state', async () => {
    const r = rig({ api: { test: res(502, { error: 'e'.repeat(300) }) } });
    await r.controller.test();
    expect(r.controller.model.note).toEqual({ key: 'push.testUnreachable', params: { error: 'e'.repeat(200) } });
    expect(r.log).toEqual(['test', 'state']);
  });
  it('another refusal shows the cut server error, or status N without one', async () => {
    const a = rig({ api: { test: res(403, { error: 'x'.repeat(300) }) } });
    await a.controller.test();
    expect(a.controller.model.note).toEqual({ key: 'push.testFailed', params: { error: 'x'.repeat(200) } });
    const b = rig({ api: { test: res(500) } });
    await b.controller.test();
    expect(b.controller.model.note).toEqual({ key: 'push.testFailed', params: { error: 'status 500' } });
  });
  it('a test the PC cannot answer (0) or is busy with (503) says so and does not refresh', async () => {
    const a = rig({ api: { test: res(0) } });
    await a.controller.test();
    expect(a.controller.model.note).toEqual({ key: 'push.pcFailed' });
    const b = rig({ api: { test: res(503) } });
    await b.controller.test();
    expect(b.controller.model.note).toEqual({ key: 'push.retryLater' });
    expect(b.log).toEqual(['test']);
  });
  it('refresh loads the state when ready, and flags a failure', async () => {
    const r = rig({ api: { state: res(200, ONSTATE) } });
    await r.controller.refresh();
    expect(r.controller.model.state).toEqual(ONSTATE);
    const bad = rig({ api: { state: res(0) } });
    await bad.controller.refresh();
    expect(bad.controller.model.loadFailed).toBe(true);
  });
  it.each([
    ['PC on, browser has it', true, true, true],
    ['PC on, browser has none', true, false, false],
    ['PC off, browser has one', false, true, false],
    ['PC off, browser has none', false, false, false],
  ] as const)('refresh reconciles: %s', async (_name, server, browser, shownOn) => {
    const r = rig({ api: { state: res(200, { subscribed: server, kinds: ['RankUp'], lastError: null }) }, has: browser });
    await r.controller.refresh();
    expect(r.controller.model.state!.subscribed).toBe(shownOn);
    const view = pushBlockView(r.controller.model);
    expect(all(view).filter((n) => n.tag === 'input')).toHaveLength(shownOn ? 10 : 0);
    expect(all(view).some((n) => n.attrs?.['data-action'] === 'push-enable')).toBe(!shownOn);
  });
  it('refresh trusts the PC when the browser cannot be asked', async () => {
    const r = rig({ api: { state: res(200, ONSTATE) }, has: 'throws' });
    await r.controller.refresh();
    expect(r.controller.model.state!.subscribed).toBe(true);
  });
  it('a PC that thinks it is on but a browser with none can enable again, and the enable replaces the browser subscription', async () => {
    const r = rig({ api: { state: res(200, ONSTATE) }, has: false });
    await r.controller.refresh();
    await r.controller.enable();
    expect(r.log).toEqual(['state', 'permission', 'key', 'browser.subscribe', 'subscribe']);
    expect(r.controller.model.state!.subscribed).toBe(true);
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
  it('subscribed shows ten switches, checked only for the enabled kinds', () => {
    const v = pushBlockView(model({ state: on(['RankUp', 'ScriptError']), note: { key: 'push.testSent' } }));
    expect(all(v).find((n) => n.attrs?.role === 'group')!.attrs!['aria-label']).toBe(t('push.kinds'));
    expect(all(v).filter((n) => n.attrs?.role === 'group')[0]!.children).toHaveLength(10);
    expect(all(v).find((n) => n.tag === 'h3')!.text).toBe(t('push.title'));
    const inputs = all(v).filter((n) => n.tag === 'input');
    expect(inputs).toHaveLength(10);
    expect(inputs.map((i) => i.attrs!['data-value'])).toEqual([...PUSH_KINDS]);
    for (const i of inputs) {
      expect(i.attrs).toMatchObject({ type: 'checkbox', role: 'switch', 'data-action': 'push-kind' });
      expect('checked' in i.attrs!).toBe(['RankUp', 'ScriptError'].includes(i.attrs!['data-value']!));
    }
    expect(texts(v)).toEqual(expect.arrayContaining([t('push.on'), t('push.limits'), ...PUSH_KINDS.map((k) => en[`push.kind.${k}` as keyof typeof en])]));
    expect(pushNoteText(model({ note: { key: 'push.testSent' } }))).toBe(t('push.testSent'));
    expect(pushNoteText(model())).toBe('');
    const buttons = all(v).filter((n) => n.tag === 'button');
    expect(buttons.map((x) => x.attrs!['data-action'])).toEqual(['push-test', 'push-disable']);
    expect(all(v).some((n) => n.attrs?.role === 'status')).toBe(false); // the live region is drawn once by startPush, not re-created with the block
  });
  it('every button has type=button', () => {
    for (const m of [model({ state: on([]) }), model({ state: { subscribed: false, kinds: [], lastError: null } })])
      for (const b of all(pushBlockView(m)).filter((n) => n.tag === 'button')) expect(b.attrs!.type).toBe('button');
  });
  it('a lastError offers Subscribe again and is only ever a text field', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    const v = pushBlockView(model({ state: on([], hostile), note: { key: 'push.testFailed', params: { error: hostile } } }));
    expect(texts(v)).toContain(t('push.lastError', { error: hostile }));
    expect(pushNoteText(model({ note: { key: 'push.testFailed', params: { error: hostile } } }))).toBe(t('push.testFailed', { error: hostile }));
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
  it('has a label for every kind and the kinds are the ten of the PC', () => {
    expect([...PUSH_KINDS]).toEqual(['ScriptStopped', 'ScriptError', 'SkuaDisconnected', 'CharacterKicked', 'SkuaOptionsWaiting', 'RankUp', 'HardFarmDrop', 'NoProgress', 'DyingOften', 'UpdateAvailable']);
    for (const k of PUSH_KINDS) expect(en).toHaveProperty(`push.kind.${k}`);
    expect(en['push.kind.UpdateAvailable']).toBe('A SkuaLink update is available');
  });
});

describe('the lazy catalog', () => {
  it('is not in the first-load catalog', async () => {
    const { en: first } = await import('../src/i18n/en');
    expect(Object.keys(first).filter((k) => k.startsWith('push.'))).toEqual(['push.chunkFailed']); // the one text for a chunk that cannot load
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

describe('FocusKeeper', () => {
  const rigFocus = () => {
    const el = { disabled: true, focus: vi.fn() };
    let current: string | null = null;
    let found: typeof el | null = el;
    const keeper = new FocusKeeper({ current: () => current, find: () => found });
    return { el, keeper, setCurrent: (k: string | null) => (current = k), setFound: (f: typeof el | null) => (found = f) };
  };
  it('waits while the control is disabled by the running action, then focuses it once it is enabled', () => {
    const f = rigFocus();
    f.keeper.activate('push-test|');
    f.keeper.before();
    f.keeper.after(true); // busy render: the control is disabled, focus() would do nothing
    expect(f.el.focus).not.toHaveBeenCalled();
    f.el.disabled = false;
    f.keeper.before();
    f.keeper.after(false); // the action finished
    expect(f.el.focus).toHaveBeenCalledTimes(1);
    f.keeper.before();
    f.keeper.after(false); // later redraws leave focus alone
    expect(f.el.focus).toHaveBeenCalledTimes(1);
  });
  it('remembers what had focus before a redraw, and gives up quietly when the control is gone', () => {
    const f = rigFocus();
    f.el.disabled = false;
    f.setCurrent('push-kind|RankUp');
    f.keeper.before();
    f.setCurrent(null);
    f.keeper.after(false);
    expect(f.el.focus).toHaveBeenCalledTimes(1);
    f.keeper.activate('push-enable|');
    f.setFound(null);
    expect(() => f.keeper.after(false)).not.toThrow();
  });
  it('does nothing when nothing was used', () => {
    const f = rigFocus();
    f.el.disabled = false;
    f.keeper.before();
    f.keeper.after(false);
    expect(f.el.focus).not.toHaveBeenCalled();
  });
});

describe('pushLoader (the chunk behind the Settings panel)', () => {
  const rigLoader = (loads: (() => Promise<{ attach(slot: HTMLElement): void }>)[]) => {
    const slot = { id: 'slot' } as unknown as HTMLElement;
    const fail = vi.fn();
    let n = 0;
    const attach = pushLoader({ load: () => loads[Math.min(n++, loads.length - 1)]!(), slot: () => slot, fail });
    return { attach, fail, slot, loadCount: () => n };
  };
  it('loads once, attaches after the load and on every later mount', async () => {
    const attached = vi.fn();
    const r = rigLoader([async () => ({ attach: attached })]);
    r.attach();
    r.attach(); // while loading: no second load
    await settle();
    expect(r.loadCount()).toBe(1);
    expect(attached).toHaveBeenCalledTimes(1);
    r.attach();
    expect(attached).toHaveBeenCalledTimes(2);
    expect(r.loadCount()).toBe(1);
    expect(r.fail).not.toHaveBeenCalled();
  });
  it('says so in the slot when the chunk cannot load, and tries again on the next opening', async () => {
    const attached = vi.fn();
    const r = rigLoader([() => Promise.reject(new Error('offline')), async () => ({ attach: attached })]);
    r.attach();
    await settle();
    expect(r.fail).toHaveBeenCalledWith(r.slot);
    expect(attached).not.toHaveBeenCalled();
    r.attach(); // the next opening
    await settle();
    expect(r.loadCount()).toBe(2);
    expect(attached).toHaveBeenCalledTimes(1);
  });
  it('does nothing without a slot', () => {
    const load = vi.fn();
    pushLoader({ load, slot: () => null, fail: vi.fn() })();
    expect(load).not.toHaveBeenCalled();
  });
  it('the failure text is in the first-load catalog and the chunk is reached only through import()', async () => {
    const { en: first } = await import('../src/i18n/en');
    expect(first['push.chunkFailed']).toMatch(/Notifications could not load/);
    expect(readFileSync(new URL('../src/dockUi.ts', import.meta.url), 'utf8')).toMatch(/import\('\.\/pushUi'\)/);
  });
});
