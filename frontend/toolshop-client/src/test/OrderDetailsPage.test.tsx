import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import OrderDetailsPage from "../pages/OrderDetailsPage";
import { renderApp, T } from "./utils";

const getOrder = vi.fn();
const returnOrder = vi.fn();
const getOrderPayments = vi.fn();
const createPayment = vi.fn();
let roles: string[] = ["Admin"];
let accessToken: string | null = null;

vi.mock("../api/orders", () => ({
  getOrder: (...a: unknown[]) => getOrder(...a),
  returnOrder: (...a: unknown[]) => returnOrder(...a),
}));
vi.mock("../api/payments", () => ({
  getOrderPayments: (...a: unknown[]) => getOrderPayments(...a),
  createPayment: (...a: unknown[]) => createPayment(...a),
}));
vi.mock("../context/AuthContext", () => ({
  useAuth: () => ({ user: { id: "1", username: "u", fullName: "U", email: "u@x", roles }, accessToken }),
}));

/** Unsigned JWT-shaped token carrying permission claims (the UI only decodes the payload). */
function tokenWith(permissions: string[]): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, "");
  return `${b64({ alg: "none" })}.${b64({ permission: permissions })}.x`;
}

const order = (over: Record<string, unknown> = {}) => ({
  id: 5,
  orderNumber: "ORD-0005",
  customerId: 1,
  customerName: "Alpha Co",
  status: "Completed",
  orderDate: "2026-10-01T10:00:00Z",
  notes: null,
  subtotal: 400,
  discountAmount: 0,
  totalAmount: 400,
  items: [
    { id: 1, productId: 1, productName: "Drill X", sku: "DR-1", barcode: null, quantity: 4, unitPrice: 100, discountAmount: 0, lineTotal: 400 },
  ],
  createdByUsername: "admin",
  completedByUsername: "admin",
  createdAt: "2026-10-01T10:00:00Z",
  completedAt: "2026-10-01T10:05:00Z",
  cancelledAt: null,
  ...over,
});

const summary = (over: Record<string, unknown> = {}) => ({
  orderTotal: 400,
  totalPaid: 400,
  outstandingBalance: 0,
  paymentState: "Paid",
  payments: [],
  ...over,
});

function renderPage() {
  return renderApp(
    <Routes>
      <Route path="/orders/:id" element={<OrderDetailsPage />} />
    </Routes>,
    { route: "/orders/5" },
  );
}

describe("OrderDetailsPage - returns", () => {
  beforeEach(() => {
    roles = ["Admin"];
    accessToken = null;
    getOrder.mockReset();
    returnOrder.mockReset();
    getOrderPayments.mockReset();
    getOrder.mockResolvedValue(order());
    getOrderPayments.mockResolvedValue(summary());
  });

  it("Admin returns a completed order: reason is required, then the API is called and the page reloads", async () => {
    const ev = userEvent.setup();
    returnOrder.mockResolvedValue(order({ status: "Returned" }));
    renderPage();

    await ev.click(await screen.findByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText(T("ord.returnNote"))).toBeInTheDocument();

    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    expect(within(dialog).getByRole("alert")).toHaveTextContent(T("ord.returnReasonRequired"));
    expect(returnOrder).not.toHaveBeenCalled();

    await ev.type(within(dialog).getByRole("textbox"), "Wrong item");
    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));

    // default = return everything that is left, all sellable, refund method/amount decided by the server
    await waitFor(() =>
      expect(returnOrder).toHaveBeenCalledWith(
        5,
        "Wrong item",
        { items: [{ orderItemId: 1, quantity: 4, damagedQuantity: 0 }] },
        expect.stringMatching(/^[A-Za-z0-9_-]{16,64}$/),
      ),
    );
    await waitFor(() => expect(getOrder).toHaveBeenCalledTimes(2)); // reloaded
  });

  it("shows a translated message when the server says the order is not returnable", async () => {
    const ev = userEvent.setup();
    returnOrder.mockRejectedValue({ response: { status: 409, data: { code: "ORDER_NOT_RETURNABLE", message: "x" } } });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");
    await ev.type(within(dialog).getByRole("textbox"), "Duplicate click");
    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("ord.err.ORDER_NOT_RETURNABLE"));
  });

  it("a partial return sends the chosen units, the damaged units, the refund method and the refund amount", async () => {
    const ev = userEvent.setup();
    returnOrder.mockResolvedValue(order({ status: "PartiallyReturned" }));
    renderPage();

    await ev.click(await screen.findByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");

    const qty = within(dialog).getByLabelText(`${T("ord.retQty")}: Drill X`);
    const damaged = within(dialog).getByLabelText(`${T("ord.retDamaged")}: Drill X`);
    await ev.clear(qty);
    await ev.type(qty, "2");
    await ev.type(damaged, "1");
    await ev.selectOptions(within(dialog).getByRole("combobox"), "Card");
    await ev.type(within(dialog).getByLabelText(new RegExp(T("ord.retAmount"))), "50");
    await ev.type(within(dialog).getByRole("textbox"), "Two were broken");
    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));

    await waitFor(() =>
      expect(returnOrder).toHaveBeenCalledWith(
        5,
        "Two were broken",
        {
          items: [{ orderItemId: 1, quantity: 2, damagedQuantity: 1 }],
          refundMethod: "Card",
          refundAmount: 50,
        },
        expect.stringMatching(/^[A-Za-z0-9_-]{16,64}$/),
      ),
    );
  });

  it("rejects impossible quantities on the client and calls nothing", async () => {
    const ev = userEvent.setup();
    renderPage();

    await ev.click(await screen.findByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");
    const qty = within(dialog).getByLabelText(`${T("ord.retQty")}: Drill X`);
    await ev.clear(qty);
    await ev.type(qty, "9"); // only 4 are left
    await ev.type(within(dialog).getByRole("textbox"), "Too many");
    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    expect(within(dialog).getByRole("alert")).toHaveTextContent(T("ord.returnInvalidQty"));

    await ev.clear(qty);
    await ev.type(qty, "0");
    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    expect(within(dialog).getByRole("alert")).toHaveTextContent(T("ord.returnNothing"));
    expect(returnOrder).not.toHaveBeenCalled();
  });

  it("a partially returned order lists its returns, shows only the units that are left and still allows return and payment", async () => {
    const ev = userEvent.setup();
    getOrder.mockResolvedValue(
      order({
        status: "PartiallyReturned",
        returnedAt: "2026-10-02T09:00:00Z",
        returnedByUsername: "manager1",
        returnReason: "Broken",
        refundedAmount: 0,
        returnedValue: 200,
        returns: [
          {
            id: 1,
            returnNumber: "RET-000001",
            returnedAt: "2026-10-02T09:00:00Z",
            returnedByUsername: "manager1",
            reason: "Broken",
            goodsValue: 200,
            refundAmount: 0,
            refundMethod: null,
            items: [{ orderItemId: 1, productId: 1, productName: "Drill X", quantity: 2, restockedQuantity: 1, writtenOffQuantity: 1, netValue: 200 }],
          },
        ],
      }),
    );
    getOrderPayments.mockResolvedValue(summary({ totalPaid: 100, outstandingBalance: 100, paymentState: "PartiallyPaid", returnedValue: 200 }));
    renderPage();

    const list = await screen.findByTestId("return-list");
    expect(within(list).getByText("RET-000001")).toBeInTheDocument();
    expect(within(list).getByText("Drill X × 2")).toBeInTheDocument();
    expect(screen.getAllByText(T("ord.status.PartiallyReturned")).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: `+ ${T("ord.addPayment")}` })).toBeInTheDocument();

    await ev.click(screen.getByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");
    // 4 sold, 2 already returned -> 2 left, prefilled
    expect(within(dialog).getByLabelText(`${T("ord.retQty")}: Drill X`)).toHaveValue(2);
  });

  it("a Cashier (no Sales.Return) does not see the return button", async () => {
    roles = ["Cashier"];
    renderPage();
    await screen.findByText("Drill X");
    expect(screen.queryByRole("button", { name: T("ord.return") })).not.toBeInTheDocument();
  });

  it("a Manager with the Sales.Return permission sees the return button", async () => {
    roles = ["Manager"];
    accessToken = tokenWith(["Sales.View", "Sales.Return"]);
    renderPage();
    expect(await screen.findByRole("button", { name: T("ord.return") })).toBeInTheDocument();
  });

  it("a returned order shows the return details and offers neither return nor payment", async () => {
    getOrder.mockResolvedValue(
      order({
        status: "Returned",
        returnedAt: "2026-10-02T09:00:00Z",
        returnedByUsername: "manager1",
        returnReason: "Wrong item",
        refundedAmount: 400,
      }),
    );
    getOrderPayments.mockResolvedValue(summary({ totalPaid: 0, outstandingBalance: 0, paymentState: "Refunded", refundedTotal: 400 }));
    renderPage();

    const info = await screen.findByTestId("return-info");
    expect(within(info).getByText("manager1")).toBeInTheDocument();
    expect(within(info).getByText("Wrong item")).toBeInTheDocument();
    expect(within(info).getByText("400 TJS")).toBeInTheDocument();
    expect(screen.getByText(T("pay.state.Refunded"))).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: T("ord.return") })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: `+ ${T("ord.addPayment")}` })).not.toBeInTheDocument();
  });

  it("a draft order has no return button", async () => {
    getOrder.mockResolvedValue(order({ status: "Draft", completedAt: null }));
    getOrderPayments.mockResolvedValue(summary({ totalPaid: 0, outstandingBalance: 400, paymentState: "Unpaid" }));
    renderPage();
    await screen.findByText("Drill X");
    expect(screen.queryByRole("button", { name: T("ord.return") })).not.toBeInTheDocument();
  });
});

describe("OrderDetailsPage - payment idempotency key", () => {
  const KEY_FORMAT = /^[A-Za-z0-9_-]{16,64}$/;
  const unpaid = () => summary({ totalPaid: 0, outstandingBalance: 400, paymentState: "Unpaid" });
  const uncertain = () => ({ isAxiosError: true, code: "ERR_NETWORK", message: "Network Error" });
  const keyOf = (call: number) => createPayment.mock.calls[call][5] as string;

  beforeEach(() => {
    roles = ["Admin"];
    accessToken = null;
    getOrder.mockReset();
    getOrderPayments.mockReset();
    createPayment.mockReset();
    getOrder.mockResolvedValue(order());
    getOrderPayments.mockResolvedValue(unpaid());
  });

  async function openAndSave(ev: ReturnType<typeof userEvent.setup>) {
    const dialog = screen.getByRole("dialog");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    return dialog;
  }

  it("sends one valid key with the payment", async () => {
    const ev = userEvent.setup();
    createPayment.mockResolvedValue({ id: 1 });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    await openAndSave(ev);

    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(1));
    expect(createPayment).toHaveBeenCalledWith(5, 400, "Cash", "", "", expect.stringMatching(KEY_FORMAT));
  });

  it("KEEPS the key after an uncertain outcome, so pressing Save again cannot double-book", async () => {
    const ev = userEvent.setup();
    createPayment.mockRejectedValueOnce(uncertain()).mockResolvedValueOnce({ id: 1 });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    const dialog = await openAndSave(ev);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("ord.paymentUncertain"));

    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(2));

    expect(keyOf(1)).toBe(keyOf(0));
  });

  it("makes a NEW key after a definite refusal, so a corrected payment is a new intention", async () => {
    const ev = userEvent.setup();
    createPayment
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { message: "Payment exceeds outstanding balance." } } })
      .mockResolvedValueOnce({ id: 1 });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    const dialog = await openAndSave(ev);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Payment exceeds outstanding balance.");

    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(2));

    expect(keyOf(1)).toMatch(KEY_FORMAT);
    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it("shows a translated message when the server says the key was used for a different payment, and starts a new key", async () => {
    const ev = userEvent.setup();
    createPayment
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { code: "IDEMPOTENCY_KEY_REUSED", message: "x" } } })
      .mockResolvedValueOnce({ id: 1 });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    const dialog = await openAndSave(ev);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("ord.err.IDEMPOTENCY_KEY_REUSED"));

    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(2));
    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it("every time the form is opened is a new intention with its own key", async () => {
    const ev = userEvent.setup();
    createPayment.mockResolvedValue({ id: 1 });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    await openAndSave(ev);
    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    await openAndSave(ev);
    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(2));

    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it("a double click on Save sends one request", async () => {
    const ev = userEvent.setup();
    let release: (v: unknown) => void = () => {};
    createPayment.mockImplementation(() => new Promise((resolve) => (release = resolve)));
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    const dialog = screen.getByRole("dialog");
    const save = within(dialog).getByRole("button", { name: T("common.save") });
    await ev.dblClick(save);

    expect(createPayment).toHaveBeenCalledTimes(1);
    release({ id: 1 });
  });
});

describe("OrderDetailsPage - return idempotency key", () => {
  const KEY_FORMAT = /^[A-Za-z0-9_-]{16,64}$/;
  const uncertain = () => ({ isAxiosError: true, code: "ERR_NETWORK", message: "Network Error" });
  const keyOf = (call: number) => returnOrder.mock.calls[call][3] as string;

  beforeEach(() => {
    roles = ["Admin"];
    accessToken = null;
    getOrder.mockReset();
    getOrderPayments.mockReset();
    returnOrder.mockReset();
    createPayment.mockReset();
    getOrder.mockResolvedValue(order());
    getOrderPayments.mockResolvedValue(summary());
  });

  /** Opens the return form, types a reason and presses the return button. */
  async function submitReturn(ev: ReturnType<typeof userEvent.setup>, reason = "Wrong item") {
    await ev.click(await screen.findByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");
    await ev.type(within(dialog).getByRole("textbox"), reason);
    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    return dialog;
  }

  it("KEEPS the key after an uncertain outcome, so pressing the button again cannot return twice", async () => {
    const ev = userEvent.setup();
    returnOrder.mockRejectedValueOnce(uncertain()).mockResolvedValueOnce(order({ status: "Returned" }));
    renderPage();

    const dialog = await submitReturn(ev);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("ord.returnUncertain"));

    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    await waitFor(() => expect(returnOrder).toHaveBeenCalledTimes(2));

    expect(keyOf(0)).toMatch(KEY_FORMAT);
    expect(keyOf(1)).toBe(keyOf(0));
  });

  it("closing and REOPENING the form after an uncertain outcome continues the same operation (same key)", async () => {
    const ev = userEvent.setup();
    returnOrder.mockRejectedValueOnce(uncertain()).mockResolvedValueOnce(order({ status: "Returned" }));
    renderPage();

    const dialog = await submitReturn(ev);
    await within(dialog).findByText(T("ord.returnUncertain"));
    await ev.click(within(dialog).getByRole("button", { name: "Close" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    await submitReturn(ev);   // reopen, type the reason again, send
    await waitFor(() => expect(returnOrder).toHaveBeenCalledTimes(2));

    expect(keyOf(1)).toBe(keyOf(0));
  });

  it("makes a NEW key after a definite refusal, so the next try is a new operation", async () => {
    const ev = userEvent.setup();
    returnOrder
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: { code: "QUANTITY_EXCEEDS_REMAINING", message: "x" } } })
      .mockResolvedValueOnce(order({ status: "Returned" }));
    renderPage();

    const dialog = await submitReturn(ev);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("ord.err.QUANTITY_EXCEEDS_REMAINING"));

    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    await waitFor(() => expect(returnOrder).toHaveBeenCalledTimes(2));

    expect(keyOf(1)).toMatch(KEY_FORMAT);
    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it("shows a translated message when the server says the key belongs to a different return, and starts a new key", async () => {
    const ev = userEvent.setup();
    returnOrder
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { code: "IDEMPOTENCY_KEY_REUSED", message: "x" } } })
      .mockResolvedValueOnce(order({ status: "Returned" }));
    renderPage();

    const dialog = await submitReturn(ev);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("ord.err.RETURN_KEY_REUSED"));

    await ev.click(within(dialog).getByRole("button", { name: T("ord.return") }));
    await waitFor(() => expect(returnOrder).toHaveBeenCalledTimes(2));
    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it("after a successful return, the next return (a new partial return) gets its own key", async () => {
    const ev = userEvent.setup();
    returnOrder.mockResolvedValue(order({ status: "PartiallyReturned" }));
    renderPage();

    await submitReturn(ev, "First part");
    await waitFor(() => expect(returnOrder).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    await submitReturn(ev, "Second part");
    await waitFor(() => expect(returnOrder).toHaveBeenCalledTimes(2));

    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it("a double click on the return button sends one request", async () => {
    const ev = userEvent.setup();
    let release: (v: unknown) => void = () => {};
    returnOrder.mockImplementation(() => new Promise((resolve) => (release = resolve)));
    renderPage();

    await ev.click(await screen.findByRole("button", { name: T("ord.return") }));
    const dialog = screen.getByRole("dialog");
    await ev.type(within(dialog).getByRole("textbox"), "Wrong item");
    await ev.dblClick(within(dialog).getByRole("button", { name: T("ord.return") }));

    expect(returnOrder).toHaveBeenCalledTimes(1);
    release(order({ status: "Returned" }));
  });

  it("closing and reopening the PAYMENT form after an uncertain outcome continues the same payment (same key)", async () => {
    const ev = userEvent.setup();
    getOrderPayments.mockResolvedValue(summary({ totalPaid: 0, outstandingBalance: 400, paymentState: "Unpaid" }));
    createPayment.mockRejectedValueOnce(uncertain()).mockResolvedValueOnce({ id: 1 });
    renderPage();

    await ev.click(await screen.findByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    let dialog = screen.getByRole("dialog");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await within(dialog).findByText(T("ord.paymentUncertain"));
    await ev.click(within(dialog).getByRole("button", { name: "Close" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    await ev.click(screen.getByRole("button", { name: `+ ${T("ord.addPayment")}` }));
    dialog = screen.getByRole("dialog");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await waitFor(() => expect(createPayment).toHaveBeenCalledTimes(2));

    expect(createPayment.mock.calls[1][5]).toBe(createPayment.mock.calls[0][5]);
  });
});
