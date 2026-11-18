import { skuaApi, type CallResult } from './api';
import { parseScripts, searchScripts, type ScriptItem } from './control';
import { addMessages, t, type MessageKey } from './i18n';
import { queueEditEn } from './i18n/queueEditEn';
import { queueApi, type QueueModel, type QueueStep } from './queue';
import { cut, isObject } from './skua';
import { h, mount, type VNode } from './views';

// The queue editor of the phone page: a chunk of its own, loaded when the owner taps "Edit queue". The class has no DOM (the tests drive
// it directly); editorView draws it and openQueueEditor owns the dialog. Typing and picking never redraw: the page redraws only on
// add, remove, move, load, search and save, so a field is never written back into state that draws it again.

/** What the PUT sends for a step. An empty id is a new step (the PC makes the id); the PC keeps a step's reputation goal and names the script. */
export interface QueueDraft {
  id: string;
  scriptId: string;
  goalItem: string;
  goalQuantity: number;
  maxMinutes: number | null;
}

/** One step as the fields show it: numbers stay text until they are saved. */
export interface DraftRow {
  id: string;
  scriptId: string;
  goalItem: string;
  quantity: string;
  minutes: string;
}

const MAX_ROWS = 20;
const WHOLE = /^[0-9]{1,7}$/;
const ITEM_LENGTH = 80;

type Failure = 'script' | 'item' | 'quantity' | 'minutes';

const FAILURE: Record<Failure, MessageKey> = {
  script: 'queueEdit.error.script',
  item: 'queueEdit.error.item',
  quantity: 'queueEdit.error.quantity',
  minutes: 'queueEdit.error.minutes',
};

function toDraft(r: DraftRow): QueueDraft | Failure {
  if (r.scriptId === '') return 'script';
  const goalItem = r.goalItem.trim();
  if (goalItem === '') return 'item';
  const quantity = WHOLE.test(r.quantity.trim()) ? Number(r.quantity.trim()) : 0;
  if (quantity < 1 || quantity > 1_000_000) return 'quantity';
  const text = r.minutes.trim();
  if (text !== '' && (!WHOLE.test(text) || Number(text) < 1 || Number(text) > 1440)) return 'minutes';
  return { id: r.id, scriptId: r.scriptId, goalItem, goalQuantity: quantity, maxMinutes: text === '' ? null : Number(text) };
}

export interface EditorDeps {
  instanceId: string;
  steps: readonly QueueStep[];
  listScripts(): Promise<CallResult>;
  replace(steps: QueueDraft[]): Promise<CallResult>;
  onSaved(): void;
  onUnpaired(): void;
  onChange(): void;
}

export class QueueEditor {
  rows: DraftRow[];
  scripts: ScriptItem[] | null = null;
  query = '';
  busy = false;
  error: string | null = null;
  /** The name each stored step had, to show a script the plugin no longer lists. */
  readonly names = new Map<string, string>();
  readonly reputationNote: boolean;

  constructor(private readonly deps: EditorDeps) {
    this.rows = deps.steps.map((s) => {
      this.names.set(s.scriptId, s.scriptName);
      return { id: s.id, scriptId: s.scriptId, goalItem: s.goalItem, quantity: String(s.goalQuantity), minutes: s.maxMinutes === null ? '' : String(s.maxMinutes) };
    });
    this.reputationNote = deps.steps.some((s) => s.reputation !== null);
  }

  async load(): Promise<void> {
    try {
      const r = await this.deps.listScripts();
      const parsed = r.status === 200 ? parseScripts(r.data) : null;
      this.scripts = parsed ? parsed.scripts : null;
    } catch {
      this.scripts = null;
    }
    this.error = this.scripts ? null : t('queueEdit.loadFailed');
    this.deps.onChange();
  }

  add(): void {
    if (this.rows.length >= MAX_ROWS || this.busy) return;
    this.rows.push({ id: '', scriptId: '', goalItem: '', quantity: '1', minutes: '' });
    this.deps.onChange();
  }

  remove(i: number): void {
    if (this.busy || i < 0 || i >= this.rows.length) return;
    this.rows.splice(i, 1);
    this.deps.onChange();
  }

  move(i: number, delta: -1 | 1): void {
    const j = i + delta;
    if (this.busy || i < 0 || i >= this.rows.length || j < 0 || j >= this.rows.length) return;
    [this.rows[i], this.rows[j]] = [this.rows[j]!, this.rows[i]!];
    this.deps.onChange();
  }

  setScript(i: number, id: string): void {
    const row = this.rows[i];
    if (row) row.scriptId = id;
  }

  setField(i: number, field: 'goalItem' | 'quantity' | 'minutes', value: string): void {
    const row = this.rows[i];
    if (row) row[field] = value;
  }

  setQuery(text: string): void {
    this.query = text;
    this.deps.onChange();
  }

  async save(): Promise<boolean> {
    if (this.busy) return false;
    if (this.rows.length === 0) return this.refuse(t('queueEdit.error.empty'));
    if (!this.scripts) return this.refuse(t('queueEdit.loadFailed'));
    if (this.rows.length > MAX_ROWS) return this.refuse(t('queueEdit.error.tooMany'));
    const drafts: QueueDraft[] = [];
    for (const [i, row] of this.rows.entries()) {
      const d = toDraft(row);
      if (typeof d === 'string') return this.refuse(t(FAILURE[d], { n: i + 1 }));
      drafts.push(d);
    }
    this.busy = true;
    this.error = null;
    this.deps.onChange();
    let saved = false;
    try {
      const r = await this.deps.replace(drafts);
      if (r.status === 200) saved = true;
      else this.error = this.failure(r);
    } catch {
      this.error = t('control.error.generic');
    }
    this.busy = false;
    if (saved) this.deps.onSaved();
    else this.deps.onChange();
    return saved;
  }

  private refuse(text: string): boolean {
    this.error = text;
    this.deps.onChange();
    return false;
  }

  private failure(r: CallResult): string | null {
    if (r.status === 401) {
      this.deps.onUnpaired();
      return null;
    }
    if (r.status === 504) return t('control.error.noAnswer');
    if (r.status === 0) return t('control.error.unreachable');
    if (r.status === 404) return t('control.error.gone');
    if (r.status === 413) return t('queueEdit.error.tooBig');
    const error = isObject(r.data) && typeof r.data.error === 'string' ? cut(r.data.error) : '';
    if (r.status === 409) {
      if (error === 'busy') return t('control.error.busy');
      return error && error !== 'Skua is disconnected' ? t('queueEdit.error.refused', { reason: error }) : t('control.error.disconnected');
    }
    if (r.status === 400 && error) return t('queueEdit.error.refused', { reason: error });
    return t('control.error.generic');
  }
}

const field = (label: string, control: VNode): VNode => h('label', { className: 'queue-field', text: label }, control);
const button = (action: string, label: MessageKey, extra: Record<string, string> = {}, disabled = false): VNode =>
  h('button', { text: t(label), attrs: { type: 'button', 'data-action': action, ...extra, ...(disabled ? { disabled: '' } : {}) } });

/** The script chooser of row i: the search's matches by folder, and the row's own script first when the search hides it or the plugin no longer lists it. */
export function scriptSelect(editor: QueueEditor, i: number): VNode {
  const row = editor.rows[i]!;
  const listed = editor.scripts ?? [];
  const groups = searchScripts(listed, editor.query).groups;
  const shown = groups.some((g) => g.scripts.some((s) => s.id === row.scriptId));
  const option = (value: string, text: string): VNode => h('option', { text, attrs: { value, ...(value === row.scriptId ? { selected: '' } : {}) } });
  const lead =
    row.scriptId === ''
      ? option('', t('queueEdit.chooseScript'))
      : shown
        ? null
        : option(row.scriptId, listed.find((s) => s.id === row.scriptId)?.name ?? editor.names.get(row.scriptId) ?? row.scriptId);
  return h(
    'select',
    { attrs: { 'data-action': 'queue-script', 'data-value': String(i), ...(editor.busy ? { disabled: '' } : {}) } },
    lead,
    ...groups.map((g) =>
      g.folder === ''
        ? h('optgroup', {}, ...g.scripts.map((s) => option(s.id, s.name)))
        : h('optgroup', { attrs: { label: g.folder } }, ...g.scripts.map((s) => option(s.id, s.name))),
    ),
  );
}

function rowView(editor: QueueEditor, row: DraftRow, i: number): VNode {
  const at = { 'data-value': String(i) };
  const text = (action: string, value: string, extra: Record<string, string>): VNode => h('input', { attrs: { type: 'text', autocomplete: 'off', value, 'data-action': action, ...at, ...extra } });
  return h(
    'div',
    { className: 'queue-row' },
    h('h3', { text: t('queueEdit.step', { n: i + 1 }) }),
    field(t('queueEdit.script'), scriptSelect(editor, i)),
    h(
      'div',
      { className: 'queue-fields' },
      field(t('queueEdit.item'), text('queue-item', row.goalItem, { maxlength: String(ITEM_LENGTH) })),
      field(t('queueEdit.quantity'), text('queue-quantity', row.quantity, { inputmode: 'numeric', maxlength: '7' })),
      field(t('queueEdit.minutes'), text('queue-minutes', row.minutes, { inputmode: 'numeric', maxlength: '4' })),
    ),
    h(
      'div',
      { className: 'queue-row-actions' },
      button('queue-remove', 'queueEdit.remove', at, editor.busy),
      button('queue-up', 'queueEdit.up', at, editor.busy || i === 0),
      button('queue-down', 'queueEdit.down', at, editor.busy || i === editor.rows.length - 1),
    ),
  );
}

export function editorView(editor: QueueEditor): VNode {
  return h(
    'div',
    { className: 'sheet-body' },
    h('h2', { text: t('queueEdit.title'), attrs: { id: 'queue-edit-title' } }),
    editor.reputationNote ? h('p', { className: 'muted', text: t('queueEdit.reputationNote') }) : null,
    editor.scripts === null && editor.error === null ? h('p', { className: 'muted', text: t('queueEdit.loading'), attrs: { role: 'status' } }) : null,
    editor.scripts
      ? field(t('queueEdit.search'), h('input', { className: 'queue-search', attrs: { type: 'text', autocomplete: 'off', value: editor.query, 'data-action': 'queue-search' } }))
      : null,
    ...editor.rows.map((r, i) => rowView(editor, r, i)),
    button('queue-add', 'queueEdit.add', {}, editor.busy || editor.rows.length >= MAX_ROWS),
    editor.error ? h('p', { className: 'queue-error', text: editor.error, attrs: { role: 'alert' } }) : null,
    button('queue-save', editor.busy ? 'queueEdit.saving' : 'queueEdit.save', {}, editor.busy),
    button('queue-cancel', 'queueEdit.cancel'),
  );
}

/** The identity of a control across a redraw. */
export const focusKeyOf = (action: string | null, value: string | null): string => `${action ?? ''}#${value ?? ''}`;
const keyOf = (el: Element): string => focusKeyOf(el.getAttribute('data-action'), el.getAttribute('data-value'));

export type EditTarget = 'script' | 'item' | 'quantity' | 'minutes' | 'search';
const TARGETS: Record<string, EditTarget> = { 'queue-script': 'script', 'queue-item': 'item', 'queue-quantity': 'quantity', 'queue-minutes': 'minutes', 'queue-search': 'search' };

/** Which field an input or change event edits. The search box counts only on `input`: its `change` (on blur) must not rebuild the choosers under a tap. */
export function editTarget(eventType: string, action: string | null): EditTarget | null {
  const target = action !== null && Object.hasOwn(TARGETS, action) ? TARGETS[action]! : null;
  return target === 'search' && eventType !== 'input' ? null : target;
}

/** Where to put focus and the caret again after a redraw: the index of the control with the saved key, and the caret kept only for it. */
export function focusPlan(saved: { key: string; start: number | null; end: number | null } | null, keys: readonly string[]): { index: number; start: number | null; end: number | null } | null {
  if (!saved) return null;
  const index = keys.indexOf(saved.key);
  return index < 0 ? null : { index, start: saved.start, end: saved.end };
}

/** Only one editor dialog at a time: two quick taps on Edit queue must not stack two. */
export const editorSlot = {
  taken: false,
  take(): boolean {
    if (this.taken) return false;
    this.taken = true;
    return true;
  },
  release(): void {
    this.taken = false;
  },
};

/** Builds the editor's own dialog, loads the scripts, and removes the dialog again when it closes. */
export function openQueueEditor(deps: { instanceId: string; queue: QueueModel | null; onSaved(): void; onUnpaired(): void }): void {
  if (!editorSlot.take()) return;
  addMessages(queueEditEn);
  const dialog = document.createElement('dialog');
  dialog.className = 'sheet';
  dialog.setAttribute('aria-labelledby', 'queue-edit-title');
  document.body.append(dialog);
  let closed = false;

  const close = (): void => {
    if (closed) return;
    closed = true;
    editorSlot.release();
    if (dialog.open) dialog.close();
    dialog.remove();
    document.querySelector<HTMLElement>('[data-action="queue-edit"]')?.focus({ preventScroll: true });
  };

  const draw = (): void => {
    if (closed) return;
    const active = document.activeElement;
    const focused = active instanceof HTMLElement && dialog.contains(active) ? active : null;
    const saved = focused
      ? { key: keyOf(focused), start: focused instanceof HTMLInputElement ? focused.selectionStart : null, end: focused instanceof HTMLInputElement ? focused.selectionEnd : null }
      : null;
    dialog.replaceChildren(mount(editorView(editor)));
    const els = [...dialog.querySelectorAll<HTMLElement>('button, input, select')];
    const plan = focusPlan(saved, els.map(keyOf));
    if (!plan) return;
    const el = els[plan.index]!;
    el.focus({ preventScroll: true });
    if (el instanceof HTMLInputElement && plan.start !== null && plan.end !== null) {
      try {
        el.setSelectionRange(plan.start, plan.end);
      } catch {
        // An input type without a caret keeps its own.
      }
    }
  };

  const editor = new QueueEditor({
    instanceId: deps.instanceId,
    steps: deps.queue?.steps ?? [],
    listScripts: () => skuaApi.listScripts(deps.instanceId),
    replace: (steps) => queueApi.replace(deps.instanceId, steps),
    onSaved: () => {
      close();
      deps.onSaved();
    },
    onUnpaired: () => {
      close();
      deps.onUnpaired();
    },
    onChange: draw,
  });

  const indexOf = (el: HTMLElement): number => Number(el.getAttribute('data-value'));
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) return close(); // a tap on the backdrop
    const el = (event.target as HTMLElement).closest<HTMLElement>('[data-action]');
    switch (el?.getAttribute('data-action')) {
      case 'queue-add': return editor.add();
      case 'queue-remove': return editor.remove(indexOf(el!));
      case 'queue-up': return editor.move(indexOf(el!), -1);
      case 'queue-down': return editor.move(indexOf(el!), 1);
      case 'queue-save': return void editor.save();
      case 'queue-cancel': return close();
    }
  });
  const edited = (event: Event): void => {
    const el = event.target as HTMLInputElement;
    const i = indexOf(el);
    switch (editTarget(event.type, el.getAttribute('data-action'))) {
      case 'script': return editor.setScript(i, el.value);
      case 'item': return editor.setField(i, 'goalItem', el.value);
      case 'quantity': return editor.setField(i, 'quantity', el.value);
      case 'minutes': return editor.setField(i, 'minutes', el.value);
      case 'search':
        // Only the choosers are drawn again, from the picks the rows hold, so the search box keeps its caret.
        editor.query = el.value;
        for (const select of dialog.querySelectorAll<HTMLElement>('select[data-action="queue-script"]')) {
          select.replaceWith(mount(scriptSelect(editor, Number(select.getAttribute('data-value')))));
        }
    }
  };
  dialog.addEventListener('input', edited);
  dialog.addEventListener('change', edited);
  dialog.addEventListener('cancel', (event) => {
    event.preventDefault();
    close();
  });

  dialog.replaceChildren(mount(editorView(editor)));
  try {
    dialog.showModal();
  } catch {
    dialog.setAttribute('open', '');
  }
  void editor.load();
}
