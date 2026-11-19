import { t } from './i18n';
import { boostApi, boostFor, BOOST_TYPES, parseBoosts, rowText, type BoostItem } from './boosts';
import type { QueueUi } from './queue';
import { isLive } from './skua';
import type { SkuaInstance } from './types';
import { h, type VNode } from './views';

// The Boosts card of the Bot section. It ships inside the queue chunk (queueViews.ts imports it), so it shares that chunk's
// confirmation sheet, command runner and catalog, and adds no file of its own.

let seen = 0; // when a counting card was last drawn (0: none): the timer lives only while the card keeps being drawn
let tick: ReturnType<typeof setInterval> | undefined;
let redrawCard: (() => void) | undefined;

const item = (i: BoostItem): VNode =>
  h(
    'div',
    { className: 'queue-row' },
    h('p', { text: t('boost.item', { name: i.name, n: i.quantity }) }),
    h('button', { text: t('boost.use'), attrs: { type: 'button', 'data-action': 'boost-use', 'data-item': String(i.itemId), ...(i.canUse ? {} : { disabled: '' }) } }),
  );

export function boostCard(i: SkuaInstance, ui: QueueUi, boosts: unknown, nowMs: number): VNode | null {
  const b = boostFor(parseBoosts(boosts), i.instanceId);
  if (!b || !isLive(i)) return null;
  if (b.online && b.readAt > 0 && BOOST_TYPES.some((type) => b.left[type] > 0)) {
    seen = Date.now();
    start();
  }
  const error = ui.errorOf(i.instanceId);
  return h(
    'section',
    { className: 'card queue-card' },
    h('h2', { text: t('boost.title') }),
    ...BOOST_TYPES.map((type) => h('p', { text: rowText(b, type, nowMs) })),
    ...b.items.map(item),
    b.canRelogin ? h('div', { className: 'queue-actions' }, h('button', { text: t('boost.relogin'), attrs: { type: 'button', 'data-action': 'boost-relogin' } })) : null,
    ui.isPending(i.instanceId) ? h('p', { className: 'muted', text: t('queue.working'), attrs: { role: 'status' } }) : null,
    error ? h('p', { className: 'queue-error', text: error, attrs: { role: 'alert' } }) : null,
  );
}

/** `boosts` may be the state itself or a function giving the current one (the sheet re-reads it when Confirm is tapped). */
export function boostTap(
  action: string,
  itemId: string | null,
  i: SkuaInstance,
  ui: QueueUi,
  boosts: unknown,
  confirm: (words: { title: string; body: string; confirm: string }, go: () => void) => void,
): void {
  const id = i.instanceId;
  if (action === 'boost-relogin') return void ui.call(id, () => boostApi.relogin(id), 'boost.refused');
  const wanted = Number(itemId);
  const find = (): BoostItem | undefined => boostFor(parseBoosts(typeof boosts === 'function' ? boosts() : boosts), id)?.items.find((y) => y.itemId === wanted && y.canUse);
  const x = find();
  // Confirm sends the id captured now, and only while that boost is still in the list and usable.
  if (x) confirm({ title: t('boost.useTitle', { name: x.name, n: x.quantity }), body: '', confirm: t('boost.use') }, () => find() && void ui.call(id, () => boostApi.use(id, x.itemId), 'boost.refused'));
}

function start(): void {
  if (tick !== undefined || !redrawCard) return;
  tick = setInterval(() => {
    if (Date.now() - seen > 2500) {
      clearInterval(tick);
      tick = undefined;
    } else if (!document.hidden) redrawCard?.();
  }, 1000);
}

/** Redraws once a second while a boost is counting down and the page is visible; one timer, gone when the card is no longer drawn. */
export function startBoostTick(redraw: () => void): void {
  redrawCard = redraw;
  if (seen) start();
}
