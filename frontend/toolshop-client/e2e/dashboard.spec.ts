import { test, expect, type Page, type Response } from "@playwright/test";
import { Api, T, formatMoney, signedInPage } from "./helpers";

interface Summary {
  salesRevenue: number;
  grossProfit: number | null;
  operatingExpenses: number;
  accountsReceivable: number;
  paymentsReceived: number;
}

let api: Api;

test.beforeEach(async ({ page }) => {
  const login = await Api.login();
  api = login.api;
  await signedInPage(page, login.refreshToken);
});
test.afterEach(async () => api?.dispose());

/** Opens the dashboard and returns the summary of the CURRENT period (the one with the latest `to`). */
async function openDashboard(page: Page): Promise<Summary> {
  const responses: Response[] = [];
  page.on("response", (r) => {
    if (r.url().includes("/api/dashboard/summary")) responses.push(r);
  });
  await page.goto("/dashboard");
  await expect(page.getByText(T("kpi.revenue")).first()).toBeVisible();
  await expect.poll(() => responses.length).toBeGreaterThanOrEqual(1);
  const withTo = responses.map((r) => ({ r, to: new URL(r.url()).searchParams.get("to") ?? "" }));
  withTo.sort((a, b) => b.to.localeCompare(a.to));
  return (await withTo[0].r.json()) as Summary;
}

const kpiCard = (page: Page, label: string) => page.getByText(label, { exact: true }).first().locator("xpath=ancestor::*[self::a or self::div][contains(@class,'rounded')][1]");

test("KPI cards show exactly the numbers returned by the API", async ({ page }) => {
  const s = await openDashboard(page);
  await expect(kpiCard(page, T("kpi.revenue"))).toContainText(formatMoney(s.salesRevenue));
  await expect(kpiCard(page, T("kpi.debt"))).toContainText(formatMoney(s.accountsReceivable));
  await expect(kpiCard(page, T("kpi.payments"))).toContainText(formatMoney(s.paymentsReceived));
  // Gross profit is no longer a KPI card (it is a line in the trend chart); the cards are Revenue, Payments, Debt,
  // Operating Expenses and Net Profit.
  await expect(kpiCard(page, T("kpi.opex"))).toContainText(formatMoney(s.operatingExpenses));
});

test("a credit sale raises Customer Debt and Revenue by exactly the right amounts", async ({ page }) => {
  const before = await openDashboard(page);

  const p = await api.seedProduct({ price: 500, stock: 5 });
  const c = await api.seedCustomer();
  await api.sell(p.id, 1, c.id, 200); // revenue +500, payments +200, debt +300

  const after = await openDashboard(page);
  expect(after.salesRevenue - before.salesRevenue).toBe(500);
  expect(after.paymentsReceived - before.paymentsReceived).toBe(200);
  expect(after.accountsReceivable - before.accountsReceivable).toBe(300);
  await expect(kpiCard(page, T("kpi.debt"))).toContainText(formatMoney(after.accountsReceivable));
});

test("Customer Debt on the Dashboard links to the Debts page", async ({ page }) => {
  await openDashboard(page);
  await kpiCard(page, T("kpi.debt")).click();
  await expect(page).toHaveURL(/\/debts/);
});
