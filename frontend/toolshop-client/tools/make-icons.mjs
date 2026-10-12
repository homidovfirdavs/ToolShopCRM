// Renders public/icon.svg and public/icon-maskable.svg to the PNG sizes the PWA manifest needs.
// Usage: node tools/make-icons.mjs   (set E2E_CHROMIUM_PATH to use an installed Chrome/Chromium)
import { chromium } from "@playwright/test";
import { readFileSync, writeFileSync } from "node:fs";

const jobs = [
  ["public/icon.svg", "public/pwa-192x192.png", 192, true],
  ["public/icon.svg", "public/pwa-512x512.png", 512, true],
  ["public/icon-maskable.svg", "public/pwa-maskable-512x512.png", 512, false],
  ["public/icon-maskable.svg", "public/apple-touch-icon.png", 180, false], // iOS applies its own rounding; must be opaque
];

const browser = await chromium.launch(process.env.E2E_CHROMIUM_PATH ? { executablePath: process.env.E2E_CHROMIUM_PATH } : {});
for (const [src, out, size, transparent] of jobs) {
  const svg = readFileSync(src, "utf8");
  const page = await browser.newPage({ viewport: { width: size, height: size } });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace(/width="512" height="512"/, `width="${size}" height="${size}"`)}</body></html>`);
  writeFileSync(out, await page.screenshot({ omitBackground: transparent, type: "png" }));
  await page.close();
  console.log("wrote", out);
}
await browser.close();
