// Tiny external store for the PWA update state, so UI components do not import the
// "virtual:pwa-register" module (which only exists in a Vite build) and stay unit-testable.
type Listener = () => void;

interface PwaState {
  needRefresh: boolean;
}

let state: PwaState = { needRefresh: false };
let applyUpdate: (() => Promise<void>) | null = null;
const listeners = new Set<Listener>();

function emit() {
  for (const l of listeners) l();
}

export function subscribePwa(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getPwaSnapshot(): PwaState {
  return state;
}

export function setNeedRefresh(value: boolean, update?: () => Promise<void>) {
  if (update) applyUpdate = update;
  state = { needRefresh: value };
  emit();
}

/** Activates the waiting service worker and reloads the page. */
export async function applyPwaUpdate() {
  await applyUpdate?.();
}

export function dismissPwaUpdate() {
  state = { needRefresh: false };
  emit();
}
