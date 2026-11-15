import { characterRows, factionRowsOf, findCharacter, lastSeen, overviewFacts, TONE_KIND, type CharacterRowView } from './characters';
import { characterHref, expFraction, formatNumber, hardFarmsView, noticeLine, relativeTime, type Route, type Section } from './state';
import { plural, t, type MessageKey } from './i18n';
import { factionRows, instanceState, LOG_FILTERS, LOG_FILTER_LABELS, questRows, reputationHome, reputationSummary, selectInstance, skuaEmptyText, skuaLogRows, skuaNow, skuaSummary, type LogFilter } from './skua';
import { THEMES, type ThemeChoice } from './theme';
import type { Faction, Notice, PhoneCharacter, RemoteState, SkuaInstance, SkuaState } from './types';

export interface VNode {
  tag: string;
  className?: string;
  text?: string;
  attrs?: Record<string, string>;
  fraction?: number;
  children: VNode[];
}

export interface Model {
  state: RemoteState | null;
  nowMs: number;
  showAll: boolean;
  hfOpen: boolean;
  /** How to install the page on an iPhone or iPad, or null. */
  installHint: string | null;
  /** The stored appearance choice; System when absent. */
  theme?: ThemeChoice;
  /** The selected Skua log filter; Everything when absent. */
  skuaFilter?: LogFilter;
  /** The label (character name) of the Skua last chosen, or null; the first connected one when absent or gone. */
  skuaInstance?: string | null;
  /** The instance chosen in this session; it wins over the label. */
  skuaInstanceId?: string | null;
  /** Draws the Control card of an instance (loaded with the Skua tab); without it the tab shows no Control card. */
  controlCard?: (i: SkuaInstance) => VNode | null;
  /** The section last opened for a character; the Overview when absent. */
  sectionOf?: (name: string) => Section;
}

type Child = VNode | null | false;

export function h(tag: string, props: Omit<Partial<VNode>, 'tag' | 'children'> = {}, ...children: Child[]): VNode {
  return { tag, ...props, children: children.filter((c): c is VNode => Boolean(c)) };
}

const SVG = new Set(['svg', 'path']);

export function mount(node: VNode): HTMLElement {
  // The dock's icons are SVG: they need the SVG namespace, and their class is set as an attribute.
  const el = SVG.has(node.tag) ? (document.createElementNS('http://www.w3.org/2000/svg', node.tag) as unknown as HTMLElement) : document.createElement(node.tag);
  if (node.className) el.setAttribute('class', node.className);
  if (node.text !== undefined) el.textContent = node.text;
  for (const [name, value] of Object.entries(node.attrs ?? {})) {
    if (name === 'style' || name.startsWith('on')) throw new Error(`The attribute ${name} is not allowed.`);
    el.setAttribute(name, value);
  }
  if (node.fraction !== undefined) el.style.setProperty('--fraction', String(node.fraction));
  for (const child of node.children) el.append(mount(child));
  return el;
}

const back = (): VNode => h('a', { className: 'back', text: t('common.back'), attrs: { href: '#/' } });

const empty = (text: string): VNode => h('p', { className: 'empty', text });

export function bar(label: string, fraction: number): VNode {
  return h(
    'div',
    {
      className: 'bar',
      attrs: { role: 'progressbar', 'aria-label': label, 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(Math.round(fraction * 100)) },
    },
    h('div', { className: 'bar-fill', fraction }),
  );
}

function status(kind: string, text: string, tag = 'p'): VNode {
  return h(tag, { className: 'status' }, h('span', { className: 'dot', attrs: { 'data-kind': kind, 'aria-hidden': 'true' } }), h('span', { text }));
}


export function listView(model: Model): VNode {
  const rows = characterRows(model.state, model.nowMs, model.sectionOf);
  const notices = model.state?.notices ?? [];
  return h(
    'div',
    {},
    h(
      'section',
      { className: 'card' },
      h('h2', { text: t('list.title') }),
      rows.length === 0 ? empty(t('list.empty')) : h('ul', { className: 'list' }, ...rows.map(characterRowNode)),
    ),
    model.state?.skua ? skuaLinkCard(model.state.skua) : null, // removed in Task 8
    noticesCard(notices, model.nowMs),
    model.installHint ? h('p', { className: 'note', text: model.installHint }) : null,
  );
}

function characterRowNode(r: CharacterRowView): VNode {
  const inner = [
    h('span', { className: 'title' }, h('span', { className: 'name', text: r.name }), r.level ? h('span', { className: 'level', text: r.level }) : null),
    r.gold ? h('span', { className: 'gold', text: r.gold }) : null,
    h('span', { className: 'row-status' }, status(r.badge.kind, r.badge.text, 'span'), r.age ? h('span', { className: 'muted', text: r.age }) : null),
  ];
  // A Skua with nobody logged in has no character to open: the same row, as plain text.
  return h('li', {}, h(r.href ? 'a' : 'div', { className: 'row', attrs: r.href ? { href: r.href } : {} }, ...inner));
}

function skuaLinkCard(skua: SkuaState): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('skua.title') }),
    h('a', { className: 'row', attrs: { href: '#/skua' } }, h('span', { className: 'name', text: skuaSummary(skua) })),
  );
}

function noticesCard(notices: Notice[], nowMs: number): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('list.notices.title') }),
    notices.length === 0
      ? empty(t('list.notices.empty'))
      : h(
          'ul',
          { className: 'notices' },
          ...notices.slice(0, 10).map((n) => {
            const line = noticeLine(n, nowMs);
            return h(
              'li',
              {},
              h('p', { className: 'notice-title', text: line.title }),
              h('p', { text: line.body }),
              h('p', { className: 'muted', text: n.character ? t('list.notices.when', { character: n.character, when: line.when }) : line.when }),
            );
          }),
        ),
  );
}

function nowCard(skua: SkuaState, nowMs: number): VNode | null {
  const now = skuaNow(skua, nowMs);
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

function questsCard(skua: SkuaState): VNode | null {
  const quests = questRows(skua);
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

/** The one way a reputation list is drawn, on the Overview and (for a character SkuaLink does not know) on the Skua tab. */
function reputationCard(factions: Pick<Faction, 'name' | 'rank' | 'max' | 'fraction' | 'toNext'>[], note: string | null = null): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('factions.title') }),
    note ? h('p', { className: 'muted', text: note }) : null,
    factions.length === 0 ? empty(t('factions.empty')) : h('ul', { className: 'list' }, ...factions.map(factionRow)),
  );
}

/**
 * The Skua tab's reputation: a one-line link to the character's Overview when SkuaLink knows the character, else the full list
 * here. Hidden when the plugin does not report reputation.
 */
function skuaReputation(skua: SkuaState, state: RemoteState | null): VNode | null {
  const rows = factionRows(skua);
  if (!rows) return null;
  const home = reputationHome(state, skua);
  if (home === null) return reputationCard(rows);
  // What the Overview shows: the character's list, live or last known.
  const sum = reputationSummary(factionRowsOf(findCharacter(state, home)?.factions ?? []));
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('factions.title') }),
    h(
      'a',
      { className: 'row', attrs: { href: characterHref(home) } },
      h('span', { className: 'name', text: plural('factions.count', sum.count, { character: home }) }),
      h(
        'span',
        { className: 'row-status' },
        h('span', {
          className: 'muted',
          text: sum.closest ? t('factions.closest', { name: sum.closest.name, n: formatNumber(sum.closest.toNext) }) : t(sum.count ? 'factions.allMax' : 'factions.empty'),
        }),
      ),
    ),
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

function logCard(skua: SkuaState, model: Model): VNode {
  const filter = model.skuaFilter ?? 'all';
  const rows = skuaLogRows(skua, model.nowMs, filter);
  const emptyText = skuaEmptyText(skua, filter);
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

const MANY_INSTANCES = 5;

function instanceLabel(i: SkuaInstance, selected: boolean, role: 'chip' | 'list'): VNode {
  const st = instanceState(i);
  const common = { 'data-action': 'skua-instance', 'data-value': i.instanceId, type: 'button' };
  const inner = [
    h('span', { className: 'chip-name', text: i.label }),
    h('span', { className: 'chip-state' }, h('span', { className: 'dot', attrs: { 'data-kind': TONE_KIND[st.tone], 'aria-hidden': 'true' } }), h('span', { text: st.text })),
  ];
  const off = i.disconnected || i.connection === 'NotConnected';
  if (role === 'list') {
    return h('li', {}, h('button', { className: off ? ['inst-btn', 'off'].join(' ') : 'inst-btn', attrs: { ...common, 'aria-pressed': String(selected) } }, ...inner));
  }
  return h('button', { className: off ? ['chip', 'off'].join(' ') : 'chip', attrs: { ...common, role: 'tab', 'aria-selected': String(selected), tabindex: selected ? '0' : '-1' } }, ...inner);
}

/** Which Skua the tab shows: nothing for one, a chip row for two to five, a list of buttons above that. */
function instanceSelector(instances: SkuaInstance[], selected: SkuaInstance): VNode | null {
  if (instances.length < 2) return null;
  if (instances.length > MANY_INSTANCES) {
    return h('ul', { className: 'inst-list', attrs: { 'aria-label': t('skua.instances.label') } }, ...instances.map((i) => instanceLabel(i, i === selected, 'list')));
  }
  return h(
    'div',
    { className: 'chip-wrap' },
    h('div', { className: 'chips', attrs: { role: 'tablist', 'aria-label': t('skua.instances.label') } }, ...instances.map((i) => instanceLabel(i, i === selected, 'chip'))),
  );
}

export function skuaView(model: Model): VNode {
  const instances = model.state?.skuaInstances ?? [];
  const chosen = selectInstance(instances, model.skuaInstance ?? null, model.skuaInstanceId ?? null);
  // An older app sends no list: the single Skua is shown as before, with no selector.
  const skua: SkuaState | null = chosen ?? model.state?.skua ?? null;
  const selector = chosen ? instanceSelector(instances, chosen) : null;
  // A disconnected instance keeps its last status, which must not be shown as live.
  const live = skua && chosen?.disconnected ? { ...skua, connection: 'NotConnected' as const } : skua;
  if (!live || live.connection !== 'Connected') {
    return h('div', {}, selector, h('section', { className: 'card' }, h('h2', { text: t('skua.title') }), empty(skuaEmptyText(live))));
  }
  const control = chosen && model.controlCard ? model.controlCard(chosen) : null;
  return h('div', {}, selector, nowCard(live, model.nowMs), control, questsCard(live), skuaReputation(live, model.state), logCard(live, model));
}

/** One faction row (FactionRow; the capture's Faction carries the same fields). */
function factionRow(f: Pick<Faction, 'name' | 'rank' | 'max' | 'fraction' | 'toNext'>): VNode {
  return h(
    'li',
    {},
    h('p', { className: 'faction' }, h('span', { className: 'name', text: f.name }), h('span', { className: 'muted', text: f.max ? t('factions.maxRank') : t('factions.rank', { rank: f.rank }) })),
    bar(f.name, f.fraction),
    f.max ? null : h('p', { className: 'muted', text: t('factions.toNext', { n: formatNumber(f.toNext) }) }),
  );
}

export function characterView(model: Model, name: string): VNode {
  const c = findCharacter(model.state, name);
  if (!c?.hasCharacter) return h('div', {}, back(), h('section', { className: 'card' }, empty(t('character.notKnown'))));
  const p = c.profile;
  const seen = c.live ? t('character.live') : lastSeen(c, model.nowMs);
  return h(
    'div',
    {},
    back(),
    h(
      'section',
      { className: 'card' },
      h('h2', { text: c.name }),
      seen ? status(c.live ? 'live' : 'saved', seen) : null,
      p ? h('dl', { className: 'figures' }, ...overviewFacts(p).flatMap((f) => [h('dt', { text: f.label }), h('dd', { text: f.value })])) : empty(t('character.noProfile')),
      p ? bar(t('character.expBar'), expFraction(p)) : null,
    ),
    reputationCard(factionRowsOf(c.factions ?? []), c.factionsLive ? t('factions.fromSkua') : null),
    hardFarmsCard(c, model),
  );
}

function hardFarmsCard(c: PhoneCharacter, model: Model): VNode {
  const hf = c.hardFarmsState === 'Ready' ? hardFarmsView(c.hardFarms, model.showAll) : null;
  const missingCount = hf ? hf.shown.length + hf.hidden : 0;
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('hardFarms.title') }),
    hf === null
      ? empty(t(c.hardFarmsState === 'NoList' ? 'hardFarms.notLoaded' : 'hardFarms.noInventory'))
      : h(
          'div',
          {},
          h('p', { text: t('hardFarms.owned', { owned: hf.owned, total: hf.total }) }),
          bar(t('hardFarms.bar'), hf.fraction),
          missingCount === 0
            ? null
            : h(
                'details',
                { attrs: model.hfOpen ? { open: '' } : {} },
                h('summary', { text: t('hardFarms.missing', { count: missingCount }) }),
                h('ul', { className: 'missing' }, ...hf.shown.map((item) => h('li', { text: item }))),
                // One toggle: Show more while some are hidden, Show fewer once all of a long list are shown.
                hf.hidden > 0 || (model.showAll && missingCount > 20)
                  ? h('button', { text: hf.hidden > 0 ? t('hardFarms.showMore', { count: hf.hidden }) : t('hardFarms.showFewer'), attrs: { type: 'button', 'data-action': 'toggle-all' } })
                  : null,
              ),
          // The notes under the list: a gap above them (.hint), then they read as one block of footnotes.
          c.inventoryTruncated ? h('p', { className: 'hint', text: t('hardFarms.truncated') }) : null,
          c.inventoryAtUtc ? h('p', { className: 'muted hint', text: t('hardFarms.inventoryAge', { when: relativeTime(c.inventoryAtUtc, model.nowMs) }) }) : null,
          h('p', { className: 'muted', text: t('hardFarms.bankHint') }),
        ),
    h('p', { className: 'muted', text: t('hardFarms.source') }),
  );
}

export function pairNeededView(reason: string | null = null, installHint: string | null = null): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('pair.title') }),
    reason ? h('p', { className: 'reason', text: reason, attrs: { role: 'status' } }) : null,
    h('p', {
      text: t('pair.steps'),
    }),
    h(
      'div',
      { className: 'paste' },
      h('p', {
        className: 'muted',
        text: t('pair.pasteHelp'),
      }),
      h('label', { text: t('pair.linkLabel'), attrs: { for: 'pair-link' } }),
      h('input', { attrs: { id: 'pair-link', type: 'text', autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'go' } }),
      h('button', { text: t('pair.button'), attrs: { type: 'button', 'data-action': 'pair-paste' } }),
    ),
    installHint ? h('p', { className: 'muted hint', text: installHint }) : null,
  );
}

export function drawFailedView(): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('failed.title') }),
    h('p', { className: 'muted', text: t('failed.body') }),
  );
}

/** The Appearance control: three toggle buttons, exactly one pressed. A footer on the Not paired screen, a group in the dock's Settings panel. */
export function themeFooter(model: Pick<Model, 'theme'>, tag = 'footer'): VNode {
  const current = model.theme ?? 'system';
  return h(
    tag,
    { className: 'appearance', attrs: { role: 'group', 'aria-labelledby': 'appearance-label' } },
    h('p', { className: 'muted', text: t('theme.group'), attrs: { id: 'appearance-label' } }),
    h(
      'div',
      { className: 'appearance-row' },
      ...THEMES.map((choice) =>
        h('button', {
          className: 'theme-btn',
          text: t(`theme.${choice}`),
          attrs: { type: 'button', 'data-action': 'theme', 'data-value': choice, 'aria-pressed': String(choice === current) },
        }),
      ),
    ),
  );
}

/**
 * Plain links to the two pages, shown where the dock goes until its chunk has loaded (or when it cannot load): the page is never
 * left without a way to Skua and back. Null where the dock would be hidden too.
 */
export function fallbackNav(route: Route, unpaired: boolean): VNode | null {
  if (unpaired || route.view === 'pair') return null;
  const link = (href: string, key: MessageKey, current: boolean) => h('a', { className: 'back', text: t(key), attrs: current ? { href, 'aria-current': 'page' } : { href } });
  const skua = route.view === 'skua';
  return h('nav', { className: 'fallback-nav', attrs: { 'aria-label': t('dock.label') } }, link('#/', 'list.title', !skua), link('#/skua', 'skua.title', skua));
}

/** Puts the Appearance footer under a view, inside the same column and grid. */
export function withFooter(view: VNode, model: Pick<Model, 'theme'>): VNode {
  return h('div', {}, ...(view.tag === 'div' ? view.children : [view]), themeFooter(model));
}
