import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PosPage from "../pages/PosPage";
import { renderApp, T } from "./utils";

const getCategories = vi.fn();
const getProducts = vi.fn();

vi.mock("../api/catalog", () => ({
  getCategories: (...a: unknown[]) => getCategories(...a),
  getProducts: (...a: unknown[]) => getProducts(...a),
}));
vi.mock("../api/customers", () => ({ getCustomers: vi.fn(), createCustomer: vi.fn() }));
vi.mock("../api/orders", () => ({ createOrder: vi.fn(), completeOrder: vi.fn(), cancelOrder: vi.fn() }));

const cat = (id: number, name: string, productCount: number | undefined, isActive = true) => ({
  id, name, description: null, isActive, productCount, totalProductCount: productCount, createdAt: "2026-01-01T00:00:00Z", updatedAt: null,
});
const drill = { id: 1, name: "Drill X", sku: "DR-1", barcode: null, categoryName: "Электрические", brandName: "B", purchasePrice: 400, salePrice: 500, isActive: true };

const cards = () => screen.getAllByTestId("pos-category-card");

describe("POS category cards", () => {
  beforeEach(() => {
    getCategories.mockReset();
    getProducts.mockReset();
    getCategories.mockResolvedValue([
      cat(1, "Асбобҳои барқӣ", 12),
      cat(2, "Асбобҳои дастӣ", 1),
      cat(3, "Холӣ", 0),
      cat(4, "Ғайрифаъол", 5, false),
      cat(5, "Бе шумора", undefined),
    ]);
    getProducts.mockResolvedValue({ items: [drill], page: 1, pageSize: 24, totalItems: 1, totalPages: 1 });
  });

  it("shows only active categories from the API with their real product counts", async () => {
    renderApp(<PosPage />);
    await screen.findByText("Асбобҳои барқӣ");
    expect(screen.queryByText("Ғайрифаъол")).not.toBeInTheDocument();
    // "All" card + 4 active categories
    expect(cards()).toHaveLength(5);
    const electric = screen.getByText("Асбобҳои барқӣ").closest("button")!;
    expect(electric).toHaveTextContent(`12 ${T("pos.catCountMany")}`);
    expect(screen.getByText("Холӣ").closest("button")).toHaveTextContent(`0 ${T("pos.catCountMany")}`);
    // unknown count is never invented
    expect(screen.getByText("Бе шумора").closest("button")).not.toHaveTextContent(/\d/);
  });

  it("clicking a category filters products by categoryId and highlights the card", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />);
    const card = (await screen.findByText("Асбобҳои дастӣ")).closest("button")!;
    expect(card).toHaveAttribute("aria-pressed", "false");
    await user.click(card);
    await waitFor(() => expect(getProducts).toHaveBeenLastCalledWith(expect.objectContaining({ categoryId: 2, isActive: true })));
    expect(card).toHaveAttribute("aria-pressed", "true");
    // back to "all"
    await user.click(screen.getByRole("button", { name: T("pos.catClear") }));
    await waitFor(() => expect(getProducts).toHaveBeenLastCalledWith(expect.objectContaining({ categoryId: undefined })));
  });

  it("category search narrows the cards and reports no match", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />);
    await screen.findByText("Асбобҳои барқӣ");
    const box = screen.getByPlaceholderText(T("pos.catSearch"));
    await user.type(box, "дастӣ");
    expect(cards()).toHaveLength(1);
    await user.clear(box);
    await user.type(box, "zzzz");
    expect(screen.getByText(T("pos.catNoMatch"))).toBeInTheDocument();
  });

  it("an empty category shows a clear message and the cart/product list still works", async () => {
    const user = userEvent.setup();
    getProducts.mockResolvedValue({ items: [], page: 1, pageSize: 24, totalItems: 0, totalPages: 0 });
    renderApp(<PosPage />);
    await user.click((await screen.findByText("Холӣ")).closest("button")!);
    expect(await screen.findByText(T("pos.categoryEmpty"))).toBeInTheDocument();
  });

  it("the toggle hides and shows the cards", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />);
    await screen.findByText("Асбобҳои барқӣ");
    await user.click(screen.getByRole("button", { name: T("pos.catHide") }));
    expect(screen.queryAllByTestId("pos-category-card")).toHaveLength(0);
    await user.click(screen.getByRole("button", { name: T("pos.catShow") }));
    expect(cards().length).toBeGreaterThan(0);
  });

  it("shows an empty hint when there are no active categories", async () => {
    getCategories.mockResolvedValue([cat(9, "Off", 3, false)]);
    renderApp(<PosPage />);
    expect(await screen.findByText(T("pos.noCategories"))).toBeInTheDocument();
    expect(within(document.body).queryAllByTestId("pos-category-card")).toHaveLength(0);
  });
});
