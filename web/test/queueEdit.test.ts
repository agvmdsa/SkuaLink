import { readFileSync } from 'node:fs';
import { describe, expect, it, vi } from 'vitest';
import type { CallResult } from '../src/api';
import { addMessages, t } from '../src/i18n';
import { queueEditEn } from '../src/i18n/queueEditEn';
import { editTarget, editorSlot, editorView, focusKeyOf, focusPlan, QueueEditor, scriptSelect, type QueueDraft } from '../src/queueEdit';
import { parseQueues, type QueueStep } from '../src/queue';
import type { VNode } from '../src/views';

addMessages(queueEditEn);

const all = (n: VNode | null): VNode[] => (n ? [n, ...n.children.flatMap(all)] : []);
const ID = 'a'.repeat(32);
const SCRIPT = '0123456789abcdef';
const step = (over: Record<string, unknown> = {}): QueueStep =>
  parseQueues([{ character: 'Hero', state: 'Stopped', steps: [{ id: ID, scriptId: SCRIPT, scriptName: 'Dailies', goalItem: 'Blood Orb', goalQuantity: 100, reputation: null, maxMinutes: null, ...over }] }])[0]!.steps[0]!;
const scriptsReply = (list: { id: string; name: string; folder?: string }[]): CallResult => ({ status: 200, data: { scripts: list.map((s) => ({ folder: '', askOptions: true, ...s })) } });
const LIST = scriptsReply([
  { id: SCRIPT, name: 'Dailies', folder: 'Farm' },
  { id: 'fedcba9876543210', name: 'Chaos', folder: 'Boss' },
]);

function make(steps: QueueStep[] = [step()], over: Partial<ConstructorParameters<typeof QueueEditor>[0]> = {}) {
  const calls = { changed: 0, saved: 0, unpaired: 0, sent: [] as QueueDraft[][] };
  const editor = new QueueEditor({
    instanceId: 'i1',
    steps,
    listScripts: vi.fn(async () => LIST),
    replace: vi.fn(async (s: QueueDraft[]) => {
      calls.sent.push(s);
      return { status: 200, data: {} } as CallResult;
    }),
    onSaved: () => calls.saved++,
    onUnpaired: () => calls.unpaired++,
    onChange: () => calls.changed++,
    ...over,
  });
  return { editor, calls };
}

describe('QueueEditor rows', () => {
  it('opens with the stored steps as text and shows the reputation note only when a step has one', () => {
    const { editor } = make([step({ maxMinutes: 30 })]);
    expect(editor.rows).toEqual([{ id: ID, scriptId: SCRIPT, goalItem: 'Blood Orb', quantity: '100', minutes: '30' }]);
    expect(editor.reputationNote).toBe(false);
    expect(make([step({ reputation: { faction: 'Evil', rank: 3 } })]).editor.reputationNote).toBe(true);
    const text = all(editorView(make([step({ reputation: { faction: 'Evil', rank: 3 } })]).editor)).map((n) => n.text);
    expect(text).toContain(t('queueEdit.reputationNote'));
  });
  it('add stops at 20; remove and move stay inside the list', () => {
    const { editor, calls } = make([]);
    for (let i = 0; i < 25; i++) editor.add();
    expect(editor.rows).toHaveLength(20);
    expect(calls.changed).toBe(20);
    const two = make([step({ goalItem: 'A' }), step({ id: 'b'.repeat(32), goalItem: 'B' })]).editor;
    two.move(0, -1);
    two.move(1, 1);
    expect(two.rows.map((r) => r.goalItem)).toEqual(['A', 'B']);
    two.move(0, 1);
    expect(two.rows.map((r) => r.goalItem)).toEqual(['B', 'A']);
    two.remove(5);
    two.remove(0);
    expect(two.rows.map((r) => r.goalItem)).toEqual(['A']);
  });
  it('typing and picking change only that row and never redraw', () => {
    const { editor, calls } = make([step(), step({ id: 'b'.repeat(32) })]);
    editor.setField(1, 'goalItem', 'Gold');
    editor.setScript(1, 'fedcba9876543210');
    editor.setField(9, 'quantity', '5');
    expect(editor.rows[0]!.goalItem).toBe('Blood Orb');
    expect(editor.rows[1]).toMatchObject({ goalItem: 'Gold', scriptId: 'fedcba9876543210' });
    expect(calls.changed).toBe(0);
  });
});

describe('QueueEditor load', () => {
  it('lists the scripts once and parses them', async () => {
    const { editor, calls } = make();
    await editor.load();
    expect(editor.scripts!.map((s) => s.name)).toEqual(['Dailies', 'Chaos']);
    expect(editor.error).toBeNull();
    expect(calls.changed).toBe(1);
  });
  it.each([[{ status: 500, data: null }], [{ status: 504, data: {} }], [{ status: 200, data: 'x' }]])('a bad answer %j gives no scripts and refuses a save', async (reply) => {
    const { editor, calls } = make([step()], { listScripts: async () => reply });
    await editor.load();
    expect(editor.scripts).toBeNull();
    expect(editor.error).toBe(t('queueEdit.loadFailed'));
    expect(await editor.save()).toBe(false);
    expect(editor.error).toBe(t('queueEdit.loadFailed'));
    expect(calls.sent).toEqual([]);
  });
  it('a throw is the same, and with no rows a save is refused locally', async () => {
    const { editor, calls } = make([], { listScripts: async () => { throw new Error('x'); } });
    await editor.load();
    expect(editor.scripts).toBeNull();
    expect(await editor.save()).toBe(false);
    expect(editor.error).toBe(t('queueEdit.error.empty'));
    expect(calls.sent).toEqual([]);
  });
  it('a reload while open keeps what the owner picked', async () => {
    const { editor } = make();
    await editor.load();
    editor.setScript(0, 'fedcba9876543210');
    editor.setField(0, 'goalItem', 'Mine');
    await editor.load();
    await editor.load();
    expect(editor.rows[0]).toMatchObject({ scriptId: 'fedcba9876543210', goalItem: 'Mine' });
    const select = scriptSelect(editor, 0);
    expect(all(select).filter((n) => n.tag === 'option' && 'selected' in n.attrs!).map((n) => n.attrs!.value)).toEqual(['fedcba9876543210']);
  });
});

describe('QueueEditor save validation', () => {
  const bad: [string, Partial<{ scriptId: string; goalItem: string; quantity: string; minutes: string }>, string][] = [
    ['no script', { scriptId: '' }, 'script'],
    ['empty item', { goalItem: '   ' }, 'item'],
    ['quantity 0', { quantity: '0' }, 'quantity'],
    ['quantity text', { quantity: '1e3' }, 'quantity'],
    ['quantity negative', { quantity: '-1' }, 'quantity'],
    ['quantity too big', { quantity: '1000001' }, 'quantity'],
    ['minutes 0', { minutes: '0' }, 'minutes'],
    ['minutes 1441', { minutes: '1441' }, 'minutes'],
    ['minutes text', { minutes: '5m' }, 'minutes'],
  ];
  it.each(bad)('%s', async (_n, change, key) => {
    const { editor, calls } = make();
    await editor.load();
    Object.assign(editor.rows[0]!, change);
    expect(await editor.save()).toBe(false);
    expect(editor.error).toBe(t(`queueEdit.error.${key}` as 'queueEdit.error.item', { n: 1 }));
    expect(calls.sent).toEqual([]);
  });
  it('more than 20 rows is refused', async () => {
    const { editor, calls } = make(Array.from({ length: 21 }, () => step()));
    await editor.load();
    expect(await editor.save()).toBe(false);
    expect(editor.error).toBe(t('queueEdit.error.tooMany'));
    expect(calls.sent).toEqual([]);
  });
});

describe('QueueEditor save', () => {
  it('sends exactly the drafts, trimmed, with empty ids for new rows, and tells the page on 200', async () => {
    const { editor, calls } = make([step({ maxMinutes: 30 })]);
    await editor.load();
    editor.add();
    editor.setScript(1, 'fedcba9876543210');
    editor.setField(1, 'goalItem', '  Gold  ');
    editor.setField(1, 'quantity', ' 5 ');
    expect(await editor.save()).toBe(true);
    expect(calls.sent).toEqual([[
      { id: ID, scriptId: SCRIPT, goalItem: 'Blood Orb', goalQuantity: 100, maxMinutes: 30 },
      { id: '', scriptId: 'fedcba9876543210', goalItem: 'Gold', goalQuantity: 5, maxMinutes: null },
    ]]);
    expect(Object.keys(calls.sent[0]![0]!)).toEqual(['id', 'scriptId', 'goalItem', 'goalQuantity', 'maxMinutes']);
    expect(calls.saved).toBe(1);
  });
  const refused = (reply: CallResult | (() => never)) => async () => {
    const { editor, calls } = make([step()], { replace: async () => (typeof reply === 'function' ? reply() : reply) });
    await editor.load();
    const saved = await editor.save();
    return { editor, calls, saved };
  };
  it('409 and 400 with a text show it and keep the rows', async () => {
    const text = 'The queue is starting or stopping a script. Try again in a moment.';
    for (const status of [409, 400]) {
      const { editor, calls, saved } = await refused({ status, data: { error: text } })();
      expect(saved).toBe(false);
      expect(editor.error).toBe(t('queueEdit.error.refused', { reason: text }));
      expect(editor.rows).toHaveLength(1);
      expect(calls.saved).toBe(0);
      expect(editor.busy).toBe(false);
    }
  });
  it.each([
    [{ status: 409, data: { error: 'busy' } }, 'control.error.busy'],
    [{ status: 409, data: { error: 'Skua is disconnected' } }, 'control.error.disconnected'],
    [{ status: 504, data: null }, 'control.error.noAnswer'],
    [{ status: 0, data: null }, 'control.error.unreachable'],
    [{ status: 404, data: null }, 'control.error.gone'],
    [{ status: 413, data: null }, 'queueEdit.error.tooBig'],
    [{ status: 500, data: null }, 'control.error.generic'],
  ] as const)('%j', async (reply, key) => {
    const { editor } = await refused(reply)();
    expect(editor.error).toBe(t(key));
  });
  it('401 tells the page it is unpaired and shows nothing', async () => {
    const { editor, calls } = await refused({ status: 401, data: null })();
    expect(calls.unpaired).toBe(1);
    expect(editor.error).toBeNull();
  });
  it('a throw is the generic words', async () => {
    const { editor } = await refused(() => { throw new Error('x'); })();
    expect(editor.error).toBe(t('control.error.generic'));
  });
  it('is busy while the call runs and ignores a second save', async () => {
    let finish: (r: CallResult) => void = () => undefined;
    const replace = vi.fn(() => new Promise<CallResult>((r) => { finish = r; }));
    const { editor } = make([step()], { replace });
    await editor.load();
    const first = editor.save();
    expect(editor.busy).toBe(true);
    expect(await editor.save()).toBe(false);
    editor.add();
    expect(editor.rows).toHaveLength(1);
    finish({ status: 200, data: {} });
    expect(await first).toBe(true);
    expect(editor.busy).toBe(false);
    expect(replace).toHaveBeenCalledTimes(1);
  });
});

describe('script chooser and search', () => {
  const options = (n: VNode) => all(n).filter((x) => x.tag === 'option');
  it('groups by folder, follows the search, and keeps the row\'s own script visible', async () => {
    const { editor } = make();
    await editor.load();
    const sel = scriptSelect(editor, 0);
    expect(all(sel).filter((x) => x.tag === 'optgroup').map((x) => x.attrs!.label)).toEqual(['Farm', 'Boss']);
    expect(options(sel).map((x) => x.text)).toEqual(['Dailies', 'Chaos']);
    editor.setQuery('chaos');
    const filtered = scriptSelect(editor, 0);
    expect(options(filtered).map((x) => x.text)).toEqual(['Dailies', 'Chaos']);
    expect(options(filtered)[0]!.attrs!.selected).toBe('');
    editor.setQuery('chaos');
    expect(options(scriptSelect(editor, 0)).map((x) => x.attrs!.value)).toEqual([SCRIPT, 'fedcba9876543210']);
  });
  it('a script that is no longer listed keeps its stored name, and a new row asks to choose', async () => {
    const { editor } = make([step({ scriptName: 'Gone Script' })], { listScripts: async () => scriptsReply([{ id: 'fedcba9876543210', name: 'Chaos' }]) });
    await editor.load();
    expect(options(scriptSelect(editor, 0))[0]!.text).toBe('Gone Script');
    editor.add();
    expect(options(scriptSelect(editor, 1))[0]!.text).toBe(t('queueEdit.chooseScript'));
  });
});

describe('editorView', () => {
  const hostile = '<b>x</b>';
  it('draws the title, search, one block per row, buttons and an alert, with hostile text only as text', async () => {
    const { editor } = make([step({ goalItem: hostile, scriptName: hostile }), step({ id: 'b'.repeat(32) })], {
      listScripts: async () => scriptsReply([{ id: SCRIPT, name: hostile, folder: hostile }]),
    });
    await editor.load();
    editor.error = 'oops';
    const v = editorView(editor);
    const nodes = all(v);
    expect(v.tag).toBe('div');
    expect(v.className).toBe('sheet-body');
    expect(nodes.find((n) => n.tag === 'h2')!.text).toBe(t('queueEdit.title'));
    expect(nodes.find((n) => n.attrs?.['data-action'] === 'queue-search')).toBeDefined();
    expect(nodes.filter((n) => n.tag === 'h3').map((n) => n.text)).toEqual([t('queueEdit.step', { n: 1 }), t('queueEdit.step', { n: 2 })]);
    const selects = nodes.filter((n) => n.tag === 'select');
    expect(selects.map((n) => n.attrs!['data-value'])).toEqual(['0', '1']);
    const item = nodes.find((n) => n.attrs?.['data-action'] === 'queue-item')!;
    expect(item.attrs).toMatchObject({ type: 'text', maxlength: '80', autocomplete: 'off', value: hostile });
    expect(nodes.find((n) => n.attrs?.['data-action'] === 'queue-quantity')!.attrs!.inputmode).toBe('numeric');
    const flag = (action: string, i: number) => 'disabled' in nodes.filter((n) => n.attrs?.['data-action'] === action)[i]!.attrs!;
    expect([flag('queue-up', 0), flag('queue-up', 1), flag('queue-down', 0), flag('queue-down', 1)]).toEqual([true, false, false, true]);
    expect(nodes.find((n) => n.attrs?.['data-action'] === 'queue-add')!.attrs).not.toHaveProperty('disabled');
    expect(nodes.find((n) => n.tag === 'p' && n.attrs?.role === 'alert')!.text).toBe('oops');
    expect(nodes.map((n) => n.text)).toContain(hostile);
    expect(all(v).filter((n) => n.tag === 'optgroup').map((n) => n.attrs!.label)).toEqual([hostile, hostile]);
    for (const n of nodes) {
      expect(n.tag).not.toBe('b');
      for (const [k, val] of Object.entries(n.attrs ?? {})) if (!['value', 'label'].includes(k)) expect(val).not.toContain('<');
    }
  });
  it('turns Add off at 20 rows and every button off while saving', () => {
    const { editor } = make([]);
    for (let i = 0; i < 20; i++) editor.add();
    expect('disabled' in all(editorView(editor)).find((n) => n.attrs?.['data-action'] === 'queue-add')!.attrs!).toBe(true);
  });
});

describe('the module', () => {
  const src = readFileSync(new URL('../src/queueEdit.ts', import.meta.url), 'utf8');
  it('uses only keys the catalog has', () => {
    const used = new Set([...src.matchAll(/'(queueEdit\.[A-Za-z.]+)'/g)].map((m) => m[1]!));
    for (const key of used) expect(Object.keys(queueEditEn), key).toContain(key);
  });
  it('keeps innerHTML out', () => expect(src).not.toMatch(/innerHTML|insertAdjacentHTML/));
});

describe('the DOM wiring decisions', () => {
  it('the search box edits on input only, so its blur change cannot rebuild the choosers', () => {
    expect(editTarget('input', 'queue-search')).toBe('search');
    expect(editTarget('change', 'queue-search')).toBeNull();
  });
  it('the other fields edit on both events and unknown ones on none', () => {
    for (const type of ['input', 'change']) {
      expect(editTarget(type, 'queue-script')).toBe('script');
      expect(editTarget(type, 'queue-item')).toBe('item');
      expect(editTarget(type, 'queue-quantity')).toBe('quantity');
      expect(editTarget(type, 'queue-minutes')).toBe('minutes');
      expect(editTarget(type, 'queue-save')).toBeNull();
      expect(editTarget(type, null)).toBeNull();
      expect(editTarget(type, 'constructor')).toBeNull();
    }
  });
  it('focus returns to the control with the same key, with its caret, else nowhere', () => {
    const keys = [focusKeyOf('queue-item', '0'), focusKeyOf('queue-item', '1'), focusKeyOf('queue-save', null)];
    expect(focusPlan({ key: keys[1]!, start: 2, end: 4 }, keys)).toEqual({ index: 1, start: 2, end: 4 });
    expect(focusPlan({ key: 'gone#', start: 1, end: 1 }, keys)).toBeNull();
    expect(focusPlan(null, keys)).toBeNull();
  });
  it('only one editor can be open: a second tap before the first closes is refused', () => {
    editorSlot.release();
    expect(editorSlot.take()).toBe(true);
    expect(editorSlot.take()).toBe(false);
    editorSlot.release();
    expect(editorSlot.take()).toBe(true);
    editorSlot.release();
  });
});
