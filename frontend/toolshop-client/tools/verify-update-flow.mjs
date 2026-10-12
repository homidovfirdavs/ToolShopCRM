// Verifies the "new version" flow end to end against two real production builds:
//   v1 is installed -> v2 is deployed -> the page offers "Reload" (no silent reload) -> Reload activates v2.
// Needs: npm run build tooling, Chromium (set E2E_CHROMIUM_PATH to use an installed one). No backend needed.
//   node tools/verify-update-flow.mjs
import { chromium } from "@playwright/test";
import { spawn, execSync } from "node:child_process";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { mockApi } from "../e2e-pwa/mockApi.ts";

const PORT = 4175;
const sh = (cmd, env = {}) => execSync(cmd, { stdio: "pipe", env: { ...process.env, ...env } });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

sh("npm run build", { APP_BUILD_ID: "v1" });
// Run vite through node itself (no "npx"): "spawn npx" fails with ENOENT on Windows, and without a shell kill() really stops it.
const viteBin = join(dirname(createRequire(import.meta.url).resolve("vite/package.json")), "bin", "vite.js");
const server = spawn(process.execPath, [viteBin, "preview", "--port", String(PORT), "--strictPort"], { stdio: "ignore" });
await sleep(2500);

const browser = await chromium.launch(process.env.E2E_CHROMIUM_PATH ? { executablePath: process.env.E2E_CHROMIUM_PATH } : {});
const results = [];
const check = (name, ok) => { results.push([name, ok]); console.log(ok ? "PASS" : "FAIL", name); };

try {
  const context = await browser.newContext({ baseURL: `http://localhost:${PORT}` });
  const page = await context.newPage();
  await page.addInitScript(() => localStorage.setItem("toolshop_lang", "en"));
  await mockApi(page);
  await page.goto("/login");
  await page.locator('input[type="text"]').first().fill("demo");
  await page.locator('input[type="password"]').first().fill("demo-password-not-real");
  await page.locator('button[type="submit"]').click();
  await page.getByTestId("app-version").first().waitFor({ state: "attached" });
  await page.evaluate(() => navigator.serviceWorker.ready);
  check("v1 installed and running", (await page.getByTestId("app-version").first().textContent())?.includes("v1"));

  // Deploy v2 while the page is open.
  sh("npm run build", { APP_BUILD_ID: "v2" });
  await page.evaluate(async () => { const r = await navigator.serviceWorker.getRegistration(); await r?.update(); });
  await page.getByTestId("update-prompt").waitFor({ timeout: 15000 });
  check("update is offered (user decides when)", true);
  check("page was NOT reloaded silently (still v1)", (await page.getByTestId("app-version").first().textContent())?.includes("v1"));

  await page.getByRole("button", { name: "Reload", exact: true }).click();
  // The page reloads itself once the new worker takes control; wait until v2 is what is on screen.
  let v2 = true;
  try {
    await page.waitForFunction(() => document.querySelector('[data-testid="app-version"]')?.textContent?.includes("v2"), null, { timeout: 20000 });
  } catch {
    v2 = false;
  }
  check("after Reload v2 is running", v2);
} finally {
  await browser.close();
  server.kill();
}
process.exit(results.every(([, ok]) => ok) ? 0 : 1);
