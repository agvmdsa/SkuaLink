import { readFileSync } from 'node:fs';
import { describe, expect, it, vi } from 'vitest';
import type { CallResult } from '../src/api';
import { t } from '../src/i18n';
import { queueEn } from '../src/i18n/queueEn';
import { confirmWords, needsConfirm, parseQueues, queueApi, queueCardModel, queueFor, QueueUi, type QueueModel, type QueueWord } from '../src/queue';
import { confirmView, queueCard, startQueue } from '../src/queueViews';
import { parseSkuaInstances } from '../src/skua';
import type { SkuaInstance } from '../src/types';
import type { VNode } from '../src/views';

const all = (n: VNode | null): VNode[] => (n ? [n, ...n.children.flatMap(all)] : []);
const texts = (n: VNode | null) => all(n).map((x) => x.text).filter((x): x is string => x !== undefined);
const settle = async (): Promise<void> => {
  for (let i = 0; i < 30; i++) await Promise.resolve();
};

type Click = (event: unknown) => void;
const fakeApp = () => {
  const app = { handler: null as Click | null, addEventListener: (_type: string, fn: Click) => { app.handler = fn; } };
  return app;
};
const tap = (action: string) => ({ target: { closest: () => ({ getAttribute: () => action }) } });
startQueue(fakeApp() as unknown as HTMLElement, { instance: () => null, queues: () => [], redraw: () => undefined, onUnpaired: () => undefined }); // registers the catalog

const ID = 'a'.repeat(32);
const step = (over: Record<string, unknown> = {}) => ({
  id: ID, scriptId: '0123456789abcdef', scriptName: 'Dailies', goalItem: 'Blood Orb', goalQuantity: 100, reputation: null, maxMinutes: null, ...over,
});
const queue = (over: Record<string, unknown> = {}) => ({
  character: 'Hero', steps: [step()], cursor: 0, state: 'Stopped', phase: null, reason: null, have: null, canEdit: true, revision: 7, ...over,
});
const model = (over: Record<string, unknown> = {}): QueueModel => parseQueues([queue(over)])[0]!;

describe('parseQueues', () => {
  it('round trips a good queue', () => {
    const q = queue({ state: 'Running', phase: 'Running', have: 37, reason: 'why', steps: [step({ reputation: { faction: 'Evil', rank: 3 }, maxMinutes: 30 })] });
    expect(parseQueues([q])).toEqual([q]);
  });
  it('not an array of queues gives none', () => {
    for (const v of [null, 'x', {}, [], undefined, 5]) expect(parseQueues(v)).toEqual([]);
  });
  it('drops a queue with an unknown state and keeps the others', () => {
    expect(parseQueues([queue({ state: 'Weird' }), queue({ character: 'Two' })]).map((q) => q.character)).toEqual(['Two']);
  });
  it('drops a damaged step on its own', () => {
    const bad = [step({ id: 'nope' }), step({ goalQuantity: -1 }), step({ goalQuantity: 1.5 }), step({ goalItem: 5 }), step({ scriptName: null }), step({ goalQuantity: 0 })];
    const q = parseQueues([queue({ steps: [...bad, step({ goalItem: 'Keep' })] })])[0]!;
    expect(q.steps.map((s) => s.goalItem)).toEqual(['Keep']);
  });
  it('cuts 21 steps to 20 and 21 queues to 20', () => {
    expect(parseQueues([queue({ steps: Array.from({ length: 21 }, () => step()) })])[0]!.steps).toHaveLength(20);
    expect(parseQueues(Array.from({ length: 21 }, (_, i) => queue({ character: 'C' + i })))).toHaveLength(20);
  });
  it('cuts long texts to 300', () => {
    const q = parseQueues([queue({ reason: 'r'.repeat(5000), steps: [step({ goalItem: 'g'.repeat(5000) })] })])[0]!;
    expect(q.reason).toHaveLength(300);
    expect(q.steps[0]!.goalItem).toHaveLength(300);
  });
  it('phase outside the four names is null', () => {
    expect(model({ state: 'Running', phase: 'Flying' }).phase).toBeNull();
    for (const p of ['Starting', 'Running', 'Stopping', 'Settling']) expect(model({ state: 'Running', phase: p }).phase).toBe(p);
  });
  it('have negative or fractional is null', () => {
    expect(model({ have: -1 }).have).toBeNull();
    expect(model({ have: 1.5 }).have).toBeNull();
    expect(model({ have: 0 }).have).toBe(0);
  });
  it('canEdit is false only when it is false', () => {
    expect(model({ canEdit: false }).canEdit).toBe(false);
    for (const v of [true, undefined, null, 'no', 0]) expect(model({ canEdit: v }).canEdit).toBe(true);
  });
});

describe('queueFor', () => {
  const qs = parseQueues([queue()]);
  it('matches without case', () => expect(queueFor(qs, 'hero')?.character).toBe('Hero'));
  it('null and unknown give null', () => {
    expect(queueFor(qs, null)).toBeNull();
    expect(queueFor(qs, 'Nobody')).toBeNull();
  });
});

describe('queueCardModel', () => {
  const buttons = (m: ReturnType<typeof queueCardModel>) => (['start', 'pause', 'continue', 'skip', 'stop', 'edit'] as const).filter((k) => m[k]);
  it.each([
    ['Stopped with steps', { state: 'Stopped' }, ['start', 'edit']],
    ['Stopped without steps', { state: 'Stopped', steps: [] }, ['edit']],
    ['Running/Running', { state: 'Running', phase: 'Running' }, ['pause', 'skip', 'stop', 'edit']],
    ['Running/Starting', { state: 'Running', phase: 'Starting', canEdit: false }, ['pause', 'skip', 'stop']],
    ['Running/Stopping', { state: 'Running', phase: 'Stopping', canEdit: false }, ['pause', 'skip', 'stop']],
    ['Running/Settling', { state: 'Running', phase: 'Settling', canEdit: false }, ['pause', 'skip', 'stop']],
    ['Paused', { state: 'Paused' }, ['continue', 'skip', 'stop', 'edit']],
    ['Finished', { state: 'Finished', cursor: 1 }, ['start', 'edit']],
  ])('%s', (_n, over, expected) => expect(buttons(queueCardModel(model(over), false, null))).toEqual(expected));
  it('empty is true without steps', () => {
    expect(queueCardModel(model({ steps: [] }), false, null).empty).toBe(true);
    expect(queueCardModel(model(), false, null).empty).toBe(false);
  });
  it('pending disables everything', () => {
    const m = queueCardModel(model({ state: 'Running', phase: 'Running' }), true, null);
    expect(buttons(m)).toEqual([]);
    expect(m.pending).toBe(true);
  });
  it('no queue gives the empty card with edit only', () => {
    const m = queueCardModel(null, false, null);
    expect(buttons(m)).toEqual(['edit']);
    expect(m.empty).toBe(true);
    expect(m.step).toBeNull();
  });
});

describe('queueCardModel texts', () => {
  const steps = [1, 2, 3, 4, 5].map((n) => step({ id: String(n).repeat(32), scriptName: n === 2 ? 'Dailies' : 'S' + n }));
  it('state, step and progress', () => {
    const m = queueCardModel(model({ state: 'Running', phase: 'Running', cursor: 1, steps, have: 37, goalQuantity: 100 }), false, null);
    expect(m.state).toBe('Running: watching the script');
    expect(m.step).toBe('Step 2 of 5: Dailies');
    expect(m.progress).toBe('37 of 100 Blood Orb');
  });
  it('goal only when have is unknown', () => expect(queueCardModel(model({ state: 'Paused' }), false, null).progress).toBe('Goal: 100 Blood Orb'));
  it('Finished has no step line', () => {
    const m = queueCardModel(model({ state: 'Finished', cursor: 1 }), false, null);
    expect(m.step).toBeNull();
    expect(m.progress).toBeNull();
  });
  it('the reason is verbatim, also when hostile', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    expect(queueCardModel(model({ state: 'Paused', reason: hostile }), false, null).reason).toBe(hostile);
  });
});

const inst = (over: Record<string, unknown> = {}, character: string | null = 'hero'): SkuaInstance =>
  parseSkuaInstances([{
    instanceId: 'i-1', label: 'Hero', connection: 'Connected', disconnected: false, hello: null, log: [],
    status: { loggedIn: true, character, server: null, map: null, script: null, scriptRunning: false, runningSince: null, quests: null, factions: null, control: 'idle' }, ...over,
  }])![0]!;
const fakeApi = (answer: CallResult | (() => Promise<CallResult>)) => ({
  command: vi.fn(async (_id: string, _w: QueueWord) => (typeof answer === 'function' ? answer() : answer)),
});

describe('queueCard view', () => {
  const ui = new QueueUi(fakeApi({ status: 200, data: {} }), () => undefined, () => undefined);
  it('is a card with a title, five buttons, the Edit button and the hostile text only as text', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    const v = queueCard(inst(), ui, [queue({ state: 'Paused', reason: hostile })], 0)!;
    expect(v.tag).toBe('section');
    expect(v.className).toBe('card queue-card');
    expect(all(v).find((n) => n.tag === 'h2')!.text).toBe(t('queue.title'));
    const b = all(v).filter((n) => n.tag === 'button');
    expect(b.map((x) => x.attrs!['data-action'])).toEqual(['queue-start', 'queue-pause', 'queue-continue', 'queue-skip', 'queue-stop', 'queue-edit']);
    expect(b.map((x) => 'disabled' in x.attrs!)).toEqual([true, true, false, false, false, false]);
    expect(texts(v)).toContain(hostile);
    for (const n of all(v)) for (const val of Object.values(n.attrs ?? {})) expect(val).not.toContain(hostile);
  });
  it('turns the Edit button off when the queue cannot be edited', () => {
    const v = queueCard(inst(), ui, [queue({ state: 'Running', phase: 'Starting' })], 0)!;
    const edit = all(v).find((n) => n.attrs?.['data-action'] === 'queue-edit')!;
    expect('disabled' in edit.attrs!).toBe(true);
  });
  it('null for a Skua that is not live or has no character', () => {
    expect(queueCard(inst({ disconnected: true }), ui, [queue()], 0)).toBeNull();
    expect(queueCard(inst({}, null), ui, [queue()], 0)).toBeNull();
  });
  it('picks the queue of the character', () => {
    const v = queueCard(inst(), ui, [queue({ character: 'Other', steps: [step({ scriptName: 'Wrong' })] }), queue()], 0)!;
    expect(texts(v).join('|')).toContain('Dailies');
    expect(texts(v).join('|')).not.toContain('Wrong');
  });
  it('shows an error line as an alert', async () => {
    const failing = new QueueUi(fakeApi({ status: 504, data: null }), () => undefined, () => undefined);
    await failing.run('i-1', 'start');
    const v = queueCard(inst(), failing, [queue()], 0)!;
    const alert = all(v).find((n) => n.attrs?.role === 'alert')!;
    expect(alert.text).toBe(t('control.error.noAnswer'));
  });
});

describe('QueueUi.run', () => {
  it('a good answer redraws and calls through once', async () => {
    const api = fakeApi({ status: 200, data: {} });
    const changed = vi.fn();
    const ui = new QueueUi(api, changed, () => undefined);
    await ui.run('i-1', 'start');
    expect(api.command).toHaveBeenCalledTimes(1);
    expect(api.command).toHaveBeenCalledWith('i-1', 'start');
    expect(ui.errorOf('i-1')).toBeNull();
    expect(ui.isPending('i-1')).toBe(false);
    expect(changed).toHaveBeenCalled();
  });
  it('a 409 refusal shows the PC text', async () => {
    const ui = new QueueUi(fakeApi({ status: 409, data: { error: 'The queue is paused. Press Continue.' } }), () => undefined, () => undefined);
    await ui.run('i-1', 'start');
    expect(ui.errorOf('i-1')).toBe(t('queue.error.refused', { reason: 'The queue is paused. Press Continue.' }));
  });
  it.each([
    [504, null, 'control.error.noAnswer'],
    [0, null, 'control.error.unreachable'],
    [404, null, 'control.error.gone'],
    [409, { error: 'busy' }, 'control.error.busy'],
    [409, { error: 'Skua is disconnected' }, 'control.error.disconnected'],
    [500, null, 'control.error.generic'],
  ] as const)('status %i maps to %s', async (status, data, key) => {
    const ui = new QueueUi(fakeApi({ status, data }), () => undefined, () => undefined);
    await ui.run('i-1', 'pause');
    expect(ui.errorOf('i-1')).toBe(t(key));
  });
  it('400 with a text is a refusal', async () => {
    const ui = new QueueUi(fakeApi({ status: 400, data: { error: 'nope' } }), () => undefined, () => undefined);
    await ui.run('i-1', 'skip');
    expect(ui.errorOf('i-1')).toBe(t('queue.error.refused', { reason: 'nope' }));
  });
  it('401 calls onUnpaired and shows nothing', async () => {
    const unpaired = vi.fn();
    const ui = new QueueUi(fakeApi({ status: 401, data: null }), () => undefined, unpaired);
    await ui.run('i-1', 'stop');
    expect(unpaired).toHaveBeenCalledTimes(1);
    expect(ui.errorOf('i-1')).toBeNull();
  });
  it('a throwing call is the generic error', async () => {
    const ui = new QueueUi({ command: async () => { throw new Error('x'); } }, () => undefined, () => undefined);
    await ui.run('i-1', 'stop');
    expect(ui.errorOf('i-1')).toBe(t('control.error.generic'));
  });
  it('a second run while pending is ignored', async () => {
    let release: (r: CallResult) => void = () => undefined;
    const api = fakeApi(() => new Promise<CallResult>((r) => { release = r; }));
    const ui = new QueueUi(api, () => undefined, () => undefined);
    const first = ui.run('i-1', 'start');
    expect(ui.isPending('i-1')).toBe(true);
    await ui.run('i-1', 'pause');
    expect(api.command).toHaveBeenCalledTimes(1);
    release({ status: 200, data: {} });
    await first;
    await settle();
    expect(ui.isPending('i-1')).toBe(false);
  });
});

describe('revision', () => {
  it('is read from the entry; missing or not a whole number is 0', () => {
    expect(model({ revision: 42 }).revision).toBe(42);
    for (const bad of [undefined, 'x', 1.5, -1, null]) expect(model({ revision: bad }).revision).toBe(0);
  });
  it('the save body carries it as a JSON number, 0 included', async () => {
    const bodies: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (_p: string, init?: RequestInit) => {
      bodies.push(String(init?.body));
      return new Response('{}');
    }));
    await queueApi.replace('i-1', [], 7);
    await queueApi.replace('i-1', [], 0);
    vi.unstubAllGlobals();
    expect(bodies.map((b) => JSON.parse(b).revision)).toEqual([7, 0]);
  });
});

describe('a clock-seeded revision', () => {
  const BIG = 1_700_000_000_123;
  it('is kept exactly; unsafe, negative and fractional ones are 0', () => {
    expect(model({ revision: BIG }).revision).toBe(BIG);
    for (const bad of [2 ** 53, -1, 1.5, Infinity, NaN]) expect(model({ revision: bad }).revision).toBe(0);
  });
  it('goes from the state through the editor snapshot into the PUT body unchanged', async () => {
    const q = model({ revision: BIG });
    const bodies: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (_p: string, init?: RequestInit) => {
      bodies.push(String(init?.body));
      return new Response('{}');
    }));
    await queueApi.replace('i-1', [], q.revision);
    vi.unstubAllGlobals();
    expect(JSON.parse(bodies[0]!).revision).toBe(BIG);
    expect(bodies[0]).toContain(`"revision":${BIG}`);
  });
});

describe('confirmation', () => {
  it('only Skip and Stop ask', () => {
    expect((['start', 'pause', 'continue', 'skip', 'stop'] as QueueWord[]).filter(needsConfirm)).toEqual(['skip', 'stop']);
  });
  it('names the character and the current step (Skip) or the queue (Stop)', () => {
    const q = model({ state: 'Running', cursor: 1, steps: [step(), step({ id: 'b'.repeat(32), scriptName: 'Chaos' })] });
    const skip = confirmWords('skip', q, 'Hero');
    expect(skip.title).toContain('Chaos');
    expect(skip.title).toContain('Hero');
    expect(skip.confirm).toBe(t('queue.skip'));
    const stop = confirmWords('stop', q, 'Hero');
    expect(stop.title).toContain('Hero');
    expect(stop.confirm).toBe(t('queue.stop'));
  });
  it('the sheet shows hostile names as text only, with Confirm and Cancel', () => {
    const evil = '<img src=x onerror=alert(1)>';
    const view = confirmView(confirmWords('skip', model({ steps: [step({ scriptName: evil })] }), evil));
    const nodes = all(view);
    expect(nodes.find((n) => n.tag === 'h2')!.text).toContain(evil);
    expect(nodes.some((n) => n.tag === 'img')).toBe(false);
    const buttons = nodes.filter((n) => n.tag === 'button');
    expect(buttons.map((b) => b.attrs!['data-action'])).toEqual(['queue-confirm', 'queue-cancel']);
    expect(buttons[0]!.className).toBe('btn-danger');
    expect(buttons[1]!.text).toBe(t('control.cancel'));
  });
});

describe('the delegated click', () => {
  const setup = () => {
    const spy = vi.fn(async (_path: string) => new Response('{}'));
    vi.stubGlobal('fetch', spy);
    const app = fakeApp();
    const asked: { title: string; go(): void }[] = [];
    const ui = startQueue(app as unknown as HTMLElement, {
      instance: () => inst(), queues: () => [queue({ state: 'Running', phase: 'Running' })], redraw: () => undefined, onUnpaired: () => undefined,
      confirm: (words, go) => asked.push({ title: words.title, go }),
    });
    return { spy, app, ui, asked };
  };
  it('runs the three plain words at once and nothing else', async () => {
    const { spy, app, ui, asked } = setup();
    expect(ui).toBeInstanceOf(QueueUi);
    for (const a of ['queue-edit', 'queue-bogus', 'toString']) app.handler!(tap(a));
    await settle();
    expect(spy).not.toHaveBeenCalled();
    for (const [action, word] of [['queue-start', 'start'], ['queue-pause', 'pause'], ['queue-continue', 'continue']]) {
      spy.mockClear();
      app.handler!(tap(action!));
      await settle();
      expect(spy.mock.calls.map((c) => c[0])).toEqual([`/api/skua/i-1/queue/${word}`]);
    }
    expect(asked).toHaveLength(0);
    vi.unstubAllGlobals();
  });
  it('Skip and Stop send nothing until Confirm', async () => {
    const { spy, app, asked } = setup();
    app.handler!(tap('queue-skip'));
    app.handler!(tap('queue-stop'));
    await settle();
    expect(spy).not.toHaveBeenCalled();
    expect(asked).toHaveLength(2);
    expect(asked[0]!.title).toContain('Dailies');
    asked[0]!.go();
    await settle();
    expect(spy.mock.calls.map((c) => c[0])).toEqual(['/api/skua/i-1/queue/skip']);
    asked[1]!.go();
    await settle();
    expect(spy.mock.calls.map((c) => c[0])).toEqual(['/api/skua/i-1/queue/skip', '/api/skua/i-1/queue/stop']);
    vi.unstubAllGlobals();
  });
  it('Skip sends nothing when the queue moved on while the sheet was open', async () => {
    const spy = vi.fn(async (_path: string) => new Response('{}'));
    vi.stubGlobal('fetch', spy);
    const app = fakeApp();
    let cursor = 0;
    const asked: (() => void)[] = [];
    const two = [step(), step({ id: 'b'.repeat(32), scriptName: 'Chaos' })];
    startQueue(app as unknown as HTMLElement, {
      instance: () => inst(), queues: () => [queue({ state: 'Running', phase: 'Running', steps: two, cursor })], redraw: () => undefined, onUnpaired: () => undefined,
      confirm: (_w, go) => asked.push(go),
    });
    app.handler!(tap('queue-skip'));
    app.handler!(tap('queue-stop'));
    cursor = 1;
    asked[0]!();
    await settle();
    expect(spy).not.toHaveBeenCalled();
    asked[1]!(); // Stop still names the queue, not a step
    await settle();
    expect(spy.mock.calls.map((c) => c[0])).toEqual(['/api/skua/i-1/queue/stop']);
    vi.unstubAllGlobals();
  });
  it('a double Confirm while the first is pending sends one command', async () => {
    const { spy, app, asked } = setup();
    app.handler!(tap('queue-stop'));
    asked[0]!.go();
    asked[0]!.go();
    await settle();
    expect(spy).toHaveBeenCalledTimes(1);
    vi.unstubAllGlobals();
  });
});

describe('catalog and stylesheet', () => {
  const read = (p: string) => readFileSync(new URL(p, import.meta.url), 'utf8');
  it('every queue key used exists in the catalog', () => {
    const used = new Set<string>();
    for (const file of ['../src/queue.ts', '../src/queueViews.ts']) for (const m of read(file).matchAll(/'(queue\.[A-Za-z.]+)'/g)) used.add(m[1]!);
    expect(used.size).toBeGreaterThan(10);
    for (const key of used) expect(queueEn).toHaveProperty(key);
  });
  const css = read('../src/styles/queue.css');
  it('declares no colour value and every selector starts with .queue-', () => {
    expect(css).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgb\(|rgba\(|hsl\(|oklch\(/);
    const selectors = css.replace(/\/\*[\s\S]*?\*\//g, '').split('}').map((r) => r.split('{')[0]!.trim()).filter(Boolean);
    expect(selectors.length).toBeGreaterThan(3);
    for (const s of selectors) expect(s).toMatch(/^\.queue-/);
  });
  it('buttons are touch sized', () => expect(css).toMatch(/\.queue-actions button\s*\{[^}]*min-height:\s*3rem/));
});
