import { test, expect } from "@playwright/test";
import { Api, T, signedInPage } from "./helpers";

let api: Api;

test.beforeEach(async ({ page }) => {
  const login = await Api.login();
  api = login.api;
  await signedInPage(page, login.refreshToken);
});
test.afterEach(async () => api?.dispose());

test("returning a credit sale in full puts the stock back and removes the debt", async ({ page }) => {
  const p = await api.seedProduct({ price: 500, stock: 5 });
  const c = await api.seedCustomer();
  const sale = await api.sell(p.id, 2, c.id, 300); // total 1000, paid 300 -> debt 700
  expect(await api.stockOf(p.id)).toBe(3);
  expect(await api.debtOf(c.name)).toBe(700);

  await page.goto(`/orders/${sale.orderId}`);
  await page.getByRole("button", { name: T("ord.return") }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("textbox").fill("Customer returned the goods");
  await dialog.getByRole("button", { name: T("ord.return") }).click();

  const info = page.getByTestId("return-info");
  await expect(info).toBeVisible();
  await expect(info).toContainText("Customer returned the goods");

  expect(await api.stockOf(p.id)).toBe(5);
  expect(await api.debtOf(c.name)).toBe(0);
  await expect(page.getByRole("button", { name: T("ord.return") })).toHaveCount(0);
});

test("a returned order cannot be returned twice", async () => {
  const p = await api.seedProduct({ price: 100, stock: 5 });
  const sale = await api.sell(p.id, 1, null, 100); // walk-in, fully paid
  await api.post(`/api/orders/${sale.orderId}/return`, { reason: "First return" });
  expect(await api.stockOf(p.id)).toBe(5);

  await api.post(`/api/orders/${sale.orderId}/return`, { reason: "Second return" }, false);
  expect(await api.stockOf(p.id)).toBe(5); // unchanged
});
