import { expect, type Page, type TestInfo } from "@playwright/test";
import { translations } from "../src/i18n/translations";
import { mockApi, type MockOptions } from "./mockApi";

export const T = (key: string): string => translations.en[key] ?? key;

export function isTouchLayout(page: Page): boolean {
  return (page.viewportSize()?.width ?? 1280) < 1280;
}
export function isDrawerLayout(page: Page): boolean {
  return (page.viewportSize()?.width ?? 1280) < 1024;
}

/** English UI, mocked API, signed in. Returns the request log. */
export async function signIn(page: Page, opts: MockOptions = {}) {
  await page.addInitScript(() => localStorage.setItem("toolshop_lang", "en"));
  const log = await mockApi(page, opts);
  await page.goto("/login");
  await page.locator('input[type="text"]').first().fill("demo");
  await page.locator('input[type="password"]').first().fill("demo-password-not-real");
  await page.locator('button[type="submit"]').click();
  await expect(page.getByRole("button", { name: T("common.logout") })).toBeVisible();
  return log;
}

export async function shot(page: Page, testInfo: TestInfo, name: string) {
  await page.screenshot({ path: `e2e-pwa-shots/${testInfo.project.name}/${name}.png`, fullPage: false });
}

export async function expectNoHorizontalOverflow(page: Page, label: string) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(over, `${label}: page is ${over}px wider than the screen`).toBeLessThanOrEqual(1);
}
