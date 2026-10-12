import { defineConfig, devices, type PlaywrightTestConfig } from "@playwright/test";

/**
 * PWA / responsive / offline checks against the PRODUCTION BUILD (so the service worker is real)
 * with a mocked API (e2e-pwa/mockApi.ts). No backend or database is needed.
 *
 *   npm run build
 *   npx playwright test -c playwright.pwa.config.ts
 *
 * These projects EMULATE platforms (viewport, touch, user agent) in Chromium. They are not real
 * Windows / macOS / Android / iOS devices. WebKit (Safari engine) projects are added when
 * PW_WEBKIT=1 and the browser is installed (npx playwright install webkit).
 */
const chromePath = process.env.E2E_CHROMIUM_PATH;
const launchOptions = chromePath ? { executablePath: chromePath } : {};

const chromium = (device: string, extra: Record<string, unknown> = {}) => ({
  ...devices[device],
  browserName: "chromium" as const,
  launchOptions,
  ...extra,
});

const projects: PlaywrightTestConfig["projects"] = [
  {
    name: "windows-desktop",
    use: {
      ...devices["Desktop Chrome"],
      launchOptions,
      viewport: { width: 1366, height: 768 },
      userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
    },
  },
  {
    name: "macos-desktop",
    use: {
      ...devices["Desktop Chrome"],
      launchOptions,
      viewport: { width: 1440, height: 900 },
      userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
    },
  },
  { name: "android-phone", use: chromium("Pixel 7") },
  { name: "android-tablet", use: chromium("Galaxy Tab S4") },
  { name: "ios-phone-emulated", use: chromium("iPhone 14") },
  { name: "ios-tablet-emulated", use: chromium("iPad (gen 7)") },
];

if (process.env.PW_WEBKIT === "1") {
  projects.push(
    { name: "ios-phone-webkit", use: { ...devices["iPhone 14"] } },
    { name: "ios-tablet-webkit", use: { ...devices["iPad (gen 7)"] } },
    { name: "macos-safari-webkit", use: { ...devices["Desktop Safari"] } },
  );
}

export default defineConfig({
  testDir: "./e2e-pwa",
  fullyParallel: true,
  workers: 2,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [["list"], ["html", { open: "never", outputFolder: "e2e-pwa-report" }]],
  outputDir: "e2e-pwa-results",
  use: {
    baseURL: "http://localhost:4173",
    serviceWorkers: "allow",
    trace: "retain-on-failure",
  },
  webServer: {
    command: "npm run preview -- --port 4173 --strictPort",
    url: "http://localhost:4173",
    reuseExistingServer: true,
    timeout: 60_000,
  },
  projects,
});
