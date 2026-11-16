import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { en } from '../src/i18n/en';

const template = readFileSync(new URL('../src/sw.template.js', import.meta.url), 'utf8');
const SENTENCE = "Can't reach SkuaLink. Check that your PC is on and SkuaLink is running.";

interface Fake {
  handler: (event: unknown) => void;
  store: Map<string, Response>;
  put: ReturnType<typeof vi.fn>;
  fetch: ReturnType<typeof vi.fn>;
}

/** Runs the generated script the way a worker would: no modules, only the globals a worker has. */
function load(fetchImpl: (request: unknown) => Promise<Response>, cached: Record<string, Response> = {}): Fake {
  const store = new Map(Object.entries(cached));
  const listeners: Record<string, (event: unknown) => void> = {};
  const put = vi.fn(async (request: { url: string }, response: Response) => {
    store.set(new URL(request.url).pathname, response);
  });
  const fetchFn = vi.fn(fetchImpl);
  const key = (r: unknown) => (typeof r === 'string' ? r : new URL((r as { url: string }).url).pathname);
  const context = {
    self: { addEventListener: (name: string, fn: (event: unknown) => void) => (listeners[name] = fn), location: { origin: 'https://pc.tail.ts.net' }, clients: { claim: async () => undefined }, skipWaiting: async () => undefined },
    caches: {
      open: async () => ({ addAll: async () => undefined, put }),
      match: async (r: unknown) => store.get(key(r)),
      keys: async () => [],
      delete: async () => true,
    },
    fetch: fetchFn,
    Response, URL, Promise, setTimeout, clearTimeout, RegExp,
  };
  vm.runInNewContext(template.replace("'__CACHE__'", '"test"').replace('__SHELL__', '[]'), context);
  return { handler: listeners.fetch!, store, put, fetch: fetchFn };
}

interface Req { url: string; method: string; mode: string; destination: string }
const req = (path: string, over: Partial<Req> = {}): Req => ({ url: `https://pc.tail.ts.net${path}`, method: 'GET', mode: 'no-cors', destination: '', ...over });
const nav = (path = '/') => req(path, { mode: 'navigate', destination: 'document' });

function dispatch(sw: Fake, request: Req) {
  let answer: Promise<Response> | undefined;
  const waits: Promise<unknown>[] = [];
  sw.handler({ request, respondWith: (p: Promise<Response>) => (answer = p), waitUntil: (p: Promise<unknown>) => waits.push(p) });
  return { answer, waits };
}

const ok = (body = 'fresh') => {
  const r = new Response(body);
  Object.defineProperty(r, 'type', { value: 'basic' });
  return r;
};
const never = () => new Promise<Response>(() => undefined);

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('service worker shell answers', () => {
  it('uses and caches the network answer when it comes within the window', async () => {
    const sw = load(async () => ok(), { '/': new Response('old') });
    const { answer, waits } = dispatch(sw, nav());
    await vi.advanceTimersByTimeAsync(2999);
    expect(await (await answer)!.text()).toBe('fresh');
    await Promise.all(waits);
    expect(sw.put).toHaveBeenCalledOnce();
    // the timer must not keep a pending timeout around
    expect(vi.getTimerCount()).toBe(0);
  });

  it('answers from the cache after 3000 ms and still refreshes the cache when the network finally answers', async () => {
    let release!: (r: Response) => void;
    const sw = load(() => new Promise<Response>((resolve) => (release = resolve)), { '/': new Response('old') });
    const { answer, waits } = dispatch(sw, nav());
    let settled = false;
    void answer!.then(() => (settled = true));
    await vi.advanceTimersByTimeAsync(2999);
    expect(settled).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(await (await answer)!.text()).toBe('old');
    expect(sw.put).not.toHaveBeenCalled();
    release(ok('newer'));
    await Promise.all(waits);
    expect(sw.put).toHaveBeenCalledOnce();
    expect(await (sw.put.mock.calls[0]![1] as Response).text()).toBe('newer');
  });

  it('never leaves an unhandled rejection when the network fails after the cache answered', async () => {
    let fail!: (e: Error) => void;
    const sw = load(() => new Promise<Response>((_, reject) => (fail = reject)), { '/': new Response('old') });
    const { answer, waits } = dispatch(sw, nav());
    await vi.advanceTimersByTimeAsync(3000);
    expect(await (await answer)!.text()).toBe('old');
    fail(new Error('down'));
    await expect(Promise.all(waits)).resolves.toBeDefined();
  });

  it('falls back at once when the network fails', async () => {
    const sw = load(async () => { throw new Error('down'); }, { '/': new Response('old') });
    const { answer } = dispatch(sw, nav());
    await vi.advanceTimersByTimeAsync(0);
    expect(await (await answer)!.text()).toBe('old');
  });

  it('answers a navigation with nothing cached with the plain unreachable page', async () => {
    const sw = load(never);
    const { answer } = dispatch(sw, nav());
    await vi.advanceTimersByTimeAsync(3000);
    const response = (await answer)!;
    expect(response.status).toBe(503);
    expect(response.headers.get('content-type')).toBe('text/html; charset=utf-8');
    expect(response.headers.get('cache-control')).toBe('no-store');
    expect(response.headers.get('referrer-policy')).toBe('no-referrer');
    expect(response.headers.get('content-security-policy')).toBe("default-src 'none'");
    const html = await response.text();
    expect(html).toContain('<h1>');
    expect(html).toContain(SENTENCE);
    expect(html).toContain('<a href="/">Try again</a>');
    expect(html).not.toMatch(/<script|<style|style=|onclick/i);
  });

  it('keeps a missing script or style failing as such, never as HTML', async () => {
    for (const destination of ['script', 'style']) {
      const sw = load(async () => { throw new Error('down'); });
      const { answer } = dispatch(sw, req('/assets/app.js', { destination }));
      await vi.advanceTimersByTimeAsync(0);
      const response = (await answer)!;
      expect(response.status).toBe(503);
      expect(await response.text()).toBe('');
    }
  });

  it('answers a missing icon or manifest with an empty 503 too', async () => {
    const sw = load(async () => { throw new Error('down'); });
    const { answer } = dispatch(sw, req('/icons/icon-192.png', { destination: 'image' }));
    await vi.advanceTimersByTimeAsync(0);
    const response = (await answer)!;
    expect(response.status).toBe(503);
    expect(await response.text()).toBe('');
  });

  it('serves a cached script after the window', async () => {
    const sw = load(never, { '/assets/app.js': new Response('cached js') });
    const { answer } = dispatch(sw, req('/assets/app.js', { destination: 'script' }));
    await vi.advanceTimersByTimeAsync(3000);
    expect(await (await answer)!.text()).toBe('cached js');
  });

  it('leaves /api/ alone', () => {
    const sw = load(never);
    const { answer } = dispatch(sw, req('/api/state'));
    expect(answer).toBeUndefined();
    expect(sw.fetch).not.toHaveBeenCalled();
  });

  it('leaves other origins and non-GET requests alone', () => {
    const sw = load(never);
    expect(dispatch(sw, { ...req('/'), url: 'https://example.com/' }).answer).toBeUndefined();
    expect(dispatch(sw, { ...req('/'), method: 'POST' }).answer).toBeUndefined();
  });

  it('caches only the shell: a response for another path is passed through', async () => {
    const sw = load(async () => ok('data'));
    const { answer, waits } = dispatch(sw, req('/other', { destination: 'document' }));
    await vi.advanceTimersByTimeAsync(0);
    expect(await (await answer)!.text()).toBe('data');
    await Promise.all(waits);
    expect(sw.put).not.toHaveBeenCalled();
  });

  it('does not cache an opaque answer', async () => {
    const opaque = new Response('x');
    Object.defineProperty(opaque, 'type', { value: 'opaque' });
    const sw = load(async () => opaque);
    const { waits } = dispatch(sw, nav());
    await vi.advanceTimersByTimeAsync(0);
    await Promise.all(waits);
    expect(sw.put).not.toHaveBeenCalled();
  });

  it('waits for a slow script that has no cached copy, as the old code did', async () => {
    let release!: (r: Response) => void;
    const sw = load(() => new Promise<Response>((resolve) => (release = resolve)));
    const { answer } = dispatch(sw, req('/assets/index-NEW.js', { destination: 'script' }));
    await vi.advanceTimersByTimeAsync(10_000);
    release(ok('new code'));
    expect(await (await answer)!.text()).toBe('new code');
  });

  it('still fails an uncached script when the fetch really fails', async () => {
    const sw = load(async () => { throw new Error('down'); });
    const { answer } = dispatch(sw, req('/assets/index-NEW.js', { destination: 'script' }));
    await vi.advanceTimersByTimeAsync(0);
    expect((await answer)!.status).toBe(503);
  });

  it('falls back to the cached page for an uncached non-shell navigation', async () => {
    const sw = load(async () => { throw new Error('down'); }, { '/': new Response('shell') });
    const { answer } = dispatch(sw, nav('/somewhere'));
    await vi.advanceTimersByTimeAsync(0);
    expect(await (await answer)!.text()).toBe('shell');
  });

  it('does not cache a failed answer', async () => {
    const sw = load(async () => new Response('x', { status: 500 }));
    const { waits } = dispatch(sw, nav());
    await vi.advanceTimersByTimeAsync(0);
    await Promise.all(waits);
    expect(sw.put).not.toHaveBeenCalled();
  });
});

describe('service worker script', () => {
  it('has the three second window as a constant', () => expect(template).toMatch(/SHELL_WAIT_MS\s*=\s*3000\b/));
  it('writes the unreachable sentence exactly as the page catalog does, so the two cannot drift', () => {
    expect(en['banner.unreachable']).toBe(SENTENCE);
    expect(template).toContain(SENTENCE);
  });
});
interface PushFake {
  listeners: Record<string, (event: unknown) => void>;
  show: ReturnType<typeof vi.fn>;
  matchAll: ReturnType<typeof vi.fn>;
  openWindow: ReturnType<typeof vi.fn>;
  subscribe: ReturnType<typeof vi.fn>;
  fetch: ReturnType<typeof vi.fn>;
}

/** Runs the template with the extra globals the push handlers use. */
function loadPush(clients: unknown[] = [], subscribeResult: unknown = null): PushFake {
  const listeners: Record<string, (event: unknown) => void> = {};
  const show = vi.fn(async () => undefined);
  const matchAll = vi.fn(async () => clients);
  const openWindow = vi.fn(async () => undefined);
  const subscribe = vi.fn(async () => subscribeResult);
  const fetchFn = vi.fn(async () => new Response('{}'));
  const context = {
    self: {
      addEventListener: (name: string, fn: (event: unknown) => void) => (listeners[name] = fn),
      location: { origin: 'https://pc.tail.ts.net' },
      clients: { claim: async () => undefined, matchAll, openWindow },
      skipWaiting: async () => undefined,
      registration: { showNotification: show, pushManager: { subscribe } },
    },
    caches: { open: async () => ({ addAll: async () => undefined, put: async () => undefined }), match: async () => undefined, keys: async () => [], delete: async () => true },
    fetch: fetchFn,
    Response, URL, Promise, setTimeout, clearTimeout, RegExp, JSON, atob, Uint8Array,
  };
  vm.runInNewContext(template.replace("'__CACHE__'", '"test"').replace('__SHELL__', '[]'), context);
  return { listeners, show, matchAll, openWindow, subscribe, fetch: fetchFn };
}

const pushEvent = (data: unknown) => {
  const waits: Promise<unknown>[] = [];
  return { event: { data: data === undefined ? null : { json: () => (typeof data === 'string' ? JSON.parse(data) : data) }, waitUntil: (p: Promise<unknown>) => waits.push(p) }, waits };
};

describe('push', () => {
  it('shows the notification the payload describes, with the notice id as its tag and the url in data', async () => {
    const sw = loadPush();
    const { event, waits } = pushEvent({ title: 'Hero keeps dying', body: 'Hero died 5 times in 2 minutes.', tag: 'dying:Hero:1', url: '/#/character/Hero/bot', kind: 'DyingOften', at: 'x' });

    sw.listeners.push!(event);
    await Promise.all(waits);

    expect(sw.show).toHaveBeenCalledWith('Hero keeps dying', { body: 'Hero died 5 times in 2 minutes.', tag: 'dying:Hero:1', icon: '/icons/icon-192.png', data: { url: '/#/character/Hero/bot' } });
  });

  it.each([[undefined], ['not json'], [42], [null], ['"a string"'], ['[]']])('always shows a notification, even for unreadable data (%s)', async (data) => {
    const sw = loadPush();
    const { event, waits } = pushEvent(data === 'not json' ? { json: undefined } : data);
    if (data === 'not json') (event as { data: unknown }).data = { json: () => { throw new SyntaxError('bad'); } };

    sw.listeners.push!(event);
    await Promise.all(waits);

    expect(sw.show).toHaveBeenCalledTimes(1);
    expect(sw.show.mock.calls[0]![0]).toBe('SkuaLink');
    expect(sw.show.mock.calls[0]![1].body).toBe('Something happened. Open SkuaLink.');
    expect(sw.show.mock.calls[0]![1].data).toEqual({ url: '/' });
  });

  it.each(['https://evil.example/x', '//evil.example/x', 'javascript:alert(1)', '\\evil', 5, null, '', '/other/path', '/#/a b', '/#/a\\b', '#x', '/#evil'])('never lets a payload send the tap elsewhere (%s)', async (url) => {
    const sw = loadPush();
    const { event, waits } = pushEvent({ title: 't', body: 'b', url });

    sw.listeners.push!(event);
    await Promise.all(waits);

    expect(sw.show.mock.calls[0]![1].data).toEqual({ url: '/' });
  });

  it('cuts very long texts and ignores non-string fields', async () => {
    const sw = loadPush();
    const { event, waits } = pushEvent({ title: 'x'.repeat(500), body: 'y'.repeat(900), tag: 7 });

    sw.listeners.push!(event);
    await Promise.all(waits);

    const [title, options] = sw.show.mock.calls[0]!;
    expect(title).toHaveLength(100);
    expect(options.body).toHaveLength(300);
    expect(options.tag).toBe('skualink');
  });
});

describe('notification click', () => {
  const clickEvent = (url: unknown) => {
    const waits: Promise<unknown>[] = [];
    const close = vi.fn();
    return { event: { notification: { close, data: url === undefined ? undefined : { url } }, waitUntil: (p: Promise<unknown>) => waits.push(p) }, waits, close };
  };

  it('closes the notification, tells an open page of this site to show the section and focuses it', async () => {
    const page = { url: 'https://pc.tail.ts.net/#/', postMessage: vi.fn(), focus: vi.fn(async () => undefined) };
    const other = { url: 'https://other.example/', postMessage: vi.fn(), focus: vi.fn(async () => undefined) };
    const sw = loadPush([other, page]);
    const { event, waits, close } = clickEvent('/#/character/Hero/bot');

    sw.listeners.notificationclick!(event);
    await Promise.all(waits);

    expect(close).toHaveBeenCalled();
    expect(page.postMessage).toHaveBeenCalledWith({ type: 'qkp-open', hash: '#/character/Hero/bot' });
    expect(page.focus).toHaveBeenCalled();
    expect(other.postMessage).not.toHaveBeenCalled();
    expect(sw.openWindow).not.toHaveBeenCalled();
  });

  it('opens a window at the url when no page is open', async () => {
    const sw = loadPush([]);
    const { event, waits } = clickEvent('/#/character/Hero/bot');

    sw.listeners.notificationclick!(event);
    await Promise.all(waits);

    expect(sw.openWindow).toHaveBeenCalledWith('/#/character/Hero/bot');
  });

  it('a url without a section opens the list (hash #/) and a hostile or missing url falls back to /', async () => {
    const page = { url: 'https://pc.tail.ts.net/', postMessage: vi.fn(), focus: vi.fn(async () => undefined) };
    const sw = loadPush([page]);
    for (const url of ['/', 'https://evil.example/', undefined]) {
      const { event, waits } = clickEvent(url);
      sw.listeners.notificationclick!(event);
      await Promise.all(waits);
    }

    expect(page.postMessage.mock.calls.map((c) => c[0].hash)).toEqual(['#/', '#/', '#/']);
  });

  it('survives a client that refuses to focus', async () => {
    const page = { url: 'https://pc.tail.ts.net/', postMessage: vi.fn(), focus: vi.fn(async () => { throw new Error('no'); }) };
    const sw = loadPush([page]);
    const { event, waits } = clickEvent('/');

    sw.listeners.notificationclick!(event);

    await expect(Promise.all(waits)).resolves.toBeDefined();
  });
});

describe('push subscription change', () => {
  const changeEvent = (extra: Record<string, unknown>) => {
    const waits: Promise<unknown>[] = [];
    return { event: { ...extra, waitUntil: (p: Promise<unknown>) => waits.push(p) }, waits };
  };
  const sub = { toJSON: () => ({ endpoint: 'https://fcm.googleapis.com/x', keys: { p256dh: 'P', auth: 'A' } }) };

  it('subscribes again with the old options and registers the new subscription without kinds', async () => {
    const sw = loadPush([], sub);
    const options = { userVisibleOnly: true, applicationServerKey: new Uint8Array([1]) };
    const { event, waits } = changeEvent({ oldSubscription: { options } });

    sw.listeners.pushsubscriptionchange!(event);
    await Promise.all(waits);

    expect(sw.subscribe).toHaveBeenCalledWith(options);
    const [path, init] = sw.fetch.mock.calls[0]!;
    expect(path).toBe('/api/push/subscription');
    expect(init.method).toBe('PUT');
    expect(init.headers['X-Remote']).toBe('1');
    expect(JSON.parse(init.body)).toEqual({ endpoint: 'https://fcm.googleapis.com/x', keys: { p256dh: 'P', auth: 'A' } });
  });

  it('uses the new subscription the browser already made', async () => {
    const sw = loadPush();
    const { event, waits } = changeEvent({ newSubscription: sub });

    sw.listeners.pushsubscriptionchange!(event);
    await Promise.all(waits);

    expect(sw.subscribe).not.toHaveBeenCalled();
    expect(sw.fetch).toHaveBeenCalledTimes(1);
  });

  it('swallows every failure', async () => {
    const sw = loadPush([], null);
    sw.fetch.mockRejectedValue(new Error('offline'));
    const { event, waits } = changeEvent({ oldSubscription: { options: {} } });

    sw.listeners.pushsubscriptionchange!(event);

    await expect(Promise.all(waits)).resolves.toBeDefined();
  });
});

describe('push url shape', () => {
  it.each(['#/character/Hero/bot', '/#/', '/'])('keeps the allowed url %s', async (url) => {
    const sw = loadPush();
    const { event, waits } = pushEvent({ title: 't', body: 'b', url });
    sw.listeners.push!(event);
    await Promise.all(waits);
    expect(sw.show.mock.calls[0]![1].data).toEqual({ url });
  });

  it('still shows the notification when showNotification itself throws synchronously', () => {
    const sw = loadPush();
    sw.show.mockImplementationOnce(() => { throw new Error('boom'); });
    const { event } = pushEvent({ title: 't' });
    expect(() => sw.listeners.push!(event)).not.toThrow();
    expect(sw.show).toHaveBeenCalledTimes(2);
  });
});

describe('push subscription change without old options', () => {
  it('reads the VAPID key from the PC and subscribes with it', async () => {
    const sub = { toJSON: () => ({ endpoint: 'https://fcm.googleapis.com/x', keys: { p256dh: 'P', auth: 'A' } }) };
    const sw = loadPush([], sub);
    sw.fetch.mockImplementation(async (path: string) => new Response(path === '/api/push/key' ? JSON.stringify({ publicKey: 'AQID' }) : '{}'));
    const waits: Promise<unknown>[] = [];

    sw.listeners.pushsubscriptionchange!({ waitUntil: (p: Promise<unknown>) => waits.push(p) });
    await Promise.all(waits);

    expect(sw.fetch.mock.calls[0]![0]).toBe('/api/push/key');
    expect(sw.fetch.mock.calls[0]![1].credentials).toBe('same-origin');
    const options = sw.subscribe.mock.calls[0]![0];
    expect(options.userVisibleOnly).toBe(true);
    expect(Array.from(options.applicationServerKey as Uint8Array)).toEqual([1, 2, 3]);
    const [path, init] = sw.fetch.mock.calls[1]!;
    expect(path).toBe('/api/push/subscription');
    expect(init.credentials).toBe('same-origin');
    expect(JSON.parse(init.body)).not.toHaveProperty('kinds');
  });
});
