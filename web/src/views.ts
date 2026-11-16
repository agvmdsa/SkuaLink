import { ago, characterRows, factionRowsOf, findCharacter, lastSeen, liveInstance, instanceOf, overviewFacts, type CharacterRowView } from './characters';
import { characterHref, expFraction, formatNumber, hardFarmsView, noticeLine, type Route, type Section } from './state';
import { t } from './i18n';
import { THEMES, type ThemeChoice } from './theme';
import type { Faction, Notice, PhoneCharacter, RemoteState, SkuaInstance } from './types';

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
  /** Draws the Bot section of a character's connected Skua (a chunk of its own, botViews.ts); absent until it has loaded. */
  botSection?: (c: PhoneCharacter, i: SkuaInstance) => VNode | null;
  /** The Bot views' chunk failed to load (it is tried again on the next page change or when back online). */
  botFailed?: boolean;
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

export const empty = (text: string): VNode => h('p', { className: 'empty', text });

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

export function status(kind: string, text: string, tag = 'p'): VNode {
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

/** The Overview's Reputation card (whether it is live is said once, under the character's name). */
function reputationCard(factions: Pick<Faction, 'name' | 'rank' | 'max' | 'fraction' | 'toNext'>[]): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('factions.title') }),
    factions.length === 0 ? empty(t('factions.empty')) : h('ul', { className: 'list' }, ...factions.map(factionRow)),
  );
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

/** Overview and Bot: two links, the current one marked; each is a real page of its own. */
function sectionSwitch(name: string, section: Section): VNode {
  const link = (to: Section, key: 'character.overview' | 'character.bot') =>
    h('a', { className: 'seg', text: t(key), attrs: to === section ? { href: characterHref(name, to), 'aria-current': 'page' } : { href: characterHref(name, to) } });
  return h('nav', { className: 'segmented', attrs: { 'aria-label': t('character.sections') } }, link('overview', 'character.overview'), link('bot', 'character.bot'));
}

export function characterView(model: Model, name: string, section: Section = 'overview'): VNode {
  const c = findCharacter(model.state, name);
  if (!c?.hasCharacter) return h('div', {}, back(), h('section', { className: 'card' }, empty(t('character.notKnown'))));
  return h('div', {}, back(), sectionSwitch(c.name, section), ...(section === 'bot' ? botCards(c, model) : overviewCards(c, model)));
}

function overviewCards(c: PhoneCharacter, model: Model): VNode[] {
  const p = c.profile;
  const seen = c.live ? t('character.live') : lastSeen(c, model.nowMs);
  return [
    h(
      'section',
      { className: 'card' },
      h('h2', { text: c.name }),
      seen ? status(c.live ? 'live' : 'saved', seen) : null,
      p ? h('dl', { className: 'figures' }, ...overviewFacts(p).flatMap((f) => [h('dt', { text: f.label }), h('dd', { text: f.value })])) : empty(t('character.noProfile')),
      p ? bar(t('character.expBar'), expFraction(p)) : null,
    ),
    reputationCard(factionRowsOf(c.factions ?? [])),
    hardFarmsCard(c, model),
  ];
}

/** The Bot section: the bot views of the connected Skua (a loading card until their chunk is in), else the calm not-connected card. */
function botCards(c: PhoneCharacter, model: Model): VNode[] {
  const i = liveInstance(model.state, c);
  if (!i) return [notConnectedCard(c, instanceOf(model.state, c), model.nowMs)];
  const drawn = model.botSection?.(c, i);
  if (!drawn) return [h('section', { className: 'card', attrs: model.botFailed ? {} : { 'aria-busy': 'true' } }, h('h2', { text: c.name }), empty(t(model.botFailed ? 'bot.loadFailed' : 'bot.loading')))];
  // Flattened into the page's grid, so the cards keep its gap.
  return drawn.tag === 'div' ? drawn.children : [drawn];
}

/**
 * No connected Skua playing the character: a Skua that is open but on a version mismatch or not logged in as it says so; otherwise
 * Not connected, with what to do, how old the data is and what the bot did last. Nothing to press.
 */
function notConnectedCard(c: PhoneCharacter, last: SkuaInstance | null, nowMs: number): VNode {
  const when = ago(c.lastSeenUtc, nowMs);
  const st = last?.status;
  const open = last && !last.disconnected && last.connection !== 'NotConnected';
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: c.name }),
    open
      ? h('p', { text: t(last.connection === 'VersionMismatch' ? 'skua.mismatch' : 'bot.notLoggedIn') })
      : h(
          'div',
          {},
          status('saved', t('bot.notConnectedTitle')),
          h('p', { className: 'hint', text: t('bot.notConnectedBody') }),
          when ? h('p', { className: 'muted hint', text: t('bot.lastData', { when }) }) : null,
          st?.map ? h('p', { className: 'muted', text: t('bot.lastState', { script: (st.scriptRunning && st.script) || t('skua.noScript'), map: st.map }) }) : null,
        ),
  );
}

function hardFarmsCard(c: PhoneCharacter, model: Model): VNode {
  const hf = c.hardFarmsState === 'Ready' ? hardFarmsView(c.hardFarms, model.showAll) : null;
  const missingCount = hf ? hf.shown.length + hf.hidden : 0;
  const inventoryAge = ago(c.inventoryAtUtc, model.nowMs);
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('hardFarms.title') }),
    // A Ready result whose numbers were damaged in transit says nothing rather than something untrue.
    hf === null
      ? c.hardFarmsState === 'Ready' ? null : empty(t(c.hardFarmsState === 'NoList' ? 'hardFarms.notLoaded' : 'hardFarms.noInventory'))
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
                // The real count: the PC sends at most 200 names.
                h('summary', { text: t('hardFarms.missing', { count: Math.max(0, hf.total - hf.owned) || missingCount }) }),
                h('ul', { className: 'missing' }, ...hf.shown.map((item) => h('li', { text: item }))),
                // One toggle: Show more while some are hidden, Show fewer once all of a long list are shown.
                hf.hidden > 0 || (model.showAll && missingCount > 20)
                  ? h('button', { text: hf.hidden > 0 ? t('hardFarms.showMore', { count: hf.hidden }) : t('hardFarms.showFewer'), attrs: { type: 'button', 'data-action': 'toggle-all' } })
                  : null,
              ),
          // The notes under the list: a gap above them (.hint), then they read as one block of footnotes.
          c.inventoryTruncated ? h('p', { className: 'hint', text: t('hardFarms.truncated') }) : null,
          inventoryAge ? h('p', { className: 'muted hint', text: t('hardFarms.inventoryAge', { when: inventoryAge }) }) : null,
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
 * A plain link to the one page, shown where the dock goes until its chunk has loaded (or when it cannot load): the page is never
 * left without a way back to Characters. Null where the dock would be hidden too.
 */
export function fallbackNav(route: Route, unpaired: boolean): VNode | null {
  if (unpaired || route.view === 'pair') return null;
  return h('nav', { className: 'fallback-nav', attrs: { 'aria-label': t('dock.label') } }, h('a', { className: 'back', text: t('list.title'), attrs: { href: '#/', 'aria-current': 'page' } }));
}

/** Puts the Appearance footer under a view, inside the same column and grid. */
export function withFooter(view: VNode, model: Pick<Model, 'theme'>): VNode {
  return h('div', {}, ...(view.tag === 'div' ? view.children : [view]), themeFooter(model));
}
