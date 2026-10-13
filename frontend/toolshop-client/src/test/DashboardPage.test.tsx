import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, within } from "@testing-library/react";
import DashboardPage from "../pages/DashboardPage";
import { renderApp, T } from "./utils";

const getDashboardSummary = vi.fn();
const getSalesTrend = vi.fn();
const getSalesByCategory = vi.fn();
const getAttention = vi.fn();
const getLowStock = vi.fn();
const getFinancialSummaryShort = vi.fn();

vi.mock("../api/dashboard", () => ({
  getDashboardSummary: (...a: unknown[]) => getDashboardSummary(...a),
  getSalesTrend: (...a: unknown[]) => getSalesTrend(...a),
  getSalesByCategory: (...a: unknown[]) => getSalesByCategory(...a),
  getAttention: (...a: unknown[]) => getAttention(...a),
  getLowStock: (...a: unknown[]) => getLowStock(...a),
  getFinancialSummaryShort: (...a: unknown[]) => getFinancialSummaryShort(...a),
}));

const summary = (over: Record<string, unknown> = {}) => ({
  dateFrom: "2026-01-01", dateTo: "2026-01-30",
  salesRevenue: 17290, grossProfit: 4000, paymentsReceived: 12090, accountsReceivable: 5200,
  lowStockCount: 2, operatingExpenses: 1000, hasCompleteCostData: true, outOfStockCount: 0, completedOrdersCount: 5,
  ...over,
});

function setup(cur = summary(), prev: ReturnType<typeof summary> | null = summary({ salesRevenue: 10000, grossProfit: 2000, operatingExpenses: 2000, paymentsReceived: 6000 })) {
  // first call = current period, second call = previous period
  getDashboardSummary.mockReset();
  getDashboardSummary.mockResolvedValueOnce(cur);
  if (prev) getDashboardSummary.mockResolvedValueOnce(prev);
  else getDashboardSummary.mockRejectedValueOnce(new Error("prev failed"));
  getSalesTrend.mockResolvedValue([]);
  getSalesByCategory.mockResolvedValue([{ category: "Drills", revenue: 17290, percentage: 100 }]);
  getAttention.mockResolvedValue([{ type: "OutstandingDebt", label: "Debt", count: 3, amount: 5200, link: "/debts" }]);
  getLowStock.mockResolvedValue([]);
  getFinancialSummaryShort.mockResolvedValue({
    revenue: 17290, cogs: 13290, grossProfit: 4000, operatingExpenses: 1000, netOperatingProfit: 3000, hasCompleteCostData: true,
  });
}

/** The KPI card that contains the given label. */
function kpi(label: string): HTMLElement {
  const el = screen.getAllByText(label)[0].closest("a,div[class*='rounded']") as HTMLElement;
  return el;
}

describe("DashboardPage", () => {
  beforeEach(() => {
    getSalesTrend.mockReset(); getSalesByCategory.mockReset(); getAttention.mockReset();
    getLowStock.mockReset(); getFinancialSummaryShort.mockReset();
  });

  it("renders KPI values from the API", async () => {
    setup();
    renderApp(<DashboardPage />);
    expect(await screen.findByText(T("kpi.revenue"))).toBeInTheDocument();
    expect(within(kpi(T("kpi.revenue"))).getAllByText("17 290 TJS").length).toBeGreaterThan(0);
    expect(within(kpi(T("kpi.debt"))).getByText("5 200 TJS")).toBeInTheDocument();
    expect(screen.queryByText(T("dash.costNote"))).not.toBeInTheDocument();
  });

  it("shows exactly the 5 KPIs in order, and no Gross Profit / Low Stock KPI or Short Financial Summary card", async () => {
    setup();
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.revenue"));
    const grid = screen.getByTestId("dash-kpis");
    const labels = [T("kpi.revenue"), T("kpi.payments"), T("kpi.debt"), T("kpi.opex"), T("kpi.netProfit")];
    const text = grid.textContent ?? "";
    const positions = labels.map((l) => text.indexOf(l));
    expect(positions.every((p) => p >= 0)).toBe(true);
    expect([...positions].sort((a, b) => a - b)).toEqual(positions); // order as requested
    expect(within(grid).queryByText(T("kpi.gross"))).not.toBeInTheDocument();
    expect(within(grid).queryByText(T("kpi.lowStock"))).not.toBeInTheDocument();
    expect(screen.queryByText(T("dash.summary"))).not.toBeInTheDocument();
  });

  it("Net Profit is the backend value (Revenue − COGS − Expenses), not recalculated in the browser", async () => {
    setup();
    getFinancialSummaryShort.mockResolvedValue({
      revenue: 17290, cogs: 13290, grossProfit: 4000, operatingExpenses: 1000, netOperatingProfit: 2999, hasCompleteCostData: true,
    }); // deliberately not 3000: the page must show what the API says
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.netProfit"));
    expect(within(kpi(T("kpi.netProfit"))).getByText("2 999 TJS")).toBeInTheDocument();
  });

  it("negative Net Profit is shown as a negative number", async () => {
    setup();
    getFinancialSummaryShort.mockResolvedValue({
      revenue: 1000, cogs: 900, grossProfit: 100, operatingExpenses: 600, netOperatingProfit: -500, hasCompleteCostData: true,
    });
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.netProfit"));
    expect(within(kpi(T("kpi.netProfit"))).getByText("-500 TJS")).toBeInTheDocument();
  });

  it("Net Profit = N/A (not 0) when cost data is incomplete, with the explanation and the page layout intact", async () => {
    setup(summary({ grossProfit: null, hasCompleteCostData: false }));
    getFinancialSummaryShort.mockResolvedValue({
      revenue: 17290, cogs: null, grossProfit: null, operatingExpenses: 1000, netOperatingProfit: null, hasCompleteCostData: false,
    });
    renderApp(<DashboardPage />);
    expect(await screen.findByText(T("dash.costNote"))).toBeInTheDocument();
    const card = kpi(T("kpi.netProfit"));
    expect(within(card).getByText("N/A")).toBeInTheDocument();
    expect(within(card).queryByText("0 TJS")).not.toBeInTheDocument();
    expect(within(card).getByText(T("dash.warnNet"))).toBeInTheDocument();
    // the other four KPIs still show their real values
    expect(within(kpi(T("kpi.revenue"))).getAllByText("17 290 TJS").length).toBeGreaterThan(0);
    expect(within(kpi(T("kpi.opex"))).getAllByText("1 000 TJS").length).toBeGreaterThan(0);
  });

  it("empty period: zeros are real zeros, Net Profit is not N/A, and no previous-period change is invented", async () => {
    setup(
      summary({ salesRevenue: 0, grossProfit: 0, paymentsReceived: 0, accountsReceivable: 0, operatingExpenses: 0, completedOrdersCount: 0 }),
      summary({ salesRevenue: 0, grossProfit: 0, operatingExpenses: 0, paymentsReceived: 0 }),
    );
    getFinancialSummaryShort.mockResolvedValue({ revenue: 0, cogs: 0, grossProfit: 0, operatingExpenses: 0, netOperatingProfit: 0, hasCompleteCostData: true });
    getSalesByCategory.mockResolvedValue([]);
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.netProfit"));
    expect(within(kpi(T("kpi.netProfit"))).getByText("0 TJS")).toBeInTheDocument();
    expect(screen.queryByText(T("dash.costNote"))).not.toBeInTheDocument();
  });

  it("keeps the alerts section, the sales chart area and the category block", async () => {
    setup();
    getAttention.mockResolvedValue([
      { type: "OutOfStock", label: "x", count: 1, amount: null, link: "/inventory?status=OutOfStock" },
      { type: "LowStock", label: "x", count: 2, amount: null, link: "/inventory?status=LowStock" },
      { type: "OutstandingDebt", label: "Debt", count: 0, amount: 5200, link: "/debts" },
    ]);
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.netProfit"));
    expect(screen.getByText(T("dash.attention"))).toBeInTheDocument();
    expect(screen.getByText(T("att.LowStock"))).toBeInTheDocument();
    expect(screen.getByText(T("dash.byCategory"))).toBeInTheDocument();
    expect(screen.getByText(T("dash.trend"))).toBeInTheDocument();
  });

  it("hides change-vs-previous when the previous period is 0 or failed to load", async () => {
    setup(summary(), summary({ salesRevenue: 0, grossProfit: 0, operatingExpenses: 0, paymentsReceived: 0 }));
    const { unmount } = renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.revenue"));
    expect(screen.queryByText(T("dash.vsPrev"))).not.toBeInTheDocument();
    unmount();

    setup(summary(), null);
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.revenue"));
    expect(screen.queryByText(T("dash.vsPrev"))).not.toBeInTheDocument();
  });

  it("shows change-vs-previous when comparable", async () => {
    setup();
    renderApp(<DashboardPage />);
    await screen.findByText(T("kpi.revenue"));
    expect(screen.getAllByText(T("dash.vsPrev")).length).toBeGreaterThan(0);
    expect(within(kpi(T("kpi.revenue"))).getByText("72.9%")).toBeInTheDocument();
  });

  it("shows a loading skeleton while the data is loading (no KPI numbers yet)", async () => {
    setup();
    getDashboardSummary.mockReset();
    getDashboardSummary.mockReturnValue(new Promise(() => undefined));
    renderApp(<DashboardPage />);
    expect(document.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(screen.queryByText(T("kpi.netProfit"))).not.toBeInTheDocument();
  });

  it("shows an error state when the summary fails", async () => {
    setup();
    getDashboardSummary.mockReset();
    getDashboardSummary.mockRejectedValue(new Error("x"));
    renderApp(<DashboardPage />);
    expect(await screen.findByText(T("dash.loadError"))).toBeInTheDocument();
  });
});
