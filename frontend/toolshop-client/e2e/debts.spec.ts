import { test, expect } from "@playwright/test";
import { Api, T, formatMoney, signedInPage } from "./helpers";

let api: Api;

test.beforeEach(async ({ page }) => {
  const login = await Api.login();
  api = login.api;
  await signedInPage(page, login.refreshToken);
});
test.afterEach(async () => api?.dispose());

test("a customer's unpaid balance appears on the Debts page with the exact amount", async ({ page }) => {
  const p = await api.seedProduct({ price: 500, stock: 5 });
  const c = await api.seedCustomer();
  await api.sell(p.id, 1, c.id, 200); // total 500, paid 200 -> debt 300
  await page.goto("/debts");
  await page.getByPlaceholder(T("debt.search")).fill(c.name);
  const row = page.getByRole("row", { name: new RegExp(c.name) });
  await expect(row).toBeVisible();
  await expect(row).toContainText(formatMoney(300));
});

test("debt = order total - valid payments: paying the rest removes the customer from Debts", async ({ page }) => {
  const p = await api.seedProduct({ price: 500, stock: 5 });
  const c = await api.seedCustomer();
  const sale = await api.sell(p.id, 1, c.id, 200);
  expect(await api.debtOf(c.name)).toBe(300);

  await api.post("/api/payments", { orderId: sale.orderId, amount: 300, method: "Cash", referenceNumber: "", notes: "" });
  expect(await api.debtOf(c.name)).toBe(0);

  await page.goto("/debts");
  await page.getByPlaceholder(T("debt.search")).fill(c.name);
  await expect(page.getByText(T("debt.empty"))).toBeVisible();
});

test("overpaying an order is rejected and the debt is unchanged", async () => {
  const p = await api.seedProduct({ price: 500, stock: 5 });
  const c = await api.seedCustomer();
  const sale = await api.sell(p.id, 1, c.id, 200);
  await api.post("/api/payments", { orderId: sale.orderId, amount: 301, method: "Cash", referenceNumber: "", notes: "" }, false);
  expect(await api.debtOf(c.name)).toBe(300);
});

test("walk-in debt is never listed as a customer debt", async ({ page }) => {
  const p = await api.seedProduct({ price: 100, stock: 5 });
  await api.sell(p.id, 1, null, 100); // walk-in, fully paid (the backend requires it)
  await page.goto("/debts");
  await expect(page.getByRole("heading", { name: T("debt.title") })).toBeVisible();
  await expect(page.getByRole("row", { name: /walk-in/i })).toHaveCount(0);
});
