import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PosPage from "../pages/PosPage";
import { renderApp, T } from "./utils";

const getCategories = vi.fn();
const getProducts = vi.fn();
const getCustomers = vi.fn();
const createOrder = vi.fn();
const completeOrder = vi.fn();
const getSingleStock = vi.fn();

vi.mock("../api/catalog", () => ({
  getCategories: (...a: unknown[]) => getCategories(...a),
  getProducts: (...a: unknown[]) => getProducts(...a),
}));
vi.mock("../api/customers", () => ({ getCustomers: (...a: unknown[]) => getCustomers(...a), createCustomer: vi.fn() }));
vi.mock("../api/orders", () => ({
  createOrder: (...a: unknown[]) => createOrder(...a),
  completeOrder: (...a: unknown[]) => completeOrder(...a),
  cancelOrder: vi.fn(),
}));
vi.mock("../api/inventory", () => ({ getSingleStock: (...a: unknown[]) => getSingleStock(...a) }));

// The camera library is replaced: the test decides which code the "camera" sees.
const cam = vi.hoisted(() => ({
  emit: null as null | ((text: string) => void),
  stop: vi.fn(),
  failWith: null as null | string,
  starts: 0,
}));
vi.mock("@zxing/browser", () => ({
  BrowserMultiFormatOneDReader: class {
    async decodeFromConstraints(constraints: unknown, _video: unknown, cb: (r: { getText: () => string } | undefined) => void) {
      cam.starts += 1;
      (cam as unknown as { lastConstraints: unknown }).lastConstraints = constraints;
      if (cam.failWith) throw Object.assign(new Error("camera"), { name: cam.failWith });
      cam.emit = (text) => cb({ getText: () => text });
      return { stop: cam.stop };
    }
  },
}));
vi.mock("@zxing/library", () => ({
  NotFoundException: class NotFoundException extends Error {},
  ChecksumException: class ChecksumException extends Error {},
  FormatException: class FormatException extends Error {},
  BarcodeFormat: { EAN_13: 1, EAN_8: 2, UPC_A: 3, UPC_E: 4, CODE_128: 5 },
  DecodeHintType: { POSSIBLE_FORMATS: 1, TRY_HARDER: 2 },
}));

const hammer = { id: 3, name: "Hammer H", sku: "H-1", barcode: "4600100000021", categoryName: "Hand", brandName: "B", purchasePrice: 20, salePrice: 35, isActive: true };
const drill = { id: 2, name: "Drill D", sku: "D-1", barcode: "4600100000014", categoryName: "Power", brandName: "B", purchasePrice: 300, salePrice: 450, isActive: true };
const ALL = [hammer, drill];
const stockOf: Record<number, number> = { 3: 40, 2: 0 };

function setCameraEnv(secure: boolean) {
  Object.defineProperty(window, "isSecureContext", { value: secure, configurable: true });
  Object.defineProperty(navigator, "mediaDevices", { value: secure ? { getUserMedia: vi.fn() } : undefined, configurable: true });
}

async function openScanner(user: ReturnType<typeof userEvent.setup>) {
  renderApp(<PosPage />);
  await screen.findAllByText("Hammer H");
  await user.click(screen.getByTestId("pos-scan-open"));
  return screen.getByTestId("scan-dialog");
}
const see = (text: string) => act(async () => { cam.emit?.(text); });
const cartHas = (name: string) => screen.queryAllByText(name).length;

describe("POS camera barcode scanner", () => {
  beforeEach(() => {
    for (const m of [getCategories, getProducts, getCustomers, createOrder, completeOrder, getSingleStock]) m.mockReset();
    cam.emit = null; cam.stop.mockReset(); cam.failWith = null; cam.starts = 0;
    setCameraEnv(true);
    getCategories.mockResolvedValue([]);
    getProducts.mockImplementation(async (q: { search?: string }) => {
      const s = (q.search ?? "").toLowerCase();
      const items = s ? ALL.filter((p) => p.name.toLowerCase().includes(s) || p.sku.toLowerCase().includes(s) || p.barcode.includes(s)) : ALL;
      return { items, page: 1, pageSize: 24, totalItems: items.length, totalPages: 1 };
    });
    getSingleStock.mockImplementation(async (id: number) => ({ productId: id, currentStock: stockOf[id] ?? 0 }));
  });
  afterEach(() => vi.restoreAllMocks());

  it("asks for the BACK camera and puts a scanned product in the cart without touching stock or orders", async () => {
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    expect((cam as unknown as { lastConstraints: MediaStreamConstraints }).lastConstraints.video).toMatchObject({ facingMode: { ideal: "environment" } });

    await see("4600100000021");
    const fb = await screen.findByTestId("scan-feedback");
    await waitFor(() => expect(fb).toHaveTextContent(T("pos.scanAdded")));
    expect(fb).toHaveTextContent("Hammer H");
    expect(fb).toHaveTextContent("35 TJS");
    expect(fb).toHaveTextContent(`${T("pos.scanStock")}: 40`);
    expect(cartHas("Hammer H")).toBeGreaterThan(1); // product card + cart line
    expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent("35 TJS");
    // Scan = add to cart. Nothing is sold or deducted on the server.
    expect(createOrder).not.toHaveBeenCalled();
    expect(completeOrder).not.toHaveBeenCalled();
  });

  it("the same barcode seen again and again adds ONE unit", async () => {
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    for (let i = 0; i < 6; i++) await see("4600100000021");
    await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(T("pos.scanAdded")));
    expect(screen.getByTestId("scan-feedback")).toHaveTextContent(`${T("pos.scanInCart")}: 1`);
    expect(getSingleStock).toHaveBeenCalledTimes(1);
  });

  it("an unknown barcode shows a clear message and creates nothing", async () => {
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    await see("9999999999994");
    const fb = await screen.findByTestId("scan-feedback");
    await waitFor(() => expect(fb).toHaveTextContent(T("pos.scanNotFound")));
    expect(fb).toHaveTextContent("9999999999994");
    expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent(": 0");
    expect(cartHas("Hammer H")).toBe(1); // only the product card
  });

  it("a product with no stock is refused", async () => {
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    await see("4600100000014");
    await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(T("pos.scanOutOfStock")));
    expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent(": 0");
  });

  it("does not put more in the cart than the stock; +1 is refused at the limit, −1 gives one back", async () => {
    stockOf[3] = 2;
    try {
      const user = userEvent.setup();
      await openScanner(user);
      await waitFor(() => expect(cam.emit).not.toBeNull());
      await see("4600100000021");
      await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(`${T("pos.scanInCart")}: 1`));
      await user.click(screen.getByTestId("scan-plus"));
      await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(`${T("pos.scanInCart")}: 2`));
      await user.click(screen.getByTestId("scan-plus"));
      await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(T("pos.scanNotEnough")));
      expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent("70 TJS");
    } finally {
      stockOf[3] = 40;
    }
  });

  it("unknown stock (stock call failed) adds the product with a warning, not as zero", async () => {
    getSingleStock.mockRejectedValue(new Error("down"));
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    await see("4600100000021");
    await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(T("pos.scanStockUnknown")));
    expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent("35 TJS");
  });

  it("−1 takes one unit back out of the cart", async () => {
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    await see("4600100000021");
    await user.click(await screen.findByTestId("scan-minus"));
    await waitFor(() => expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent(": 0"));
  });

  it("camera permission denied: clear message, manual entry still adds the product", async () => {
    cam.failWith = "NotAllowedError";
    const user = userEvent.setup();
    const dialog = await openScanner(user);
    expect(await within(dialog).findByTestId("scan-camera-problem")).toHaveTextContent(T("pos.scanDenied"));
    await user.type(screen.getByTestId("scan-manual-input"), "4600100000021");
    await user.click(screen.getByTestId("scan-manual-submit"));
    await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent(T("pos.scanAdded")));
    expect(screen.getByTestId("scan-cart-summary")).toHaveTextContent("35 TJS");
  });

  it("no HTTPS / no camera API: explains why and keeps manual entry (by SKU too)", async () => {
    setCameraEnv(false);
    const user = userEvent.setup();
    const dialog = await openScanner(user);
    expect(cam.starts).toBe(0);
    expect(within(dialog).getByTestId("scan-camera-problem")).toHaveTextContent(T("pos.scanInsecure"));
    await user.type(screen.getByTestId("scan-manual-input"), "h-1{enter}");
    await waitFor(() => expect(screen.getByTestId("scan-feedback")).toHaveTextContent("Hammer H"));
  });

  it("the camera is released when the scanner is closed, and Done closes it keeping the cart", async () => {
    const user = userEvent.setup();
    await openScanner(user);
    await waitFor(() => expect(cam.emit).not.toBeNull());
    await see("4600100000021");
    await screen.findByTestId("scan-minus");
    await user.click(screen.getByTestId("scan-done"));
    expect(screen.queryByTestId("scan-dialog")).not.toBeInTheDocument();
    expect(cam.stop).toHaveBeenCalled();
    expect(cartHas("Hammer H")).toBeGreaterThan(1);
  });

  it("offline: the scan button is disabled", async () => {
    renderApp(<PosPage />);
    await screen.findAllByText("Hammer H");
    expect(screen.getByTestId("pos-scan-open")).toBeEnabled();
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
    window.dispatchEvent(new Event("offline"));
    await waitFor(() => expect(screen.getByTestId("pos-scan-open")).toBeDisabled());
  });
});
