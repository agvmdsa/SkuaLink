import { addMessages, t, type MessageKey } from './i18n';
import { queueEn } from './i18n/queueEn';
import { confirmWords, needsConfirm, parseQueues, queueApi, queueCardModel, queueFor, QueueUi, type QueueWord } from './queue';
import { isLive } from './skua';
import type { SkuaInstance } from './types';
import { h, mount, type VNode } from './views';
import { addRules, pageSheet } from './cssom';
import queueCss from './styles/queue.css?inline';

// The queue card of the Bot section, loaded with it as a chunk of its own: its texts, its rules and its click handling.

const BUTTONS: readonly { word: QueueWord; action: string; label: MessageKey; on(c: ReturnType<typeof queueCardModel>): boolean }[] = [
  { word: 'start', action: 'queue-start', label: 'queue.start', on: (c) => c.start },
  { word: 'pause', action: 'queue-pause', label: 'queue.pause', on: (c) => c.pause },
  { word: 'continue', action: 'queue-continue', label: 'queue.continue', on: (c) => c.continue },
  { word: 'skip', action: 'queue-skip', label: 'queue.skip', on: (c) => c.skip },
  { word: 'stop', action: 'queue-stop', label: 'queue.stop', on: (c) => c.stop },
];

/** The queue card of one Skua's character; null when that Skua is not live or has no character. Names and the PC's reason are text only. */
export function queueCard(i: SkuaInstance, ui: QueueUi, queues: unknown, _nowMs: number): VNode | null {
  const character = i.status?.character;
  if (!isLive(i) || !character) return null;
  const pending = ui.isPending(i.instanceId);
  const c = queueCardModel(queueFor(parseQueues(queues), character), pending, ui.errorOf(i.instanceId));
  return h(
    'section',
    { className: 'card queue-card' },
    h('h2', { text: t('queue.title') }),
    h('p', { className: 'status', text: c.state }),
    c.step ? h('p', { className: 'queue-step', text: c.step }) : null,
    c.progress ? h('p', { className: 'queue-progress', text: c.progress }) : null,
    c.reason ? h('p', { className: 'queue-reason', text: c.reason }) : null,
    c.empty ? h('p', { className: 'muted', text: t('queue.empty') }) : null,
    h(
      'div',
      { className: 'queue-actions' },
      ...BUTTONS.map((b) => h('button', { text: t(b.label), attrs: { type: 'button', 'data-action': b.action, ...(b.on(c) ? {} : { disabled: '' }), ...(pending ? { 'aria-busy': 'true' } : {}) } })),
      h('button', { text: t('queue.edit'), attrs: { type: 'button', 'data-action': 'queue-edit', ...(c.edit ? {} : { disabled: '' }) } }),
    ),
    pending ? h('p', { className: 'muted', text: t('queue.working'), attrs: { role: 'status' } }) : null,
    c.error ? h('p', { className: 'queue-error', text: c.error, attrs: { role: 'alert' } }) : null,
  );
}

const WORDS: Record<string, QueueWord> = {
  'queue-start': 'start',
  'queue-pause': 'pause',
  'queue-continue': 'continue',
  'queue-skip': 'skip',
  'queue-stop': 'stop',
};

/** The confirmation sheet, in the style of the Stop sheet of the Control card (the same classes); names go in as text only. */
export function confirmView(words: { title: string; body: string; confirm: string }): VNode {
  return h(
    'div',
    { className: 'sheet-body' },
    h('h2', { text: words.title, attrs: { id: 'queue-confirm-title', tabindex: '-1' } }),
    h('p', { className: 'muted', text: words.body }),
    h('button', { className: 'btn-danger', text: words.confirm, attrs: { type: 'button', 'data-action': 'queue-confirm' } }),
    h('button', { className: 'btn-quiet', text: t('control.cancel'), attrs: { type: 'button', 'data-action': 'queue-cancel', autofocus: '' } }),
  );
}

/** Opens the sheet in a dialog of its own; Confirm runs `go` once, Cancel, Esc and a tap outside only close it. */
let confirmOpen = false; // two quick taps must not stack two sheets

function openConfirm(words: { title: string; body: string; confirm: string }, go: () => void): void {
  if (confirmOpen) return;
  confirmOpen = true;
  const dialog = document.createElement('dialog');
  dialog.className = 'sheet';
  dialog.setAttribute('aria-labelledby', 'queue-confirm-title');
  dialog.replaceChildren(mount(confirmView(words)));
  document.body.append(dialog);
  const close = (): void => {
    confirmOpen = false;
    if (dialog.open) dialog.close();
    dialog.remove();
  };
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) return close();
    const action = (event.target as HTMLElement).closest<HTMLElement>('[data-action]')?.getAttribute('data-action');
    if (action === 'queue-confirm') {
      close();
      go();
    } else if (action === 'queue-cancel') close();
  });
  dialog.addEventListener('cancel', (event) => {
    event.preventDefault();
    close();
  });
  try {
    dialog.showModal();
  } catch {
    dialog.setAttribute('open', '');
  }
}

/** Adds the card's rules to the page; called once, when this chunk has loaded. */
export const addQueueRules = (): void => addRules(pageSheet(), queueCss);

/** Registers the card's texts and the one delegated click listener; returns the object that runs the commands. */
export function startQueue(
  app: HTMLElement,
  deps: { instance(): SkuaInstance | null; queues(): unknown; redraw(): void; onUnpaired(): void; confirm?: (words: { title: string; body: string; confirm: string }, go: () => void) => void },
): QueueUi {
  addMessages(queueEn);
  const ui = new QueueUi(queueApi, deps.redraw, deps.onUnpaired);
  app.addEventListener('click', (event) => {
    const button = (event.target as HTMLElement).closest<HTMLElement>('[data-action^="queue-"]');
    const action = button?.getAttribute('data-action') ?? '';
    const i = deps.instance();
    if (!button || !i) return;
    if (Object.hasOwn(WORDS, action)) {
      const word = WORDS[action]!;
      const instanceId = i.instanceId;
      const go = (): void => void ui.run(instanceId, word);
      if (!needsConfirm(word)) return go();
      const character = i.status?.character ?? '';
      (deps.confirm ?? openConfirm)(confirmWords(word, queueFor(parseQueues(deps.queues()), character), character), go);
    } else if (action === 'queue-edit') {
      const instanceId = i.instanceId;
      const queue = queueFor(parseQueues(deps.queues()), i.status?.character ?? null);
      import('./queueEdit')
        .then((m) => m.openQueueEditor({ instanceId, queue, onSaved: deps.redraw, onUnpaired: deps.onUnpaired }))
        .catch(() => ui.setError(instanceId, t('control.error.generic')));
    }
  });
  return ui;
}
