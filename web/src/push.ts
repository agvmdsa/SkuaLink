import { send } from './api';

/** The kinds the PC can push, in the order of the PC's PushKinds.All. */
export const PUSH_KINDS = ['ScriptStopped', 'ScriptError', 'SkuaDisconnected', 'CharacterKicked', 'SkuaOptionsWaiting', 'RankUp', 'HardFarmDrop', 'NoProgress', 'DyingOften', 'UpdateAvailable'] as const;
export type PushKind = (typeof PUSH_KINDS)[number];

export interface PushState {
  subscribed: boolean;
  kinds: PushKind[];
  lastError: string | null;
}

export type PushSupport = 'ready' | 'needs-home-screen' | 'unsupported' | 'denied';

export interface PushEnv {
  ios: boolean;
  standalone: boolean;
  hasServiceWorker: boolean;
  hasPushManager: boolean;
  hasNotification: boolean;
  permission: NotificationPermission | 'unsupported';
}

/** iPhone pages outside the Home Screen cannot subscribe (iOS 16.4 and later, installed pages only); then missing APIs; then a blocked permission. */
export function pushSupport(env: PushEnv): PushSupport {
  if (env.ios && !env.standalone) return 'needs-home-screen';
  if (!env.hasServiceWorker || !env.hasPushManager || !env.hasNotification) return 'unsupported';
  return env.permission === 'denied' ? 'denied' : 'ready';
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const known = (v: unknown): v is PushKind => typeof v === 'string' && (PUSH_KINDS as readonly string[]).includes(v);
const MAX_ERROR = 200;

export function parsePushState(data: unknown): PushState | null {
  if (!isObject(data) || typeof data.subscribed !== 'boolean' || !Array.isArray(data.kinds)) return null;
  const on = new Set(data.kinds.filter(known));
  const lastError = typeof data.lastError === 'string' ? data.lastError.slice(0, MAX_ERROR) : null;
  return { subscribed: data.subscribed, kinds: PUSH_KINDS.filter((k) => on.has(k)), lastError };
}

/** The bytes of a base64url text (the PC's public key). Throws on anything else. */
export function keyBytes(text: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/.test(text)) throw new Error('base64url');
  const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4);
  return Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
}

export interface CallResult {
  status: number;
  data: unknown;
}

async function call(method: string, path: string, body?: unknown): Promise<CallResult> {
  try {
    const response = await send(path, body === undefined ? { method } : { method, body: JSON.stringify(body) });
    let data: unknown = null;
    try {
      data = await response.json();
    } catch {
      // An empty or non-JSON body is no data.
    }
    return { status: response.status, data };
  } catch {
    return { status: 0, data: null };
  }
}

/** The push routes of the PC (`/api/push/*`). */
export const pushApi = {
  state: () => call('GET', '/api/push/state'),
  key: () => call('GET', '/api/push/key'),
  subscribe: (sub: { endpoint: string; p256dh: string; auth: string }) =>
    call('PUT', '/api/push/subscription', { endpoint: sub.endpoint, keys: { p256dh: sub.p256dh, auth: sub.auth } }),
  unsubscribe: () => call('DELETE', '/api/push/subscription'),
  setKinds: (kinds: PushKind[]) => call('PUT', '/api/push/kinds', { kinds }),
  test: () => call('POST', '/api/push/test', {}),
};

export type PushApi = typeof pushApi;
