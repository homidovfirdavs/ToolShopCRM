import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, within } from "@testing-library/react";
import ReturnsReportTab from "../pages/reports/ReturnsReportTab";
import { renderApp, T } from "./utils";

const getReturnsReport = vi.fn();

vi.mock("../api/reports", () => ({
  getReturnsReport: (...a: unknown[]) => getReturnsReport(...a),
}));

const row = (over: Record<string, unknown> = {}) => ({
  returnId: 1,
  returnNumber: "RET-000001",
  returnedAt: "2026-10-02T09:00:00Z",
  orderId: 5,
  orderNumber: "ORD-0005",
  customerName: "Alpha Co",
  returnedByUserName: "Manager",
  reason: "Broken",
  returnedUnits: 2,
  restockedUnits: 1,
  writtenOffUnits: 1,
  goodsValue: 200,
  refundAmount: 200,
  refundMethod: "Cash",
  costReversed: 60,
  writeOffCost: 60,
  ...over,
});

const report = (over: Record<string, unknown> = {}) => ({
  items: [row()],
  page: 1,
  pageSize: 20,
  totalItems: 1,
  totalPages: 1,
  returnsCount: 1,
  goodsValue: 200,
  refundsPaid: 200,
  costReversed: 60,
  writeOffCost: 60,
  ...over,
});

describe("ReturnsReportTab", () => {
  beforeEach(() => getReturnsReport.mockReset());

  it("lists each return with its numbers and totals", async () => {
    getReturnsReport.mockResolvedValue(report());
    renderApp(<ReturnsReportTab />);

    expect(await screen.findByText("RET-000001")).toBeInTheDocument();
    expect(screen.getByText("ORD-0005")).toBeInTheDocument();
    expect(screen.getByText("Broken")).toBeInTheDocument();
    const totals = screen.getByTestId("returns-totals");
    expect(within(totals).getAllByText("200 TJS")).toHaveLength(2); // goods value + refunds paid
  });

  it("shows N/A (never 0) when the cost of a return is unknown", async () => {
    getReturnsReport.mockResolvedValue(report({ items: [row({ costReversed: null, writeOffCost: null })], costReversed: null, writeOffCost: null }));
    renderApp(<ReturnsReportTab />);

    await screen.findByText("RET-000001");
    const totals = screen.getByTestId("returns-totals");
    expect(within(totals).getAllByText("N/A")).toHaveLength(2);
    expect(screen.getAllByText("N/A").length).toBeGreaterThanOrEqual(4);
  });

  it("shows an empty state when there are no returns", async () => {
    getReturnsReport.mockResolvedValue(report({ items: [], totalItems: 0, returnsCount: 0, goodsValue: 0, refundsPaid: 0, costReversed: 0, writeOffCost: 0 }));
    renderApp(<ReturnsReportTab />);
    expect(await screen.findByText(T("rep.noReturns"))).toBeInTheDocument();
  });
});
