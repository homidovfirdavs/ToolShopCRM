import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import DebtsPage from "../pages/DebtsPage";
import { renderApp, T } from "./utils";

const getDebts = vi.fn();
const getWalkInExceptions = vi.fn();
let roles: string[] = ["Admin"];

vi.mock("../api/payments", () => ({
  getDebts: (...a: unknown[]) => getDebts(...a),
  getWalkInExceptions: (...a: unknown[]) => getWalkInExceptions(...a),
}));
vi.mock("../context/AuthContext", () => ({
  useAuth: () => ({ user: { id: "1", username: "u", fullName: "U", email: "u@x", roles } }),
}));

const page = (over: Record<string, unknown> = {}) => ({
  items: [
    { customerId: 1, customerName: "Alpha Co", phone: "123", outstandingOrders: 2, totalDebt: 2700, lastPurchase: null, isActive: true },
    { customerId: 2, customerName: "Beta Old", phone: null, outstandingOrders: 1, totalDebt: 500, lastPurchase: null, isActive: false },
  ],
  page: 1, pageSize: 20, totalItems: 2, totalPages: 1,
  walkInUnresolvedDebt: 0, walkInUnresolvedOrderCount: 0,
  ...over,
});

describe("DebtsPage", () => {
  beforeEach(() => {
    roles = ["Admin"];
    getDebts.mockReset();
    getWalkInExceptions.mockReset();
  });

  it("lists customer debts and marks inactive customers", async () => {
    getDebts.mockResolvedValue(page());
    renderApp(<DebtsPage />);
    expect(await screen.findByText("Alpha Co")).toBeInTheDocument();
    expect(screen.getByText("2 700 TJS")).toBeInTheDocument();
    expect(screen.getByText("500 TJS")).toBeInTheDocument();
    expect(screen.getAllByText(T("debt.inactive"))).toHaveLength(1);
    expect(screen.queryByText(T("debt.walkInTitle"))).not.toBeInTheDocument();
  });

  it("shows empty state", async () => {
    getDebts.mockResolvedValue(page({ items: [], totalItems: 0 }));
    renderApp(<DebtsPage />);
    expect(await screen.findByText(T("debt.empty"))).toBeInTheDocument();
  });

  it("shows error state when loading fails", async () => {
    getDebts.mockRejectedValue(new Error("boom"));
    renderApp(<DebtsPage />);
    expect(await screen.findByText(T("common.loadFailed"))).toBeInTheDocument();
  });

  it("shows walk-in banner separately and lets Admin expand read-only exceptions", async () => {
    getDebts.mockResolvedValue(page({ walkInUnresolvedDebt: 2500, walkInUnresolvedOrderCount: 3 }));
    getWalkInExceptions.mockResolvedValue({
      items: [{ orderId: 9, orderNumber: "ORD-9", orderDate: "2026-01-01T00:00:00Z", total: 2500, paid: 0, outstanding: 2500 }],
      totalOutstanding: 2500, count: 1,
    });
    renderApp(<DebtsPage />);
    expect(await screen.findByText(T("debt.walkInTitle"))).toBeInTheDocument();
    expect(screen.getByText(/2 500 TJS/)).toBeInTheDocument();
    await userEvent.click(screen.getByText(T("debt.walkInShow")));
    expect(await screen.findByText("ORD-9")).toBeInTheDocument();
    expect(screen.getByText(T("debt.walkInReadOnly"))).toBeInTheDocument();
    // walk-in debt must not be a customer row
    expect(screen.queryByText("Walk-in")).not.toBeInTheDocument();
  });

  it("hides the exceptions toggle from non Admin/Manager roles", async () => {
    roles = ["Cashier"];
    getDebts.mockResolvedValue(page({ walkInUnresolvedDebt: 2500, walkInUnresolvedOrderCount: 3 }));
    renderApp(<DebtsPage />);
    expect(await screen.findByText(T("debt.walkInTitle"))).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText(T("debt.walkInShow"))).not.toBeInTheDocument());
    expect(getWalkInExceptions).not.toHaveBeenCalled();
  });
});
