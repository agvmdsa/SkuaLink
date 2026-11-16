const CACHE = '__CACHE__';
const SHELL = __SHELL__;

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

// Push. The payload is the JSON the PC encrypted. A push that cannot be read still shows a notice: iOS drops subscriptions
// whose pushes show nothing. This script has no module system and cannot use the page's i18n catalog, so these two texts are
// plain English constants (the fixed English notification text of the spec), not part of the catalog.
const PUSH_TITLE = 'SkuaLink';
const PUSH_BODY = 'Something happened. Open SkuaLink.';
const PUSH_TAG = 'skualink';
const PUSH_ICON = '/icons/icon-192.png';
const pushText = (value, fallback, max) => (typeof value === 'string' && value.length > 0 ? value.slice(0, max) : fallback);
// A path of this site only, the list or a section of it: a payload can never send the tap anywhere else.
const PUSH_URL = /^\/?#\/[^\s\\]{0,300}$/;
const pushUrl = (value) => (typeof value === 'string' && (value === '/' || PUSH_URL.test(value)) ? value : '/');

async function showPush(event) {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = {};
  }
  if (data === null || typeof data !== 'object' || Array.isArray(data)) data = {};
  try {
    await self.registration.showNotification(pushText(data.title, PUSH_TITLE, 100), {
      body: pushText(data.body, PUSH_BODY, 300),
      tag: pushText(data.tag, PUSH_TAG, 100),
      icon: PUSH_ICON,
      data: { url: pushUrl(data.url) },
    });
  } catch {
    // Whatever went wrong, one plain notice still goes out.
    await self.registration.showNotification(PUSH_TITLE, { body: PUSH_BODY, tag: PUSH_TAG, icon: PUSH_ICON, data: { url: '/' } });
  }
}

self.addEventListener('push', (event) => {
  event.waitUntil(showPush(event).catch(() => undefined));
});

async function openApp(url) {
  const hash = url.includes('#') ? url.slice(url.indexOf('#')) : '#/';
  const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
  const page = windows.find((client) => new URL(client.url).origin === self.location.origin);
  // A bare '#/...' would resolve against the worker script (/sw.js#/...): open an absolute path instead.
  if (!page) return self.clients.openWindow(url.startsWith('#') ? '/' + url : url);
  page.postMessage({ type: 'qkp-open', hash });
  try {
    return await page.focus();
  } catch {
    return undefined;
  }
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  event.waitUntil(openApp(pushUrl(event.notification.data && event.notification.data.url)).catch(() => undefined));
});

const keyBytes = (text) => {
  const raw = atob(text.replace(/-/g, '+').replace(/_/g, '/'));
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i += 1) bytes[i] = raw.charCodeAt(i);
  return bytes;
};

// The browser dropped or renewed the subscription: subscribe again and tell the PC (the kinds stay as they are on the PC).
async function resubscribe(event) {
  try {
    let subscription = event.newSubscription;
    if (!subscription) {
      let options = event.oldSubscription && event.oldSubscription.options;
      if (!options || !options.applicationServerKey) {
        const answer = await fetch('/api/push/key', { credentials: 'same-origin', cache: 'no-store' });
        const body = await answer.json();
        options = { userVisibleOnly: true, applicationServerKey: keyBytes(body.publicKey) };
      }
      subscription = await self.registration.pushManager.subscribe(options);
    }
    const { endpoint, keys } = subscription.toJSON();
    await fetch('/api/push/subscription', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'X-Remote': '1' },
      body: JSON.stringify({ endpoint, keys }),
      credentials: 'same-origin',
      cache: 'no-store',
    });
  } catch {
    // Nothing more can be done here: Settings shows "off" and the owner turns notifications on again.
  }
}

self.addEventListener('pushsubscriptionchange', (event) => {
  event.waitUntil(resubscribe(event));
});

// Only the shell is kept: the page itself and files with these extensions, never /api/ and never other origins.
const KEPT = /\.(js|css|png|webmanifest)$/;
// How long the network may take for the shell before the cached copy is shown instead (the network keeps going in the background).
const SHELL_WAIT_MS = 3000;
// This script has no module system and cannot use the page's i18n catalog, so the texts of the page below are plain English constants.
// They are not part of the catalog. The sentence must stay identical to the catalog's `banner.unreachable` (test/sw.test.ts checks it).
const UNREACHABLE = "Can't reach SkuaLink. Check that your PC is on and SkuaLink is running.";
const TRY_AGAIN = 'Try again';
const offline = () => new Response('', { status: 503, statusText: 'Offline' });
// No inline script or style, even though the site's CSP does not apply to a synthesized answer.
const unreachablePage = () =>
  new Response(
    `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SkuaLink</title></head><body><h1>SkuaLink</h1><p>${UNREACHABLE}</p><p><a href="/">${TRY_AGAIN}</a></p></body></html>`,
    {
      status: 503,
      statusText: 'Unreachable',
      headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': "default-src 'none'" },
    },
  );

const TIMED_OUT = Symbol('timed out');

/** What to show when the network gave no usable answer: the cached copy, else the page for a navigation, else a plain failure. */
async function fallback(request) {
  const hit = await caches.match(request);
  if (hit) return hit;
  // A missing script or style must fail as such, not be answered with the page's HTML.
  if (request.destination === 'script' || request.destination === 'style') return offline();
  if (request.mode === 'navigate' || request.destination === 'document') return (await caches.match('/')) || unreachablePage();
  return offline();
}

self.addEventListener('fetch', (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;
  const keep = url.pathname === '/' || KEPT.test(url.pathname);
  // The network request runs to its end even after the cache has answered, so the cache is refreshed for the next visit.
  let stored = Promise.resolve();
  const network = fetch(request).then((response) => {
    if (keep && response.ok && response.type === 'basic') {
      const copy = response.clone();
      // Not awaited: the answer is not delayed by the cache write.
      stored = caches.open(CACHE).then((cache) => cache.put(request, copy)).catch(() => undefined);
    }
    return response;
  });
  event.waitUntil(network.then(() => stored, () => undefined));
  if (!keep) {
    event.respondWith(network.catch(() => fallback(request)));
    return;
  }
  const wait = async () => {
    // The timer only applies where there is something to fall back to: a cached copy, or a navigation (which gets the plain page).
    // Without one, a slow network is waited for as before, so a new version's assets on a slow link still load.
    const canFallBack = request.mode === 'navigate' || request.destination === 'document' || !!(await caches.match(request));
    if (!canFallBack) return network.catch(() => fallback(request));
    let timer;
    const late = new Promise((resolve) => {
      timer = setTimeout(() => resolve(TIMED_OUT), SHELL_WAIT_MS);
    });
    try {
      const result = await Promise.race([network, late]);
      return result === TIMED_OUT ? await fallback(request) : result;
    } catch {
      return fallback(request);
    } finally {
      clearTimeout(timer);
    }
  };
  event.respondWith(wait());
});
