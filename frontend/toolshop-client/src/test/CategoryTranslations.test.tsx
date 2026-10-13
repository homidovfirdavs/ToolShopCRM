import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PosPage from "../pages/PosPage";
import ProductsPage from "../pages/ProductsPage";
import CategoriesPage from "../pages/CategoriesPage";
import { useI18n } from "../i18n/useI18n";
import { localizedCategoryName, resetCategoryNames } from "../lib/categoryNames";
import { renderApp, T } from "./utils";

const getCategories = vi.fn();
const getProducts = vi.fn();
const getBrands = vi.fn();
const createCategory = vi.fn();
const updateCategory = vi.fn();

vi.mock("../api/catalog", () => ({
  getCategories: (...a: unknown[]) => getCategories(...a),
  getProducts: (...a: unknown[]) => getProducts(...a),
  getBrands: (...a: unknown[]) => getBrands(...a),
  createCategory: (...a: unknown[]) => createCategory(...a),
  updateCategory: (...a: unknown[]) => updateCategory(...a),
  deactivateCategory: vi.fn(),
  deactivateProduct: vi.fn(),
}));
vi.mock("../api/customers", () => ({ getCustomers: vi.fn(), createCustomer: vi.fn() }));
vi.mock("../api/orders", () => ({ createOrder: vi.fn(), completeOrder: vi.fn(), cancelOrder: vi.fn() }));
vi.mock("../context/AuthContext", () => ({
  useAuth: () => ({ user: { id: "1", username: "a", fullName: "A", email: "a@x", roles: ["Admin"] }, accessToken: null }),
}));

const cat = (id: number, name: string, tr: { nameTj?: string | null; nameRu?: string | null; nameEn?: string | null } = {}, productCount = 3) => ({
  id, name, nameTj: null, nameRu: null, nameEn: null, ...tr, description: null, isActive: true, productCount, totalProductCount: productCount, createdAt: "2026-01-01T00:00:00Z", updatedAt: null,
});

const CATS = [
  cat(1, "Асбобҳои барқӣ", { nameTj: "Асбобҳои барқӣ", nameRu: "Электроинструмент", nameEn: "Power tools" }),
  cat(2, "Асбобҳои дастӣ", { nameEn: "Hand tools" }), // only English: RU/TJ fall back to the main name
  cat(3, "Бе тарҷума"),
];

function LangButtons() {
  const { setLang } = useI18n();
  return (
    <div>
      <button onClick={() => setLang("tj")}>go-tj</button>
      <button onClick={() => setLang("ru")}>go-ru</button>
      <button onClick={() => setLang("en")}>go-en</button>
    </div>
  );
}

const product = { id: 1, name: "Drill X", sku: "DR-1", barcode: null, categoryName: "Асбобҳои барқӣ", brandName: "B", purchasePrice: 400, salePrice: 500, isActive: true };

describe("localizedCategoryName", () => {
  it("returns the language's name and falls back to the main name when empty or blank", () => {
    const c = { name: "Main", nameTj: "Тоҷ", nameRu: "  ", nameEn: null };
    expect(localizedCategoryName(c, "tj")).toBe("Тоҷ");
    expect(localizedCategoryName(c, "ru")).toBe("Main");
    expect(localizedCategoryName(c, "en")).toBe("Main");
    expect(localizedCategoryName({ name: "Only" }, "ru")).toBe("Only");
  });
});

describe("category names follow the selected language", () => {
  beforeEach(() => {
    resetCategoryNames();
    for (const m of [getCategories, getProducts, getBrands, createCategory, updateCategory]) m.mockReset();
    getCategories.mockResolvedValue(CATS);
    getBrands.mockResolvedValue([]);
    getProducts.mockResolvedValue({ items: [product], page: 1, pageSize: 24, totalItems: 1, totalPages: 1 });
    createCategory.mockResolvedValue({});
    updateCategory.mockResolvedValue({});
  });

  it("POS cards switch TJ/RU/EN instantly, without another request, and fall back to the main name", async () => {
    const user = userEvent.setup();
    renderApp(
      <>
        <LangButtons />
        <PosPage />
      </>,
      { lang: "en" },
    );
    expect(await screen.findByText("Power tools")).toBeInTheDocument();
    expect(screen.getByText("Hand tools")).toBeInTheDocument();
    expect(screen.getByText("Бе тарҷума")).toBeInTheDocument(); // no translation at all → main name
    const calls = getCategories.mock.calls.length;

    await user.click(screen.getByText("go-ru"));
    expect(screen.getByText("Электроинструмент")).toBeInTheDocument();
    expect(screen.queryByText("Power tools")).not.toBeInTheDocument();
    expect(screen.getByText("Асбобҳои дастӣ")).toBeInTheDocument(); // RU empty → main name

    await user.click(screen.getByText("go-tj"));
    expect(screen.getByText("Асбобҳои барқӣ")).toBeInTheDocument();

    await user.click(screen.getByText("go-en"));
    expect(screen.getByText("Power tools")).toBeInTheDocument();
    expect(getCategories.mock.calls.length).toBe(calls); // no reload, no new request
  });

  it("POS category filter still sends the category id (not the name) and the search finds any language", async () => {
    const user = userEvent.setup();
    renderApp(<PosPage />, { lang: "en" });
    await screen.findByText("Power tools");
    await user.type(screen.getByPlaceholderText(T("pos.catSearch")), "электро"); // Russian text while UI is English
    expect(screen.getAllByTestId("pos-category-card")).toHaveLength(1);
    await user.click(screen.getByText("Power tools").closest("button")!);
    await waitFor(() => expect(getProducts).toHaveBeenLastCalledWith(expect.objectContaining({ categoryId: 1 })));
  });

  it("Products page: row category name is translated and updates on language change", async () => {
    const user = userEvent.setup();
    renderApp(
      <>
        <LangButtons />
        <ProductsPage />
      </>,
      { lang: "en" },
    );
    await screen.findAllByText("Drill X");
    await waitFor(() => expect(screen.getAllByText("Power tools").length).toBeGreaterThan(0));
    await user.click(screen.getByText("go-ru"));
    expect(screen.getAllByText("Электроинструмент").length).toBeGreaterThan(0);
    // the filter option is translated too, but its value stays the id
    const opt = screen.getAllByRole("option", { name: "Электроинструмент" })[0] as HTMLOptionElement;
    expect(opt.value).toBe("1");
  });

  it("Categories page: the add form has Tajik/Russian/English inputs and sends them", async () => {
    const user = userEvent.setup();
    renderApp(<CategoriesPage />, { lang: "en" });
    await screen.findByText("Power tools");
    await user.click(screen.getByRole("button", { name: `+ ${T("cat.add")}` }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(new RegExp(T("cat.mainName"))), "Рангубор");
    await user.type(within(dialog).getByLabelText(new RegExp(T("cat.nameTj"))), "Рангубор");
    await user.type(within(dialog).getByLabelText(new RegExp(T("cat.nameRu"))), "Краски");
    await user.type(within(dialog).getByLabelText(new RegExp(T("cat.nameEn"))), "Paint");
    await user.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await waitFor(() => expect(createCategory).toHaveBeenCalledTimes(1));
    expect(createCategory).toHaveBeenCalledWith("Рангубор", "", { nameTj: "Рангубор", nameRu: "Краски", nameEn: "Paint" });
  });

  it("Categories page: edit pre-fills the translations; the main name stays required", async () => {
    const user = userEvent.setup();
    renderApp(<CategoriesPage />, { lang: "en" });
    await screen.findByText("Power tools");
    const row = screen.getByText("Power tools").closest("tr")!;
    await user.click(within(row).getByRole("button", { name: T("common.edit") }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(new RegExp(T("cat.nameRu")))).toHaveValue("Электроинструмент");
    expect(within(dialog).getByLabelText(new RegExp(T("cat.nameEn")))).toHaveValue("Power tools");

    await user.clear(within(dialog).getByLabelText(new RegExp(T("cat.mainName"))));
    await user.click(within(dialog).getByRole("button", { name: T("common.save") }));
    expect(await within(dialog).findByText(T("common.nameRequired"))).toBeInTheDocument();
    expect(updateCategory).not.toHaveBeenCalled();
  });

  it("Categories page: re-activating keeps the translations", async () => {
    const user = userEvent.setup();
    getCategories.mockResolvedValue([{ ...CATS[0], isActive: false }]);
    renderApp(<CategoriesPage />, { lang: "en" });
    await screen.findByText("Power tools");
    await user.click(screen.getByRole("button", { name: T("common.activate") }));
    await waitFor(() => expect(updateCategory).toHaveBeenCalledTimes(1));
    expect(updateCategory).toHaveBeenCalledWith(1, "Асбобҳои барқӣ", "", true, { nameTj: "Асбобҳои барқӣ", nameRu: "Электроинструмент", nameEn: "Power tools" });
  });
});
