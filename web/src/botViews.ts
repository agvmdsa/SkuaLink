import { LOG_FILTERS, LOG_FILTER_LABELS, questRows, skuaEmptyText, skuaLogRows, skuaNow, type LogFilter } from './bot';
import { plural, t } from './i18n';
import type { SkuaInstance } from './types';
import { empty, h, status, type VNode } from './views';

// The Bot section of a character (what the Skua tab showed): a chunk of its own, loaded when a Bot section is opened. It never
// imports the control chunk: the Control card is passed in, so the two chunks stay apart.

export { parseLogFilter } from './bot';

function nowCard(i: SkuaInstance, nowMs: number): VNode | null {
  const now = skuaNow(i, nowMs);
  if (!now) return null;
  return h(
    'section',
    { className: 'card now-card' },
    h('h2', { text: t('skua.now.title') }),
    now.loggedIn ? (now.character ? h('p', { className: 'now-name', text: now.character }) : null) : h('p', { className: 'skua-line', text: t('skua.notLoggedIn') }),
    now.where ? h('p', { className: 'skua-line', text: now.where }) : null,
    status(now.script.running ? 'live' : 'idle', now.script.text),
    now.script.name ? h('p', { className: 'skua-line', text: now.script.name }) : null,
  );
}

function questsCard(i: SkuaInstance): VNode | null {
  const quests = questRows(i);
  if (!quests) return null;
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: plural('skua.quests.title', quests.length) }),
    quests.length === 0
      ? empty(t('skua.quests.none'))
      : h('ul', { className: 'quests' }, ...quests.map((q) => h('li', {}, h('p', { className: 'name', text: q.name }), q.ready ? status('ready', t('skua.quests.ready')) : null))),
  );
}

function filterRow(selected: LogFilter): VNode {
  return h(
    'div',
    { className: 'filters', attrs: { role: 'group', 'aria-label': t('skua.filter.group') } },
    ...LOG_FILTERS.map((filter) =>
      h('button', {
        className: 'filter-btn',
        text: t(LOG_FILTER_LABELS[filter]),
        attrs: { type: 'button', 'data-action': 'skua-filter', 'data-value': filter, 'aria-pressed': String(filter === selected) },
      }),
    ),
  );
}

function logCard(i: SkuaInstance, nowMs: number, filter: LogFilter): VNode {
  const rows = skuaLogRows(i, nowMs, filter);
  const emptyText = skuaEmptyText(i, filter);
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('skua.log.title') }),
    filterRow(filter),
    emptyText
      ? empty(emptyText)
      : h('ul', { className: 'skua-log' }, ...rows.map((r) => h('li', {}, h('p', { text: r.text }), r.when ? h('p', { className: 'muted', text: r.when }) : null))),
  );
}

/** The Bot section of a connected Skua: Now, Control (once the control chunk is in), Active quests and Log. */
export function botSection(i: SkuaInstance, model: { nowMs: number; skuaFilter: LogFilter; controlCard?: (i: SkuaInstance) => VNode | null }): VNode {
  return h('div', {}, nowCard(i, model.nowMs), model.controlCard?.(i) ?? null, questsCard(i), logCard(i, model.nowMs, model.skuaFilter));
}
