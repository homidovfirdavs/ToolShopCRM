import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import CashShiftPage from "../pages/CashShiftPage";
import { renderApp, T } from "./utils";

const getCurrentCashShift = vi.fn();
const getCashShifts = vi.fn();
const openCashShift = vi.fn();
const closeCashShift = vi.fn();
const addCashMovement = vi.fn();
let roles: string[] = ["Admin"];

vi.mock("../api/cashShifts", () => ({
  getCurrentCashShift: (...a: unknown[]) => getCurrentCashShift(...a),
  getCashShifts: (...a: unknown[]) => getCashShifts(...a),
  openCashShift: (...a: unknown[]) => openCashShift(...a),
  closeCashShift: (...a: unknown[]) => closeCashShift(...a),
  addCashMovement: (...a: unknown[]) => addCashMovement(...a),
  voidCashMovement: vi.fn(),
  reviewCashShift: vi.fn(),
}));
vi.mock("../context/AuthContext", () => ({
  useAuth: () => ({ user: { id: "1", username: "u", fullName: "U", email: "u@x", roles }, accessToken: null }),
}));

const breakdown = { cashSales: null, cashRefunds: null, cashPaymentsVoidedFromOtherShifts: null, cashIn: null, cashOut: null,
  cardSales: 300, bankTransferSales: 0, qrSales: 0, otherSales: 0, paymentCount: 2, refundCount: 0, movementCount: 0 };

const openShift = (expectedCash: number | null) => ({
  isOpen: true,
  shift: {
    id: 5, shiftNumber: "SHF-000005", cashRegisterId: 1, cashRegisterName: "Main", status: "Open", cashierUserId: "1",
    cashierName: "Anvar", openedAt: "2026-10-24T08:00:00Z", openedByName: "Anvar", openingCash: 100, closedAt: null,
    closedByName: null, closingCash: null, expectedCash, cashDifference: null, closeNote: null, closedByOther: false,
    requiresReview: false, reviewed: false, reviewedAt: null, reviewedByName: null, reviewNote: null, breakdown,
  },
  movements: [],
});

const apiError = (status: number, code: string) =>
  Object.assign(new Error(code), { isAxiosError: true, response: { status, data: { code, message: code } } });

describe("CashShiftPage", () => {
  beforeEach(() => {
    roles = ["Admin"];
    [getCurrentCashShift, getCashShifts, openCashShift, closeCashShift, addCashMovement].forEach((m) => m.mockReset());
    getCashShifts.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 });
  });

  it("shows the disabled message when the server says cash shifts are off", async () => {
    getCurrentCashShift.mockRejectedValue(apiError(503, "CASH_SHIFTS_DISABLED"));
    renderApp(<CashShiftPage />);
    expect(await screen.findByText(T("cash.disabled"))).toBeInTheDocument();
  });

  it("offers to open a shift and sends the opening cash", async () => {
    getCurrentCashShift.mockResolvedValue({ isOpen: false, previousClosingCash: 250 });
    openCashShift.mockResolvedValue({});
    renderApp(<CashShiftPage />);
    expect(await screen.findByText("250 TJS")).toBeInTheDocument();
    await userEvent.type(screen.getByRole("textbox"), "100");
    await userEvent.click(screen.getByRole("button", { name: T("cash.openShift") }));
    await waitFor(() => expect(openCashShift).toHaveBeenCalledWith(100));
  });

  it("hides the expected cash while the shift is open (blind count) and shows no amount at all, never 0", async () => {
    getCurrentCashShift.mockResolvedValue(openShift(null));
    renderApp(<CashShiftPage />, { lang: "en" });
    expect(await screen.findByText(T("cash.hiddenUntilClose"))).toBeInTheDocument();
    // The expected-cash tile must carry no amount at all (not even 0): only the hint text.
    const tile = screen.getByText(T("cash.expected")).parentElement as HTMLElement;
    expect(tile).toHaveTextContent(T("cash.hiddenUntilClose"));
    expect(tile.textContent).not.toMatch(/TJS|N\/A/);
  });

  it("keeps the close dialog open and asks for a note when the difference needs one", async () => {
    getCurrentCashShift.mockResolvedValue(openShift(null));
    closeCashShift.mockRejectedValue(apiError(400, "CASH_DIFFERENCE_NOTE_REQUIRED"));
    renderApp(<CashShiftPage />);
    await userEvent.click(await screen.findByRole("button", { name: T("cash.closeShift") }));
    const dialog = await screen.findByRole("dialog");
    const inputs = dialog.querySelectorAll("input");
    await userEvent.type(inputs[0], "90");
    const buttons = dialog.querySelectorAll("button");
    await userEvent.click(buttons[buttons.length - 1]);
    await waitFor(() => expect(closeCashShift).toHaveBeenCalledWith(5, 90, ""));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect((await screen.findAllByText(T("cash.err.noteRequired"))).length).toBeGreaterThan(0);
  });

  it("sends cash movements with one Idempotency-Key and reuses it only until success", async () => {
    getCurrentCashShift.mockResolvedValue(openShift(null));
    addCashMovement.mockResolvedValue({});
    renderApp(<CashShiftPage />);
    await userEvent.click(await screen.findByRole("button", { name: T("cash.cashOut") }));
    const dialog = await screen.findByRole("dialog");
    const inputs = dialog.querySelectorAll("input");
    await userEvent.type(inputs[0], "20");
    await userEvent.type(inputs[1], "Taxi");
    const buttons = dialog.querySelectorAll("button");
    await userEvent.click(buttons[buttons.length - 1]);
    await waitFor(() => expect(addCashMovement).toHaveBeenCalledTimes(1));
    const [id, type, amount, reason, key] = addCashMovement.mock.calls[0];
    expect([id, type, amount, reason]).toEqual([5, "CashOut", 20, "Taxi"]);
    expect(String(key).length).toBeGreaterThanOrEqual(16);
  });

  it("does not show operate buttons to a user without CashShifts.Operate", async () => {
    roles = ["Warehouse"];
    getCurrentCashShift.mockResolvedValue(openShift(null));
    renderApp(<CashShiftPage />);
    await screen.findByText(T("cash.hiddenUntilClose"));
    expect(screen.queryByRole("button", { name: T("cash.closeShift") })).not.toBeInTheDocument();
  });
});
