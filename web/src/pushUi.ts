import { addMessages, t, type MessageKey } from './i18n';
import { pushEn } from './i18n/pushEn';
import { isIos } from './state';
import { h, mount, type VNode } from './views';
import { keyBytes, parsePushState, pushApi, pushSupport, PUSH_KINDS, type CallResult, type PushApi, type PushEnv, type PushKind, type PushState, type PushSupport } from './push';

addMessages(pushEn);

// The Notifications block of the Settings panel: a chunk of its own, loaded when the panel first opens (dockUi.ts).
export interface PushBrowser {
  requestPermission(): Promise<NotificationPermission>;
  subscribe(key: Uint8Array): Promise<{ endpoint: string; p256dh: string; auth: string } | null>;
  unsubscribe(): Promise<void>;
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
    if (state) this.set({ state, loadFailed: false });
    else this.set({ loadFailed: this.model.state === null });
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
        this.set({ support: 'denied' });
        return;
      }
      const key = await this.deps.api.key();
      const publicKey = field(key.data, 'publicKey');
      if (key.status !== 200 || typeof publicKey !== 'string') {
        this.set({ note: FAILED });
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
        this.set({ note: FAILED });
        return;
      }
      this.set({ state, loadFailed: false });
    });
  }

  async disable(): Promise<void> {
    await this.run(async () => {
      await this.deps.api.unsubscribe().catch(() => undefined);
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
      else this.set({ note: FAILED });
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
      if (answer.status === 410) this.set({ note: { key: 'push.testGone' } });
      else {
        const error = field(answer.data, 'error');
        this.set({ note: { key: 'push.testFailed', params: { error: typeof error === 'string' && error ? cut(error) : `status ${answer.status}` } } });
      }
      await this.load();
    });
  }
}

const button = (action: string, label: MessageKey, busy = false): VNode =>
  h('button', { className: 'btn', text: t(label), attrs: { type: 'button', 'data-action': action, ...(busy ? { disabled: '' } : {}) } });
const line = (text: string, className = 'muted'): VNode => h('p', { className, text });

/** The block for a model. Every text goes through t(); hostile text (the push service's, the PC's) lands in `text` only. */
export function pushBlockView(model: PushModel): VNode {
  const title = line(t('push.title'));
  const wrap = (...children: (VNode | null)[]): VNode => h('div', { className: 'push-body' }, title, ...children);
  if (model.support === 'needs-home-screen') return wrap(line(t('push.needsHomeScreen')));
  if (model.support === 'unsupported') return wrap(line(t('push.unsupported')));
  if (model.support === 'denied') return wrap(line(t('push.denied')));
  const { state, busy } = model;
  if (!state) return wrap(line(t(model.loadFailed ? 'push.loadFailed' : 'push.working')));
  const note = model.note ? h('p', { className: 'muted', text: t(model.note.key, model.note.params), attrs: { role: 'status' } }) : null;
  if (!state.subscribed) return wrap(line(t('push.intro')), button('push-enable', 'push.enable', busy), note, line(t('push.limits')));
  return wrap(
    line(t('push.on')),
    line(t('push.kinds')),
    ...PUSH_KINDS.map((kind) =>
      h(
        'label',
        { className: 'push-row' },
        h('input', {
          attrs: { type: 'checkbox', role: 'switch', 'data-action': 'push-kind', 'data-value': kind, ...(state.kinds.includes(kind) ? { checked: '' } : {}), ...(busy ? { disabled: '' } : {}) },
        }),
        h('span', { text: t(`push.kind.${kind}` as MessageKey) }),
      ),
    ),
    button('push-test', 'push.test', busy),
    button('push-disable', 'push.disable', busy),
    state.lastError ? line(t('push.lastError', { error: state.lastError })) : null,
    state.lastError ? button('push-enable', 'push.resubscribe', busy) : null,
    note,
    line(t('push.limits')),
  );
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

/** Wires the block to the Settings panel: attach(slot) draws it into the slot each time the panel is mounted. */
export function startPush(host: HTMLElement): { attach(slot: HTMLElement): void } {
  let current: HTMLElement | null = null;
  const render = (): void => {
    if (!current || !current.isConnected) return;
    const focused = document.activeElement instanceof HTMLElement && current.contains(document.activeElement) ? document.activeElement : null;
    const action = focused?.getAttribute('data-action');
    const value = focused?.getAttribute('data-value');
    current.replaceChildren(mount(pushBlockView(controller.model)));
    if (action) {
      const same = Array.from(current.querySelectorAll<HTMLElement>('[data-action]')).find((el) => el.getAttribute('data-action') === action && el.getAttribute('data-value') === value);
      same?.focus();
    }
  };
  const controller = new PushController({ env: realEnv, api: pushApi, browser: realBrowser(), onChange: render });
  host.addEventListener('click', (event) => {
    const target = (event.target as HTMLElement).closest<HTMLElement>('[data-action^="push-"]');
    if (!target || target.getAttribute('data-action') === 'push-kind') return;
    const action = target.getAttribute('data-action');
    if (action === 'push-enable') void controller.enable();
    else if (action === 'push-disable') void controller.disable();
    else if (action === 'push-test') void controller.test();
  });
  host.addEventListener('change', (event) => {
    const input = (event.target as HTMLElement).closest<HTMLInputElement>('[data-action="push-kind"]');
    const kind = input?.getAttribute('data-value');
    if (input && kind && (PUSH_KINDS as readonly string[]).includes(kind)) void controller.toggle(kind as PushKind, input.checked);
  });
  return {
    attach(slot) {
      current = slot;
      render();
      void controller.refresh();
    },
  };
}
