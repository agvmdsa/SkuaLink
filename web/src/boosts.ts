import { skuaCall, skuaPath } from './api';
import { t, type MessageKey } from './i18n';
import { cut, isObject } from './skua';

// The boosts of the PC's state as the page sees them, and the two commands. Loaded with the queue card (queueViews.ts).

export const BOOST_TYPES = ['gold', 'class', 'rep', 'xp'] as const;
export type BoostType = (typeof BOOST_TYPES)[number];
const NAMES: Record<BoostType, MessageKey> = { gold: 'character.gold', class: 'character.class', rep: 'factions.title', xp: 'boost.xp' };

export interface BoostItem {
  itemId: number;
  name: string;
  quantity: number;
  canUse: boolean;
}

/** `left` holds seconds as of `readAt`: 0 inactive, -1 active with the time unknown. */
export interface BoostModel {
  instanceId: string;
  online: boolean;
  readAt: number;
  left: Record<BoostType, number>;
  items: BoostItem[];
  canRelogin: boolean;
}

/** A whole number from `min` up to 2^53 - 1 (a unix time in ms is far above the int32 that count() allows), or null. */
export const safeInt = (v: unknown, min = 0): number | null => (typeof v === 'number' && Number.isSafeInteger(v) && v >= min ? v : null);

/** The boosts of the state: at most 8 Skua and 12 items each; a damaged entry is left out on its own; anything but an array is none. */
export function parseBoosts(v: unknown): BoostModel[] {
  const out: BoostModel[] = [];
  for (const b of Array.isArray(v) ? (v as unknown[]).slice(0, 8) : []) {
    if (!isObject(b) || typeof b.instanceId !== 'string') continue;
    const items: BoostItem[] = [];
    for (const x of Array.isArray(b.items) ? (b.items as unknown[]).slice(0, 12) : []) {
      const itemId = isObject(x) ? safeInt(x.itemId, 1) : null;
      const quantity = isObject(x) ? safeInt(x.quantity) : null;
      if (isObject(x) && itemId !== null && quantity !== null && typeof x.name === 'string') items.push({ itemId, name: cut(x.name), quantity, canUse: x.canUse === true });
    }
    const left = (k: string): number => safeInt(b[k], -1) ?? 0;
    out.push({ instanceId: cut(b.instanceId), online: b.online === true, readAt: safeInt(b.readAt) ?? 0, left: { gold: left('gold'), class: left('class'), rep: left('rep'), xp: left('xp') }, items, canRelogin: b.canRelogin === true });
  }
  return out;
}

export const boostFor = (all: readonly BoostModel[], instanceId: string): BoostModel | null => all.find((b) => b.instanceId === instanceId) ?? null;

/** The row of one type: inactive, active without a time, or the minutes left (counted down from `readAt` while the character is online). */
export function rowText(b: BoostModel, type: BoostType, nowMs: number): string {
  const name = t(NAMES[type]);
  const v = b.left[type];
  const seconds = b.online && Number.isFinite(nowMs) ? v - (nowMs - b.readAt) / 1000 : v;
  return v === 0 ? t('boost.off', { name }) : v < 0 ? t('boost.on', { name }) : t('boost.left', { name, n: Math.max(0, Math.ceil(seconds / 60)) });
}

export const boostApi = {
  use: (instanceId: string, itemId: number) => skuaCall(skuaPath(instanceId, 'boosts', 'use'), { itemId }),
  relogin: (instanceId: string) => skuaCall(skuaPath(instanceId, 'relogin'), {}),
};
