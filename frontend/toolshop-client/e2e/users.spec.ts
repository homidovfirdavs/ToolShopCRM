import { test, expect, request } from "@playwright/test";
import { API_URL, Api, T, signedInPage, uid } from "./helpers";

let api: Api;

test.beforeEach(async ({ page }) => {
  const login = await Api.login();
  api = login.api;
  await signedInPage(page, login.refreshToken);
});
test.afterEach(async () => api?.dispose());

test("Admin creates a Cashier who can sign in, then deactivates them and sign-in stops working", async ({ page, browser, baseURL }) => {
  const username = `e2e_u_${uid()}`;
  const password = `Aa1-${uid()}x`;

  await page.goto("/users");
  await page.getByRole("button", { name: `+ ${T("users.add")}` }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel(new RegExp(`^${T("users.username")}`)).fill(username);
  await dialog.getByLabel(new RegExp(`^${T("users.fullName")}`)).fill("E2E Cashier");
  await dialog.getByLabel(new RegExp(`^${T("users.email")}`)).fill(`${username}@example.test`);
  await dialog.getByLabel(new RegExp(`^${T("users.role")}`)).selectOption("Cashier");
  await dialog.getByLabel(new RegExp(`^${T("users.password")}`)).fill(password);
  await dialog.getByRole("button", { name: T("common.save") }).click();

  const row = page.getByTestId(`user-row-${username}`);
  await expect(row).toBeVisible();
  await expect(row).toContainText(T("users.role.Cashier"));

  // the new user can sign in; a Cashier must not reach the Users page
  const ctx = await request.newContext({ baseURL: API_URL });
  const login = await ctx.post("/api/auth/login", { data: { usernameOrEmail: username, password } });
  expect(login.ok()).toBeTruthy();
  const { refreshToken } = (await login.json()) as { refreshToken: string };

  const cashierContext = await browser.newContext({ baseURL });
  const cashierPage = await cashierContext.newPage();
  await signedInPage(cashierPage, refreshToken);
  await cashierPage.goto("/users");
  await expect(cashierPage).not.toHaveURL(/\/users/);
  await cashierContext.close();

  // deactivate through the UI
  await row.getByRole("button", { name: T("common.deactivate") }).click();
  await page.getByRole("dialog").getByRole("button", { name: T("common.deactivate") }).click();
  await expect(row).toContainText(T("common.inactive"));

  const after = await ctx.post("/api/auth/login", { data: { usernameOrEmail: username, password } });
  expect(after.status()).toBe(401);
  await ctx.dispose();
});

test("the signed-in Admin cannot deactivate themselves", async ({ page }) => {
  await page.goto("/users");
  const meRow = page.getByTestId(`user-row-${process.env.E2E_ADMIN_USER}`);
  await expect(meRow).toBeVisible();
  await expect(meRow).toContainText(T("users.you"));
  await expect(meRow.getByRole("button", { name: T("common.deactivate") })).toHaveCount(0);
});
