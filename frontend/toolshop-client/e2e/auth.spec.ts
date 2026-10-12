import { test, expect } from "@playwright/test";
import { T, adminPassword, adminUser } from "./helpers";

test.describe("Login", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("toolshop_lang", "en"));
  });

  test("wrong password shows an error and stays on the login page", async ({ page }) => {
    await page.goto("/login");
    await page.locator('input[type="text"]').fill(adminUser());
    await page.locator('input[type="password"]').fill("definitely-wrong-password-1");
    await page.getByRole("button", { name: T("login.submit") }).click();
    await expect(page.getByRole("alert")).toContainText(T("login.errInvalid"));
    await expect(page).toHaveURL(/\/login/);
  });

  test("valid credentials open the application", async ({ page }) => {
    await page.goto("/login");
    await page.locator('input[type="text"]').fill(adminUser());
    await page.locator('input[type="password"]').fill(adminPassword());
    await page.getByRole("button", { name: T("login.submit") }).click();
    await expect(page).not.toHaveURL(/\/login/);
  });

  test("protected pages redirect anonymous users to login", async ({ page }) => {
    await page.goto("/pos");
    await expect(page).toHaveURL(/\/login/);
  });
});
