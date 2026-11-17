import { addMessages, t, type MessageKey } from './i18n';
import { kindKey, pushEn } from './i18n/pushEn';
import { isIos } from './state';
import { h, mount, type VNode } from './views';
import { keyBytes, parsePushState, pushApi, pushSupport, PUSH_KINDS, type CallResult, type PushApi, type PushEnv, type PushKind, type PushState, type PushSupport } from './push';

addMessages(pushEn);

// The Notifications block of the Settings panel: a chunk of its own, loaded when the panel first opens (dockUi.ts).
export interface PushBrowser {
  requestPermission(): Promise<NotificationPermission>;
  subscribe(key: Uint8Array): Promise<{ endpoint: string; p256dh: string; auth: string } | null>;
  unsubscribe(): Promise<void>;
  /** Whether this browser holds a push subscription (presence only). */
  hasSubscription(): Promise<boolean>;
}
export interface PushNote {
  key: MessageKey;
  params?: Record<string, string>;
}
export interface PushModel {
  support: PushSupport;
  state: PushState | null;
  busy: boolean;
  note: PushNote | null;
  loadFailed: boolean;
}

const OFF: PushState = { subscribed: false, kinds: [], lastError: null };
const FAILED: PushNote = { key: 'push.pcFailed' };
/** A 503 is the PC asking for a moment (Forget in progress); anything else the PC could not do. */
const failure = (status: number): PushNote => (status === 503 ? { key: 'push.retryLater' } : FAILED);
const cut = (text: string): string => text.slice(0, 200);
const field = (data: unknown, name: string): unknown => (typeof data === 'object' && data !== null ? (data as Record<string, unknown>)[name] : undefined);

export class PushController {
  model: PushModel;
  private running = false;

  constructor(private readonly deps: { env: () => PushEnv; api: PushApi; browser: PushBrowser; onChange: () => void }) {
    this.model = { support: pushSupport(deps.env()), state: null, busy: false, note: null, loadFailed: false };
  }

  private set(change: Partial<PushModel>): void {
    this.model = { ...this.model, ...change };
    this.deps.onChange();
  }

  /** One action at a time: a second tap while one runs does nothing. Never throws. */
  private async run(action: () => Promise<void>): Promise<void> {
    if (this.running) return;
    this.running = true;
    this.set({ busy: true, note: null });
    try {
      await action();
    } catch {
      this.set({ note: FAILED });
    } finally {
      this.running = false;
      this.set({ busy: false });
    }
  }

  private async load(): Promise<void> {
    const answer = await this.deps.api.state();
    const state = answer.status === 200 ? parsePushState(answer.data) : null;
    if (!state) {
      this.set({ loadFailed: this.model.state === null });
      return;
    }
    // The PC and this browser must agree: a PC that thinks this phone is subscribed while the browser holds nothing
    // (site data cleared, expired, another phone) offers Enable again. Not being able to ask the browser trusts the PC.
    const has = await this.deps.browser.hasSubscription().catch(() => state.subscribed);
    this.set({ state: state.subscribed && !has ? { ...state, subscribed: false } : state, loadFailed: false });
  }

  async refresh(): Promise<void> {
    try {
      const support = pushSupport(this.deps.env());
      this.set({ support });
      if (support === 'ready') await this.load();
    } catch {
      this.set({ loadFailed: this.model.state === null });
    }
  }

  async enable(): Promise<void> {
    await this.run(async () => {
      // The first call, before any await: iOS accepts the permission prompt only inside the tap.
      const permission = await this.deps.browser.requestPermission();
      if (permission !== 'granted') {
        // Only a refusal blocks; a dismissed prompt ('default') leaves Enable available.
        if (permission === 'denied') this.set({ support: 'denied' });
        return;
      }
      const key = await this.deps.api.key();
      const publicKey = field(key.data, 'publicKey');
      if (key.status !== 200 || typeof publicKey !== 'string') {
        this.set({ note: failure(key.status) });
        return;
      }
      const sub = await this.deps.browser.subscribe(keyBytes(publicKey));
      if (!sub) {
        this.set({ note: { key: 'push.unsupported' } });
        return;
      }
      const saved = await this.deps.api.subscribe(sub);
      const state = saved.status === 200 ? parsePushState(saved.data) : null;
      if (!state) {
        await this.deps.browser.unsubscribe().catch(() => undefined);
        this.set({ note: failure(saved.status) });
        return;
      }
      this.set({ state, loadFailed: false });
    });
  }

  async disable(): Promise<void> {
    await this.run(async () => {
      const answer = await this.deps.api.unsubscribe();
      if (answer.status < 200 || answer.status > 299) {
        // The PC still has it: stay on rather than show off while notifications keep coming.
        this.set({ note: failure(answer.status) });
        return;
      }
      await this.deps.browser.unsubscribe().catch(() => undefined);
      this.set({ state: OFF });
    });
  }

  async toggle(kind: PushKind, on: boolean): Promise<void> {
    await this.run(async () => {
      const current = new Set(this.model.state?.kinds ?? []);
      if (on) current.add(kind);
      else current.delete(kind);
      const answer = await this.deps.api.setKinds(PUSH_KINDS.filter((k) => current.has(k)));
      const state = answer.status === 200 ? parsePushState(answer.data) : null;
      if (state) this.set({ state });
      else this.set({ note: failure(answer.status) });
    });
  }

  async test(): Promise<void> {
    await this.run(async () => {
      const answer: CallResult = await this.deps.api.test();
      if (answer.status === 200) {
        this.set({ note: { key: 'push.testSent' } });
        return;
      }
      if (answer.status === 429) {
        this.set({ note: { key: 'push.testWait' } });
        return;
      }
      if (answer.status === 0 || answer.status === 503) {
        this.set({ note: failure(answer.status) });
        return;
      }
      if (answer.status === 410) this.set({ note: { key: 'push.testGone' } });
      else {
        const error = field(answer.data, 'error');
        const text = typeof error === 'string' && error ? cut(error) : t('push.statusN', { n: answer.status });
        this.set({ note: { key: answer.status === 502 ? 'push.testUnreachable' : 'push.testFailed', params: { error: text } } });
      }
      await this.load();
    });
  }
}

const button = (action: string, label: MessageKey, busy = false): VNode =>
  h('button', { className: 'btn', text: t(label), attrs: { type: 'button', 'data-action': action, ...(busy ? { disabled: '' } : {}) } });
const line = (text: string, className = 'muted'): VNode => h('p', { className, text });

/** The text of the live region under the block: the last result, or nothing. Hostile text arrives through t() params and lands in textContent only. */
export const pushNoteText = (model: PushModel): string => (model.note ? t(model.note.key, model.note.params) : '');

/** The block for a model (the live region is drawn once by startPush, outside this tree). Every text goes through t(); hostile text lands in `text` only. */
export function pushBlockView(model: PushModel): VNode {
  const title = h('h3', { className: 'push-title', text: t('push.title') });
  const wrap = (...children: (VNode | null)[]): VNode => h('div', { className: 'push-body' }, title, ...children);
  if (model.support === 'needs-home-screen') return wrap(line(t('push.needsHomeScreen')));
  if (model.support === 'unsupported') return wrap(line(t('push.unsupported')));
  if (model.support === 'denied') return wrap(line(t('push.denied')));
  const { state, busy } = model;
  if (!state) return wrap(line(t(model.loadFailed ? 'push.loadFailed' : 'push.working')));
  if (!state.subscribed) return wrap(line(t('push.intro')), button('push-enable', 'push.enable', busy), line(t('push.limits')));
  return wrap(
    line(t('push.on')),
    h(
      'div',
      { className: 'push-kinds', attrs: { role: 'group', 'aria-label': t('push.kinds') } },
      ...PUSH_KINDS.map((kind) =>
        h(
          'label',
          { className: 'push-row' },
          h('input', {
            attrs: { type: 'checkbox', role: 'switch', 'data-action': 'push-kind', 'data-value': kind, ...(state.kinds.includes(kind) ? { checked: '' } : {}), ...(busy ? { disabled: '' } : {}) },
          }),
          h('span', { text: t(kindKey(kind)) }),
        ),
      ),
    ),
    button('push-test', 'push.test', busy),
    button('push-disable', 'push.disable', busy),
    state.lastError ? line(t('push.lastError', { error: state.lastError })) : null,
    state.lastError ? button('push-enable', 'push.resubscribe', busy) : null,
    line(t('push.limits')),
  );
}

/** A control the keyboard or a tap works on: what to find again after the block is redrawn. */
export interface Focusable {
  disabled: boolean;
  focus(): void;
}

/**
 * Keeps keyboard focus on the control the user used. Every action redraws the block, and while it runs the control is disabled (focus()
 * on it does nothing), so the key is remembered until the control is back and enabled; then focus returns to it once.
 */
export class FocusKeeper {
  private last: string | null = null;

  constructor(private readonly deps: { current(): string | null; find(key: string): Focusable | null }) {}

  /** The control the user just used. */
  activate(key: string): void {
    this.last = key;
  }

  /** Before the block is replaced: what has focus now. */
  before(): void {
    this.last = this.deps.current() ?? this.last;
  }

  /** After the block is replaced. */
  after(busy: boolean): void {
    if (!this.last) return;
    const el = this.deps.find(this.last);
    if (el && !el.disabled) el.focus();
    if (!busy) this.last = null;
  }
}

export function realBrowser(): PushBrowser {
  return {
    requestPermission: () => Notification.requestPermission(),
    async subscribe(key) {
      const reg = await navigator.serviceWorker.ready;
      // From scratch on purpose: a subscription made with an older VAPID key (after Forget my data) would be refused for ever.
      await (await reg.pushManager.getSubscription())?.unsubscribe();
      const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key as BufferSource });
      const json = sub.toJSON();
      const { p256dh, auth } = json.keys ?? {};
      return json.endpoint && p256dh && auth ? { endpoint: json.endpoint, p256dh, auth } : null;
    },
    async unsubscribe() {
      const reg = await navigator.serviceWorker.ready;
      await (await reg.pushManager.getSubscription())?.unsubscribe();
    },
    async hasSubscription() {
      // `ready` never settles without a registered worker: give up after a moment (the caller then trusts the PC).
      const reg = await Promise.race([navigator.serviceWorker.ready, new Promise<never>((_, no) => setTimeout(no, 3000))]);
      return (await reg.pushManager.getSubscription()) !== null;
    },
  };
}

export function realEnv(): PushEnv {
  const standalone = (navigator as Navigator & { standalone?: boolean }).standalone === true || matchMedia('(display-mode: standalone)').matches;
  return {
    ios: isIos(navigator.userAgent, navigator.maxTouchPoints ?? 0),
    standalone,
    hasServiceWorker: 'serviceWorker' in navigator,
    hasPushManager: typeof PushManager !== 'undefined',
    hasNotification: typeof Notification !== 'undefined',
    permission: typeof Notification === 'undefined' ? 'unsupported' : Notification.permission,
  };
}

const keyOf = (el: Element | null): string | null => (el?.getAttribute('data-action')?.startsWith('push-') ? `${el.getAttribute('data-action')}|${el.getAttribute('data-value') ?? ''}` : null);

/** Wires the block to the Settings panel: attach(slot) draws it into the slot each time the panel is mounted. */
export function startPush(host: HTMLElement): { attach(slot: HTMLElement): void } {
  let body: HTMLElement | null = null;
  let status: HTMLElement | null = null;
  const keeper = new FocusKeeper({
    current: () => keyOf(document.activeElement),
    find: (key) => {
      const [action, value = ''] = key.split('|');
      const found = Array.from(body?.querySelectorAll<HTMLInputElement | HTMLButtonElement>('[data-action]') ?? []).find((el) => el.getAttribute('data-action') === action && (el.getAttribute('data-value') ?? '') === value);
      return found ?? null;
    },
  });
  const render = (): void => {
    if (!body?.isConnected || !status) return;
    keeper.before();
    body.replaceChildren(mount(pushBlockView(controller.model)));
    status.textContent = pushNoteText(controller.model); // the live region stays the same element: only its text changes
    keeper.after(controller.model.busy);
  };
  const controller = new PushController({ env: realEnv, api: pushApi, browser: realBrowser(), onChange: render });
  host.addEventListener('click', (event) => {
    const target = (event.target as HTMLElement).closest<HTMLElement>('[data-action^="push-"]');
    const key = keyOf(target);
    if (!target || !key) return;
    keeper.activate(key);
    const action = target.getAttribute('data-action');
    if (action === 'push-enable') void controller.enable();
    else if (action === 'push-disable') void controller.disable();
    else if (action === 'push-test') void controller.test();
  });
  host.addEventListener('change', (event) => {
    const input = (event.target as HTMLElement).closest<HTMLInputElement>('[data-action="push-kind"]');
    const kind = input?.getAttribute('data-value');
    if (input && kind && (PUSH_KINDS as readonly string[]).includes(kind)) {
      keeper.activate(keyOf(input)!);
      void controller.toggle(kind as PushKind, input.checked);
    }
  });
  return {
    attach(slot) {
      body = document.createElement('div');
      status = mount(h('p', { className: 'muted', attrs: { role: 'status' } }));
      slot.replaceChildren(body, status);
      render();
      void controller.refresh();
    },
  };
}
