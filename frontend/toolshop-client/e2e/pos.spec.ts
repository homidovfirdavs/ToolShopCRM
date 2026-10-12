import { test, expect, type Page } from "@playwright/test";
import { Api, T, formatMoney, signedInPage } from "./helpers";

let api: Api;
let refreshToken: string;

test.beforeEach(async ({ page }) => {
  const login = await Api.login();
  api = login.api;
  refreshToken = login.refreshToken;
  await signedInPage(page, refreshToken);
});
test.afterEach(async () => api?.dispose());

async function addToCart(page: Page, sku: string, name: string) {
  await page.goto("/pos");
  await page.getByPlaceholder(T("pos.searchProducts")).fill(sku);
  // product card (first match: card comes before the cart line)
  await page.getByRole("button", { name: new RegExp(name) }).first().click();
}
const complete = (page: Page) => page.getByRole("button", { name: `${T("pos.complete")} (F12)` });
const confirm = (page: Page) => page.getByRole("button", { name: T("pos.confirmSale"), exact: true });

test("walk-in: full cash sale completes and stock drops by exactly the quantity", async ({ page }) => {
  const p = await api.seedProduct({ price: 100, stock: 10 });
  await addToCart(page, p.sku, p.name);
  await complete(page).click();
  await confirm(page).click();
  await expect(page.getByText(T("pos.saleDone"))).toBeVisible();
  expect(await api.stockOf(p.id)).toBe(9);
});

test("walk-in: partial and pay-later options are disabled", async ({ page }) => {
  const p = await api.seedProduct();
  await addToCart(page, p.sku, p.name);
  await expect(page.getByLabel(T("pos.payPartial"))).toBeDisabled();
  await expect(page.getByLabel(T("pos.payLater"))).toBeDisabled();
  await expect(page.getByLabel(T("pos.payFull"))).toBeEnabled();
});

test("customer: partial payment leaves exactly the remainder as debt", async ({ page }) => {
  const p = await api.seedProduct({ price: 100, stock: 10 });
  const c = await api.seedCustomer();
  await addToCart(page, p.sku, p.name);
  await page.getByPlaceholder(T("pos.searchCustomer")).fill(c.name);
  await page.getByRole("button", { name: new RegExp(c.name) }).click();
  await page.getByLabel(T("pos.payPartial")).check();
  await page.getByPlaceholder(T("pos.amountNow")).fill("40");
  await complete(page).click();
  await confirm(page).click();
  await expect(page.getByText(T("pos.saleDone"))).toBeVisible();
  await expect(page.getByText(formatMoney(60)).first()).toBeVisible(); // debt on the receipt
  expect(await api.debtOf(c.name)).toBe(60);
  expect(await api.stockOf(p.id)).toBe(9);
});

test("insufficient stock: sale is rejected, message shown, stock and debts unchanged", async ({ page }) => {
  const p = await api.seedProduct({ price: 100, stock: 1 });
  await addToCart(page, p.sku, p.name);
  await page.getByRole("button", { name: "+", exact: true }).click(); // quantity 2 > stock 1
  await complete(page).click();
  await confirm(page).click();
  await expect(page.getByRole("alert")).toContainText(T("pos.errStock"));
  await expect(page.getByText(T("pos.saleDone"))).toHaveCount(0);
  expect(await api.stockOf(p.id)).toBe(1);
});

test("discount larger than the subtotal is rejected before any request", async ({ page }) => {
  const p = await api.seedProduct({ price: 100, stock: 5 });
  await addToCart(page, p.sku, p.name);
  await page.locator('input[type="number"][step="0.01"]').fill("9999");
  await complete(page).click();
  await expect(page.getByRole("alert")).toContainText(T("pos.errDiscount"));
  expect(await api.stockOf(p.id)).toBe(5);
});
