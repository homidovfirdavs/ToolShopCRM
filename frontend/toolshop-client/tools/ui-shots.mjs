// Before/after screenshots of the key screens at 375, 768, 1366 and 1920 px (mocked API, no backend).
//   node tools/ui-shots.mjs <label>          builds into dist-<label>, serves it, writes ui-shots/<label>/*.png
// Includes the "COGS unknown" state (gross profit N/A + warning) so it can be compared before/after.
import { chromium } from "@playwright/test";
import { spawn, execSync } from "node:child_process";
import { createRequire } from "node:module";
import { mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { mockApi } from "../e2e-pwa/mockApi.ts";

const label = process.argv[2] ?? "after";
const PORT = 4190 + (label === "before" ? 0 : 1);
const outDir = `dist-${label}`;
const shotDir = `ui-shots/${label}`;
mkdirSync(shotDir, { recursive: true });

if (!process.argv.includes("--no-build")) execSync(`npm run build -- --outDir ${outDir}`, { stdio: "pipe", env: { ...process.env, APP_BUILD_ID: label } });
const viteBin = join(dirname(createRequire(import.meta.url).resolve("vite/package.json")), "bin", "vite.js");
const server = spawn(process.execPath, [viteBin, "preview", "--outDir", outDir, "--port", String(PORT), "--strictPort"], { stdio: "ignore" });
await new Promise((r) => setTimeout(r, 2500));

const VIEWPORTS = [
  { name: "375", width: 375, height: 812, mobile: true },
  { name: "768", width: 768, height: 1024, mobile: true },
  { name: "1366", width: 1366, height: 768, mobile: false },
  { name: "1920", width: 1920, height: 1080, mobile: false },
];

const browser = await chromium.launch(process.env.E2E_CHROMIUM_PATH ? { executablePath: process.env.E2E_CHROMIUM_PATH } : {});
try {
  for (const vp of VIEWPORTS) {
    const context = await browser.newContext({
      baseURL: `http://localhost:${PORT}`,
      viewport: { width: vp.width, height: vp.height },
      isMobile: vp.mobile,
      hasTouch: vp.mobile,
      serviceWorkers: "block",
    });
    const page = await context.newPage();
    await page.addInitScript(() => localStorage.setItem("toolshop_lang", "en"));
    await mockApi(page);
    await page.goto("/login");
    await page.locator('input[type="text"]').first().fill("demo");
    await page.locator('input[type="password"]').first().fill("demo-password-not-real");
    await page.locator('button[type="submit"]').click();
    await page.getByRole("button", { name: "Logout" }).waitFor();

    const snap = async (name, full = false) => {
      await page.waitForLoadState("networkidle");
      await page.waitForTimeout(900);
      await page.screenshot({ path: `${shotDir}/${vp.name}-${name}.png`, fullPage: full });
    };

    await page.goto("/dashboard");
    await page.locator("h1").first().waitFor();
    await snap("dashboard", true);

    // Sidebar: always visible on desktop; a drawer on phone/tablet.
    if (vp.width < 1024) {
      await page.getByRole("button", { name: /menu/i }).first().click();
      await page.waitForTimeout(400);
      await page.screenshot({ path: `${shotDir}/${vp.name}-sidebar.png` });
      await page.keyboard.press("Escape");
    } else {
      await page.screenshot({ path: `${shotDir}/${vp.name}-sidebar.png`, clip: { x: 0, y: 0, width: 360, height: Math.min(vp.height, 760) } });
    }

    // COGS unknown: gross profit is N/A, a warning is shown and the gross line is not drawn. Numbers are not altered by the UI.
    await page.route("http://localhost:5252/api/dashboard/summary*", (route) =>
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ dateFrom: "2026-09-12", dateTo: "2026-10-11", salesRevenue: 20290, grossProfit: null, paymentsReceived: 15090, accountsReceivable: 5200, lowStockCount: 3, operatingExpenses: 2605, hasCompleteCostData: false, outOfStockCount: 1, completedOrdersCount: 36 }) }));
    await page.route("http://localhost:5252/api/dashboard/financial-summary*", (route) =>
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ revenue: 20290, cogs: null, grossProfit: null, operatingExpenses: 2605, netOperatingProfit: null, hasCompleteCostData: false }) }));
    await page.goto("/dashboard");
    await page.locator("h1").first().waitFor();
    await snap("dashboard-cogs-na", true);
    await page.unroute("http://localhost:5252/api/dashboard/summary*");
    await page.unroute("http://localhost:5252/api/dashboard/financial-summary*");

    await page.goto("/pos");
    await page.getByRole("button", { name: /Makita Rotary Hammer/ }).first().waitFor();
    await page.getByRole("button", { name: /Makita Rotary Hammer/ }).first().click();
    await page.getByRole("button", { name: /Stanley Claw Hammer/ }).first().click();
    await snap("pos");
    if (vp.width < 1280) {
      await page.locator("#pos-cart").scrollIntoViewIfNeeded();
      await page.locator("#pos-cart").evaluate((el) => el.scrollIntoView({ block: "start" }));
      await snap("pos-cart");
    }

    for (const [name, path] of [["orders", "/orders"], ["debts", "/debts"], ["expenses", "/expenses"], ["inventory", "/inventory"]]) {
      await page.goto(path);
      await page.locator("h1").first().waitFor();
      await snap(name);
    }
    await context.close();
  }
} finally {
  await browser.close();
  server.kill();
}
console.log("done:", shotDir);
