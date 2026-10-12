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

  it("shows N/A (not 0) for gross profit when cost data is incomplete, with a warning", async () => {
    setup(summary({ grossProfit: null, hasCompleteCostData: false }));
    renderApp(<DashboardPage />);
    expect(await screen.findByText(T("dash.costNote"))).toBeInTheDocument();
    expect(within(kpi(T("kpi.gross"))).getByText("N/A")).toBeInTheDocument();
    expect(within(kpi(T("kpi.gross"))).queryByText("0 TJS")).not.toBeInTheDocument();
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

  it("shows an error state when the summary fails", async () => {
    setup();
    getDashboardSummary.mockReset();
    getDashboardSummary.mockRejectedValue(new Error("x"));
    renderApp(<DashboardPage />);
    expect(await screen.findByText(T("dash.loadError"))).toBeInTheDocument();
  });
});
