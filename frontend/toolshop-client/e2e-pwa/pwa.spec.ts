import { test, expect, type Page } from "@playwright/test";
import { T, shot, signIn } from "./helpers";
import { mockApi } from "./mockApi";

async function waitForServiceWorker(page: Page) {
  await page.evaluate(async () => {
    const reg = await navigator.serviceWorker.ready;
    if (!reg.active) throw new Error("no active service worker");
  });
}

test.describe("installability", () => {
  test("web app manifest and icons", async ({ page, request }) => {
    await page.goto("/");
    const href = await page.locator('link[rel="manifest"]').getAttribute("href");
    expect(href).toBeTruthy();
    const res = await request.get(href!);
    expect(res.ok()).toBeTruthy();
    const m = await res.json();
    expect(m.name).toBe("ToolShop CRM");
    expect(m.short_name).toBe("ToolShop");
    expect(m.display).toBe("standalone");
    expect(m.start_url).toBe("/");
    expect(m.scope).toBe("/");
    expect(m.theme_color).toMatch(/^#/);
    expect(m.background_color).toMatch(/^#/);
    const sizes = (m.icons as { sizes: string; purpose?: string }[]).map((i) => `${i.sizes}:${i.purpose ?? "any"}`);
    expect(sizes).toEqual(expect.arrayContaining(["192x192:any", "512x512:any", "512x512:maskable"]));
    for (const icon of m.icons as { src: string; type: string }[]) {
      const r = await request.get("/" + icon.src);
      expect(r.ok(), icon.src).toBeTruthy();
      expect(r.headers()["content-type"]).toContain("image/png");
    }
  });

  test("head tags for desktop, Android and iOS install", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute("content", "#0f172a");
    await expect(page.locator('meta[name="viewport"]')).toHaveAttribute("content", /viewport-fit=cover/);
    await expect(page.locator('meta[name="apple-mobile-web-app-capable"]')).toHaveAttribute("content", "yes");
    await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute("href", "/apple-touch-icon.png");
    await expect(page).toHaveTitle("ToolShop CRM");
  });

  test("install entry: browser prompt (Chrome/Edge/Android) or Add to Home Screen steps (iOS)", async ({ page }, testInfo) => {
    await signIn(page);
    const btn = page.getByTestId("install-button");
    if (testInfo.project.name.startsWith("ios")) {
      await expect(btn).toBeVisible();
      await btn.click();
      await expect(page.getByRole("dialog", { name: T("pwa.iosTitle") })).toBeVisible();
      await expect(page.getByText(T("pwa.iosStep2"))).toBeVisible();
      await shot(page, testInfo, "install-ios-steps");
    } else {
      await expect(btn).toHaveCount(0); // nothing to offer until the browser says the app is installable
      await page.evaluate(() => {
        const ev = new Event("beforeinstallprompt", { cancelable: true });
        Object.assign(ev, { prompt: async () => { (window as unknown as { __prompted: boolean }).__prompted = true; }, userChoice: Promise.resolve({ outcome: "accepted" }) });
        window.dispatchEvent(ev);
      });
      await expect(btn).toBeVisible();
      await shot(page, testInfo, "install-button");
      await btn.click();
      expect(await page.evaluate(() => (window as unknown as { __prompted?: boolean }).__prompted)).toBe(true);
    }
  });
});

test.describe("service worker: static shell only", () => {
  test("registers, precaches the app shell and never caches API data, tokens or user data", async ({ page }) => {
    await signIn(page);
    await waitForServiceWorker(page);
    // visit data-heavy pages so any (wrong) runtime caching would have a chance to happen
    for (const route of ["/orders", "/finance", "/dashboard", "/inventory"]) {
      await page.goto(route);
      await page.waitForLoadState("networkidle");
    }
    const report = await page.evaluate(async () => {
      const urls: string[] = [];
      for (const name of await caches.keys()) {
        const cache = await caches.open(name);
        for (const req of await cache.keys()) urls.push(req.url);
      }
      const jwtLike = /^[\w-]+\.[\w-]+\.[\w-]*$/;
      const stored: string[] = [];
      for (const store of [localStorage, sessionStorage]) {
        for (let i = 0; i < store.length; i++) stored.push(store.getItem(store.key(i)!) ?? "");
      }
      const dbs = (await indexedDB.databases?.()) ?? [];
      return { urls, jwtInStorage: stored.some((v) => jwtLike.test(v)), dbs: dbs.map((d) => d.name) };
    });
    expect(report.urls.length).toBeGreaterThan(10);
    expect(report.urls.some((u) => /index\.html(\?|$)/.test(u))).toBe(true);
    expect(report.urls.filter((u) => /\/api\/|:5252|\/health\//.test(u))).toEqual([]); // no API response cached
    expect(report.urls.filter((u) => /\.(json)$/.test(u) && !/manifest/.test(u))).toEqual([]);
    expect(report.jwtInStorage, "access token (JWT) must never be persisted").toBe(false);
    expect(report.dbs, "no IndexedDB data store").toEqual([]);
  });

  test("API requests always go to the network (service worker does not answer them)", async ({ page }) => {
    const log = await signIn(page);
    await waitForServiceWorker(page);
    await page.reload(); // now controlled by the service worker
    await page.goto("/orders");
    await page.waitForLoadState("networkidle");
    const before = log.filter((l) => l.method === "GET" && l.path === "/orders").length;
    await page.reload();
    await page.waitForLoadState("networkidle");
    const after = log.filter((l) => l.method === "GET" && l.path === "/orders").length;
    expect(after).toBeGreaterThan(before); // fresh request, not a cached answer
  });
});

test.describe("offline behaviour", () => {
  test("write actions are blocked while offline and work again when the connection returns", async ({ page, context }, testInfo) => {
    const log = await signIn(page);
    await page.goto("/pos");
    await page.getByRole("button", { name: /Makita Rotary Hammer/ }).first().click();
    const complete = page.getByRole("button", { name: `${T("pos.complete")} (F12)` });
    await expect(complete).toBeEnabled();

    await context.setOffline(true);
    await expect(page.getByTestId("offline-banner")).toBeVisible();
    await expect(complete).toBeDisabled();
    await shot(page, testInfo, "offline-pos-blocked");

    // the keyboard shortcut cannot start a sale either: it shows the offline message instead
    await page.keyboard.press("F12");
    await expect(page.getByRole("alert").filter({ hasText: T("offline.blocked") })).toBeVisible();

    const writes = () => log.filter((l) => l.method !== "GET" && !l.path.startsWith("/auth/"));
    expect(writes()).toEqual([]);

    await context.setOffline(false);
    await expect(page.getByTestId("offline-banner")).toHaveCount(0);
    await expect(complete).toBeEnabled();
  });

  test("the app shell still opens offline (no stale business data is shown)", async ({ page, context }, testInfo) => {
    await mockApi(page);
    await page.addInitScript(() => localStorage.setItem("toolshop_lang", "en"));
    await page.goto("/login");
    await waitForServiceWorker(page);
    await page.reload(); // make sure the page is controlled by the worker
    await context.setOffline(true);
    await page.reload();
    await expect(page.locator('input[type="password"]').first()).toBeVisible(); // shell rendered from the precache
    await expect(page).toHaveTitle("ToolShop CRM");
    await shot(page, testInfo, "offline-shell");

    // signing in needs the server
    const log = await mockApi(page);
    await page.locator('input[type="text"]').first().fill("demo");
    await page.locator('input[type="password"]').first().fill("demo-password-not-real");
    await page.locator('button[type="submit"]').click();
    await page.waitForTimeout(400);
    expect(log.filter((l) => l.path === "/auth/login")).toEqual([]);
    await context.setOffline(false);
  });
});
