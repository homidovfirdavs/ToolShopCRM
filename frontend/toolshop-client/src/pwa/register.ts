import { registerSW } from "virtual:pwa-register";
import { setNeedRefresh } from "./pwaStore";

const CHECK_EVERY_MS = 60 * 60 * 1000; // look for a new version once an hour

/** Registers the service worker. Production builds only (the dev server never installs one). */
export function registerPwa() {
  if (!("serviceWorker" in navigator)) return;

  const updateSW = registerSW({
    onNeedRefresh() {
      setNeedRefresh(true, async () => {
        // The waiting worker takes control after "skip waiting"; reload then so the page runs the
        // new version. Done here (not left to the library) so the reload always happens.
        navigator.serviceWorker.addEventListener("controllerchange", () => window.location.reload(), { once: true });
        await updateSW();
      });
    },
    onRegisteredSW(_url, registration) {
      if (!registration) return;
      window.setInterval(() => {
        if (navigator.onLine) void registration.update();
      }, CHECK_EVERY_MS);
    },
  });
}
