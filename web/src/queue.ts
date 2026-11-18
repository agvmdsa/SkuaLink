import { skuaCall, skuaPath, type CallResult } from './api';
import { t, type MessageKey } from './i18n';
import { count, cut, isObject } from './skua';

// The script queue as the page sees it: the PC's `queues` field parsed and bounded, the card's numbers and words, and the five
// commands. Loaded with the queue card (a chunk of its own).

export type QueueStateName = 'Stopped' | 'Running' | 'Paused' | 'Finished';
export type QueuePhaseName = 'Starting' | 'Running' | 'Stopping' | 'Settling';
export type QueueWord = 'start' | 'pause' | 'continue' | 'skip' | 'stop';

export interface QueueStep {
  id: string;
  scriptId: string;
  scriptName: string;
  goalItem: string;
  goalQuantity: number;
  reputation: { faction: string; rank: number } | null;
  maxMinutes: number | null;
}

export interface QueueModel {
  character: string;
  steps: QueueStep[];
  cursor: number;
  state: QueueStateName;
  phase: QueuePhaseName | null;
  reason: string | null;
  have: number | null;
  canEdit: boolean;
  /** The PC's number for these steps; a save quotes the one the editor opened with. */
  revision: number;
}

/** What the page sends for a step (the PC keeps the reputation goal and names the script itself). */
export interface QueueDraft {
  id?: string;
  scriptId: string;
  goalItem: string;
  goalQuantity: number;
  maxMinutes: number | null;
}

const MAX_QUEUES = 20;
const MAX_STEPS = 20;
const MAX_QUANTITY = 1_000_000;
const STATES: readonly string[] = ['Stopped', 'Running', 'Paused', 'Finished'];
const PHASES: readonly string[] = ['Starting', 'Running', 'Stopping', 'Settling'];
const STEP_ID = /^[0-9a-f]{32}$/;
const SCRIPT_ID = /^[0-9a-f]{16}$/;

function parseStep(v: unknown): QueueStep | null {
  if (!isObject(v)) return null;
  const quantity = count(v.goalQuantity);
  if (typeof v.id !== 'string' || !STEP_ID.test(v.id) || typeof v.scriptId !== 'string' || !SCRIPT_ID.test(v.scriptId)) return null;
  if (typeof v.scriptName !== 'string' || typeof v.goalItem !== 'string' || quantity === null || quantity < 1 || quantity > MAX_QUANTITY) return null;
  const rep = v.reputation;
  const rank = isObject(rep) ? count(rep.rank) : null;
  return {
    id: v.id,
    scriptId: v.scriptId,
    scriptName: cut(v.scriptName),
    goalItem: cut(v.goalItem),
    goalQuantity: quantity,
    reputation: isObject(rep) && typeof rep.faction === 'string' && rank !== null ? { faction: cut(rep.faction), rank } : null,
    maxMinutes: count(v.maxMinutes),
  };
}

function parseQueue(v: unknown): QueueModel | null {
  if (!isObject(v) || typeof v.character !== 'string' || typeof v.state !== 'string' || !STATES.includes(v.state)) return null;
  const steps: QueueStep[] = [];
  for (const s of Array.isArray(v.steps) ? (v.steps as unknown[]).slice(0, MAX_STEPS) : []) {
    const step = parseStep(s);
    if (step) steps.push(step);
  }
  return {
    character: cut(v.character),
    steps,
    cursor: count(v.cursor) ?? 0,
    state: v.state as QueueStateName,
    phase: typeof v.phase === 'string' && PHASES.includes(v.phase) ? (v.phase as QueuePhaseName) : null,
    reason: typeof v.reason === 'string' ? cut(v.reason) : null,
    have: count(v.have),
    canEdit: v.canEdit !== false,
    revision: count(v.revision) ?? 0,
  };
}

/** The queues of the PC's state: at most 20, a damaged one left out on its own; anything but an array is none. */
export function parseQueues(v: unknown): QueueModel[] {
  if (!Array.isArray(v)) return [];
  const queues: QueueModel[] = [];
  for (const q of (v as unknown[]).slice(0, MAX_QUEUES)) {
    const queue = parseQueue(q);
    if (queue) queues.push(queue);
  }
  return queues;
}

export function queueFor(queues: readonly QueueModel[], character: string | null): QueueModel | null {
  if (!character) return null;
  const wanted = character.toLowerCase();
  return queues.find((q) => q.character.toLowerCase() === wanted) ?? null;
}

export const queueApi = {
  get: (instanceId: string) => skuaCall(skuaPath(instanceId, 'queue')),
  replace: (instanceId: string, steps: QueueDraft[], revision: number) => skuaCall(skuaPath(instanceId, 'queue'), { revision, steps }, 'PUT'),
  command: (instanceId: string, word: QueueWord) => skuaCall(skuaPath(instanceId, 'queue', word), {}),
};

export interface QueueCard {
  state: string;
  step: string | null;
  progress: string | null;
  reason: string | null;
  start: boolean;
  pause: boolean;
  continue: boolean;
  skip: boolean;
  stop: boolean;
  edit: boolean;
  error: string | null;
  pending: boolean;
  empty: boolean;
}

const STATE_TEXT: Record<QueueStateName, MessageKey> = {
  Stopped: 'queue.state.stopped',
  Running: 'queue.state.running',
  Paused: 'queue.state.paused',
  Finished: 'queue.state.finished',
};
const PHASE_TEXT: Record<QueuePhaseName, MessageKey> = {
  Starting: 'queue.phase.starting',
  Running: 'queue.phase.running',
  Stopping: 'queue.phase.stopping',
  Settling: 'queue.phase.settling',
};

/** The card's words and which buttons work. Pending (a command is on its way) turns every button off. */
export function queueCardModel(q: QueueModel | null, pending: boolean, error: string | null): QueueCard {
  if (!q) {
    return { state: t(STATE_TEXT.Stopped), step: null, progress: null, reason: null, start: false, pause: false, continue: false, skip: false, stop: false, edit: !pending, error, pending, empty: true };
  }
  const running = q.state === 'Running';
  const active = running || q.state === 'Paused';
  const current = q.state === 'Finished' ? undefined : q.steps[q.cursor];
  const state = running && q.phase ? t('queue.status', { state: t(STATE_TEXT.Running), phase: t(PHASE_TEXT[q.phase]) }) : t(STATE_TEXT[q.state]);
  return {
    state,
    step: current ? t('queue.step', { n: q.cursor + 1, total: q.steps.length, script: current.scriptName }) : null,
    progress: current
      ? q.have === null ? t('queue.goalOnly', { goal: current.goalQuantity, item: current.goalItem }) : t('queue.goal', { have: q.have, goal: current.goalQuantity, item: current.goalItem })
      : null,
    reason: q.reason,
    start: !pending && q.steps.length > 0 && (q.state === 'Stopped' || q.state === 'Finished'),
    pause: !pending && running,
    continue: !pending && q.state === 'Paused',
    skip: !pending && active,
    stop: !pending && active,
    edit: !pending && q.canEdit && !(running && q.phase !== 'Running'),
    error,
    pending,
    empty: q.steps.length === 0,
  };
}

/** Skip and Stop ask first; Start, Pause and Continue go at once. */
export const needsConfirm = (word: QueueWord): word is 'skip' | 'stop' => word === 'skip' || word === 'stop';

/** The words of the confirmation sheet: the character and the current step (Skip) or the queue (Stop). */
export function confirmWords(word: 'skip' | 'stop', q: QueueModel | null, character: string): { title: string; body: string; confirm: string } {
  const script = q?.steps[q.cursor]?.scriptName ?? '';
  return word === 'skip'
    ? { title: t('queue.skipTitle', { script, character }), body: t('queue.skipBody'), confirm: t('queue.skip') }
    : { title: t('queue.stopTitle', { character }), body: t('queue.stopBody'), confirm: t('queue.stop') };
}

type Command = Pick<typeof queueApi, 'command'>;

/** Runs the five commands, one at a time per Skua, and keeps the words of the last failure. */
export class QueueUi {
  private readonly pending = new Set<string>();
  private readonly errors = new Map<string, string>();

  constructor(private readonly api: Command, private readonly changed: () => void, private readonly unpaired: () => void) {}

  isPending(instanceId: string): boolean {
    return this.pending.has(instanceId);
  }

  /** Shows words in the card's error line (a failure that did not come from a command). */
  setError(instanceId: string, words: string): void {
    this.errors.set(instanceId, words);
    this.changed();
  }

  errorOf(instanceId: string): string | null {
    return this.errors.get(instanceId) ?? null;
  }

  async run(instanceId: string, word: QueueWord): Promise<void> {
    if (this.pending.has(instanceId)) return;
    this.pending.add(instanceId);
    this.errors.delete(instanceId);
    this.changed();
    try {
      const r = await this.api.command(instanceId, word);
      const words = r.status === 200 ? null : this.failure(r);
      if (words) this.errors.set(instanceId, words);
    } catch {
      this.errors.set(instanceId, t('control.error.generic'));
    } finally {
      this.pending.delete(instanceId);
      this.changed();
    }
  }

  private failure(r: CallResult): string | null {
    if (r.status === 401) {
      this.unpaired();
      return null;
    }
    if (r.status === 504) return t('control.error.noAnswer');
    if (r.status === 0) return t('control.error.unreachable');
    if (r.status === 404) return t('control.error.gone');
    const error = isObject(r.data) && typeof r.data.error === 'string' ? cut(r.data.error) : '';
    if (r.status === 409) {
      if (error === 'busy') return t('control.error.busy');
      return error && error !== 'Skua is disconnected' ? t('queue.error.refused', { reason: error }) : t('control.error.disconnected');
    }
    if (r.status === 400 && error) return t('queue.error.refused', { reason: error });
    return t('control.error.generic');
  }
}
