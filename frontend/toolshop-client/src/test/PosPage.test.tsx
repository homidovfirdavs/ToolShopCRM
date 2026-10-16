import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PosPage from "../pages/PosPage";
import { renderApp, T } from "./utils";

const getCategories = vi.fn();
const getProducts = vi.fn();
const getCustomers = vi.fn();
const createCustomer = vi.fn();
const createOrder = vi.fn();
const completeOrder = vi.fn();
const cancelOrder = vi.fn();

vi.mock("../api/catalog", () => ({
  getCategories: (...a: unknown[]) => getCategories(...a),
  getProducts: (...a: unknown[]) => getProducts(...a),
}));
vi.mock("../api/customers", () => ({
  getCustomers: (...a: unknown[]) => getCustomers(...a),
  createCustomer: (...a: unknown[]) => createCustomer(...a),
}));
vi.mock("../api/orders", () => ({
  createOrder: (...a: unknown[]) => createOrder(...a),
  completeOrder: (...a: unknown[]) => completeOrder(...a),
  cancelOrder: (...a: unknown[]) => cancelOrder(...a),
}));

const drill = { id: 1, name: "Drill X", sku: "DR-1", barcode: null, categoryName: "Drills", brandName: "B", purchasePrice: 400, salePrice: 500, isActive: true };
const saw = { id: 2, name: "Saw Y", sku: "SW-1", barcode: null, categoryName: "Saws", brandName: "B", purchasePrice: 100, salePrice: 150, isActive: true };

const apiErr = (status: number, data: unknown) => ({ response: { status, data } });
const KEY_FORMAT = /^[A-Za-z0-9_-]{16,64}$/;
/** No answer from the server (network failure / timeout): the sale may or may not have been completed. */
const noAnswer = () => ({ isAxiosError: true, code: "ERR_NETWORK", message: "Network Error" });
const keyOfCall = (call: number) => completeOrder.mock.calls[call][2] as string;

async function openPos() {
  const user = userEvent.setup();
  renderApp(<PosPage />);
  await screen.findByText("Drill X");
  return user;
}

const addDrill = (user: ReturnType<typeof userEvent.setup>) => user.click(screen.getAllByText("Drill X")[0]); // product card comes before the cart line in the DOM
const completeBtn = () => screen.getByRole("button", { name: `${T("pos.complete")} (F12)` });

describe("PosPage", () => {
  beforeEach(() => {
    for (const m of [getCategories, getProducts, getCustomers, createCustomer, createOrder, completeOrder, cancelOrder]) m.mockReset();
    getCategories.mockResolvedValue([]);
    getProducts.mockResolvedValue({ items: [drill, saw], page: 1, pageSize: 24, totalItems: 2, totalPages: 1 });
    createOrder.mockResolvedValue({ id: 77 });
    completeOrder.mockResolvedValue({ id: 77, orderNumber: "ORD-77", totalAmount: 500, createdAt: "2026-01-01T10:00:00Z", completedAt: "2026-01-01T10:00:00Z" });
    cancelOrder.mockResolvedValue(undefined);
  });

  it("disables complete button for an empty cart", async () => {
    await openPos();
    expect(completeBtn()).toBeDisabled();
  });

  it("adds to cart, merges repeated products and computes totals with discount", async () => {
    const user = await openPos();
    await addDrill(user);
    await addDrill(user);
    await user.click(screen.getAllByText("Saw Y")[0]);
    // 2 × 500 + 150 = 1150
    expect(screen.getAllByText("1 150 TJS").length).toBeGreaterThan(0);
    const discount = screen.getByDisplayValue("0");
    await user.clear(discount);
    await user.type(discount, "150");
    expect(screen.getAllByText("1 000 TJS").length).toBeGreaterThan(0);
  });

  it("walk-in (no customer): partial and later payment options are disabled", async () => {
    const user = await openPos();
    await addDrill(user);
    expect(screen.getByLabelText(T("pos.payPartial"))).toBeDisabled();
    expect(screen.getByLabelText(T("pos.payLater"))).toBeDisabled();
    expect(screen.getByLabelText(T("pos.payFull"))).toBeEnabled();
  });

  it("rejects a discount larger than the subtotal without calling the API", async () => {
    const user = await openPos();
    await addDrill(user);
    const discount = screen.getByDisplayValue("0");
    await user.clear(discount);
    await user.type(discount, "9999");
    await user.click(completeBtn());
    expect(await screen.findByText(T("pos.errDiscount"))).toBeInTheDocument();
    expect(createOrder).not.toHaveBeenCalled();
  });

  it("full-payment walk-in sale: creates order, completes with full cash payment, shows receipt", async () => {
    const user = await openPos();
    await addDrill(user);
    await user.click(completeBtn());
    await user.click(await screen.findByRole("button", { name: T("pos.confirmSale") }));

    expect(await screen.findByText(T("pos.saleDone"))).toBeInTheDocument();
    expect(createOrder).toHaveBeenCalledTimes(1);
    expect(createOrder.mock.calls[0][0]).toMatchObject({
      customerId: null,
      discountAmount: 0,
      items: [{ productId: 1, quantity: 1, discountAmount: 0 }],
    });
    expect(completeOrder).toHaveBeenCalledTimes(1);
    expect(completeOrder).toHaveBeenCalledWith(77, { amount: 500, method: "Cash", referenceNumber: "", notes: "" }, expect.stringMatching(KEY_FORMAT));
    expect(cancelOrder).not.toHaveBeenCalled();
    expect(screen.getByText(/ORD-77/)).toBeInTheDocument();
  });

  it("double click on confirm sends only one sale", async () => {
    let resolve!: (v: unknown) => void;
    completeOrder.mockReturnValue(new Promise((r) => { resolve = r; }));
    const user = await openPos();
    await addDrill(user);
    await user.click(completeBtn());
    const confirm = await screen.findByRole("button", { name: T("pos.confirmSale") });
    await user.dblClick(confirm);
    resolve({ id: 77, orderNumber: "ORD-77", totalAmount: 500, createdAt: "2026-01-01T10:00:00Z", completedAt: null });
    await screen.findByText(T("pos.saleDone"));
    expect(createOrder).toHaveBeenCalledTimes(1);
    expect(completeOrder).toHaveBeenCalledTimes(1);
  });

  it("server WALKIN_FULL_PAYMENT_REQUIRED: shows message and cancels the draft", async () => {
    completeOrder.mockRejectedValue(apiErr(400, { code: "WALKIN_FULL_PAYMENT_REQUIRED", message: "x" }));
    const user = await openPos();
    await addDrill(user);
    await user.click(completeBtn());
    await user.click(await screen.findByRole("button", { name: T("pos.confirmSale") }));
    expect(await screen.findByText(T("pos.errWalkInFull"))).toBeInTheDocument();
    expect(cancelOrder).toHaveBeenCalledWith(77);
    expect(screen.queryByText(T("pos.saleDone"))).not.toBeInTheDocument();
  });

  it("insufficient stock: lists the shortage and cancels the draft", async () => {
    completeOrder.mockRejectedValue(
      apiErr(409, { code: "INSUFFICIENT_STOCK", shortages: [{ productId: 1, productName: "Drill X", available: 1, requested: 3 }] }),
    );
    const user = await openPos();
    await addDrill(user);
    await user.click(completeBtn());
    await user.click(await screen.findByRole("button", { name: T("pos.confirmSale") }));
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(T("pos.errStock"));
    expect(alert).toHaveTextContent("Drill X");
    expect(alert).toHaveTextContent(`${T("pos.stockHave")} 1`);
    expect(alert).toHaveTextContent(`${T("pos.stockNeed")} 3`);
    expect(cancelOrder).toHaveBeenCalledWith(77);
    // cart is kept so the cashier can correct it
    expect(screen.getAllByText("Drill X").length).toBeGreaterThan(1);
  });

  it("customer sale can pay partially: sends partial amount and leaves debt on the receipt", async () => {
    getCustomers.mockResolvedValue({ items: [{ id: 5, name: "Alpha Co", phone: "123" }], page: 1, pageSize: 5, totalItems: 1, totalPages: 1 });
    completeOrder.mockResolvedValue({ id: 77, orderNumber: "ORD-78", totalAmount: 500, createdAt: "2026-01-01T10:00:00Z", completedAt: null });
    const user = await openPos();
    await addDrill(user);
    await user.type(screen.getByPlaceholderText(T("pos.searchCustomer")), "Alp");
    await user.click(await screen.findByText(/Alpha Co/));
    await user.click(screen.getByLabelText(T("pos.payPartial")));
    await user.type(screen.getByPlaceholderText(T("pos.amountNow")), "200");
    await user.click(completeBtn());
    await user.click(await screen.findByRole("button", { name: T("pos.confirmSale") }));
    await screen.findByText(T("pos.saleDone"));
    expect(createOrder.mock.calls[0][0]).toMatchObject({ customerId: 5 });
    expect(completeOrder).toHaveBeenCalledWith(77, { amount: 200, method: "Cash", referenceNumber: "", notes: "" }, expect.stringMatching(KEY_FORMAT));
    expect(screen.getByText(T("pos.debt"))).toBeInTheDocument();
    expect(screen.getAllByText("300 TJS").length).toBeGreaterThan(0);
  });

  it("hold sale stores the cart and resume restores it", async () => {
    const user = await openPos();
    await addDrill(user);
    await user.click(screen.getByRole("button", { name: T("pos.hold") }));
    expect(JSON.parse(localStorage.getItem("toolshop_pos_held") ?? "[]")).toHaveLength(1);
    expect(screen.getByText(T("pos.cartEmpty"))).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: new RegExp(T("pos.held")) }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: T("pos.resume") }));
    await waitFor(() => expect(screen.queryByText(T("pos.cartEmpty"))).not.toBeInTheDocument());
    expect(JSON.parse(localStorage.getItem("toolshop_pos_held") ?? "[]")).toHaveLength(0);
  });

  describe("idempotent sale (uncertain outcome)", () => {
    async function confirmSale(user: ReturnType<typeof userEvent.setup>) {
      await user.click(completeBtn());
      await user.click(await screen.findByRole("button", { name: T("pos.confirmSale") }));
    }

    it("sends a valid Idempotency-Key with the sale", async () => {
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);
      await screen.findByText(T("pos.saleDone"));
      expect(keyOfCall(0)).toMatch(KEY_FORMAT);
    });

    it("no answer: keeps the cart, says the outcome is unknown, and does NOT cancel the draft", async () => {
      completeOrder.mockRejectedValueOnce(noAnswer());
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);

      expect(await screen.findByRole("alert")).toHaveTextContent(T("pos.saleUncertain"));
      expect(cancelOrder).not.toHaveBeenCalled();
      expect(screen.queryByText(T("pos.saleDone"))).not.toBeInTheDocument();
      expect(screen.getAllByText("Drill X").length).toBeGreaterThan(1);   // cart kept
    });

    it("pressing Confirm again re-sends the SAME draft with the SAME key - no second draft, no second sale", async () => {
      completeOrder.mockRejectedValueOnce(noAnswer());
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);
      await screen.findByText(T("pos.saleUncertain"));

      await confirmSale(user);
      await screen.findByText(T("pos.saleDone"));

      expect(createOrder).toHaveBeenCalledTimes(1);            // the draft is reused
      expect(completeOrder).toHaveBeenCalledTimes(2);
      expect(completeOrder.mock.calls[1][0]).toBe(completeOrder.mock.calls[0][0]);   // same draft
      expect(keyOfCall(1)).toBe(keyOfCall(0));                  // same key
      expect(cancelOrder).not.toHaveBeenCalled();
    });

    it("changing the cart after an unknown outcome is a NEW sale: new draft, new key, the old draft is cancelled", async () => {
      createOrder.mockResolvedValueOnce({ id: 77 }).mockResolvedValueOnce({ id: 78 });
      completeOrder.mockRejectedValueOnce(noAnswer());
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);
      await screen.findByText(T("pos.saleUncertain"));

      await user.click(screen.getAllByText("Saw Y")[0]);        // the cashier changes the sale
      await confirmSale(user);
      await screen.findByText(T("pos.saleDone"));

      expect(cancelOrder).toHaveBeenCalledWith(77);
      expect(createOrder).toHaveBeenCalledTimes(2);
      expect(completeOrder.mock.calls[1][0]).toBe(78);
      expect(keyOfCall(1)).not.toBe(keyOfCall(0));
    });

    it("a definite refusal drops the attempt: the next try is a new draft with a new key", async () => {
      createOrder.mockResolvedValueOnce({ id: 77 }).mockResolvedValueOnce({ id: 78 });
      completeOrder.mockRejectedValueOnce(apiErr(400, { code: "WALKIN_FULL_PAYMENT_REQUIRED", message: "x" }));
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);
      await screen.findByText(T("pos.errWalkInFull"));
      expect(cancelOrder).toHaveBeenCalledWith(77);

      await confirmSale(user);
      await screen.findByText(T("pos.saleDone"));

      expect(createOrder).toHaveBeenCalledTimes(2);
      expect(completeOrder.mock.calls[1][0]).toBe(78);
      expect(keyOfCall(1)).not.toBe(keyOfCall(0));
    });

    it("after a successful sale the next sale gets its own key", async () => {
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);
      await screen.findByText(T("pos.saleDone"));
      await user.click(screen.getByRole("button", { name: T("pos.newSale") }));

      await addDrill(user);
      await confirmSale(user);
      await waitFor(() => expect(completeOrder).toHaveBeenCalledTimes(2));

      expect(keyOfCall(1)).not.toBe(keyOfCall(0));
    });

    it("the server saying the key belongs to a different sale shows a translated message and drops the attempt", async () => {
      completeOrder.mockRejectedValueOnce(apiErr(409, { code: "IDEMPOTENCY_KEY_REUSED", message: "x" }));
      const user = await openPos();
      await addDrill(user);
      await confirmSale(user);

      expect(await screen.findByRole("alert")).toHaveTextContent(T("pos.errSaleKeyReused"));
      expect(cancelOrder).toHaveBeenCalledWith(77);
    });
  });
});
