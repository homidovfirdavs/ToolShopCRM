import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import KpiCard from "../components/ui/KpiCard";

describe("KpiCard", () => {
  it("shows label and value", () => {
    render(<KpiCard label="Revenue" value="17 290 TJS" accent="blue" />);
    expect(screen.getByText("Revenue")).toBeInTheDocument();
    expect(screen.getByText("17 290 TJS")).toBeInTheDocument();
  });

  it("shows N/A as given and does not turn it into zero", () => {
    render(<KpiCard label="Gross profit" value="N/A" accent="amber" />);
    expect(screen.getByText("N/A")).toBeInTheDocument();
    expect(screen.queryByText(/^0/)).toBeNull();
  });

  it("hides the change line when it cannot be compared honestly (pct = null)", () => {
    render(<KpiCard label="Revenue" value="1" accent="blue" delta={{ pct: null, upIsGood: true, label: "vs previous" }} />);
    expect(screen.queryByText("vs previous")).toBeNull();
  });

  it("shows the change with the right percentage and label", () => {
    render(<KpiCard label="Revenue" value="1" accent="blue" delta={{ pct: 12.34, upIsGood: true, label: "vs previous" }} />);
    expect(screen.getByText("12.3%")).toBeInTheDocument();
    expect(screen.getByText("vs previous")).toBeInTheDocument();
  });

  it("colours a rise in expenses as bad and a fall as good", () => {
    const { rerender } = render(
      <KpiCard label="Expenses" value="1" accent="red" delta={{ pct: 10, upIsGood: false, label: "x" }} />,
    );
    expect(screen.getByText("10.0%").className).toContain("text-red-600");
    rerender(<KpiCard label="Expenses" value="1" accent="red" delta={{ pct: -10, upIsGood: false, label: "x" }} />);
    expect(screen.getByText("10.0%").className).toContain("text-emerald-600");
  });

  it("links to the detail page when `to` is given", () => {
    render(
      <MemoryRouter>
        <KpiCard label="Debt" value="5 200 TJS" accent="red" to="/debts" />
      </MemoryRouter>,
    );
    expect(screen.getByRole("link")).toHaveAttribute("href", "/debts");
  });
});
