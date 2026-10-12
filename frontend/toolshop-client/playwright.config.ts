import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests run against a REAL backend connected to a DEDICATED test database
 * (database name must end with "_test"). Start everything with backend/scripts/Run-E2E.ps1.
 * Never point these tests at the real "toolshop_crm" database.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1, // shared database: tests run one at a time
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [["list"], ["html", { open: "never", outputFolder: "e2e-report" }]],
  outputDir: "e2e-results",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        // Optional: use an already installed Chromium/Chrome instead of the Playwright-managed one.
        launchOptions: process.env.E2E_CHROMIUM_PATH ? { executablePath: process.env.E2E_CHROMIUM_PATH } : {},
      },
    },
  ],
});
