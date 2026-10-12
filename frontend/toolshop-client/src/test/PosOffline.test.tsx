import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PosPage from "../pages/PosPage";
import { renderApp, T } from "./utils";

const getCategories = vi.fn();
const getProducts = vi.fn();
const getCustomers = vi.fn();
const createOrder = vi.fn();
const completeOrder = vi.fn();

vi.mock("../api/catalog", () => ({
  getCategories: (...a: unknown[]) => getCategories(...a),
  getProducts: (...a: unknown[]) => getProducts(...a),
}));
vi.mock("../api/customers", () => ({
  getCustomers: (...a: unknown[]) => getCustomers(...a),
  createCustomer: vi.fn(),
}));
vi.mock("../api/orders", () => ({
  createOrder: (...a: unknown[]) => createOrder(...a),
  completeOrder: (...a: unknown[]) => completeOrder(...a),
  cancelOrder: vi.fn(),
}));

const drill = { id: 1, name: "Drill X", sku: "DR-1", barcode: null, categoryName: "Drills", brandName: "B", purchasePrice: 400, salePrice: 500, isActive: true };

describe("PosPage on a phone / without a connection", () => {
  beforeEach(() => {
    for (const m of [getCategories, getProducts, getCustomers, createOrder, completeOrder]) m.mockReset();
    getCategories.mockResolvedValue([]);
    getProducts.mockResolvedValue({ items: [drill], page: 1, pageSize: 24, totalItems: 1, totalPages: 1 });
  });

  it("shows a bottom cart bar with the total once something is in the cart", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />);
    expect(screen.queryByTestId("pos-cart-bar")).not.toBeInTheDocument();
    await user.click((await screen.findAllByText("Drill X"))[0]);
    const bar = screen.getByTestId("pos-cart-bar");
    expect(bar).toHaveTextContent("500 TJS");
    expect(bar).toHaveTextContent(T("pos.goToCart"));
  });

  it("the cart bar scrolls to the cart section", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />);
    await user.click((await screen.findAllByText("Drill X"))[0]);
    const scroll = vi.fn();
    const cart = document.getElementById("pos-cart")!;
    cart.scrollIntoView = scroll;
    await user.click(screen.getByRole("button", { name: T("pos.goToCart") }));
    expect(scroll).toHaveBeenCalled();
  });

  it("offline: the Complete button is disabled and no order is created", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />);
    await user.click((await screen.findAllByText("Drill X"))[0]);
    const complete = screen.getByRole("button", { name: `${T("pos.complete")} (F12)` });
    expect(complete).toBeEnabled();

    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
    window.dispatchEvent(new Event("offline"));
    expect(await screen.findByRole("button", { name: `${T("pos.complete")} (F12)` })).toBeDisabled();
    expect(createOrder).not.toHaveBeenCalled();
    expect(completeOrder).not.toHaveBeenCalled();
  });
});
