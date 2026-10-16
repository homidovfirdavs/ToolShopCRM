import { describe, it, expect, vi, beforeEach } from "vitest";
import { barcodeCandidates, findProductByCode, normalizeBarcode, ScanDeduper } from "../lib/barcode";

const getProducts = vi.fn();
vi.mock("../api/catalog", () => ({ getProducts: (...a: unknown[]) => getProducts(...a) }));

const p = (id: number, name: string, sku: string, barcode: string | null) => ({
  id, name, sku, barcode, categoryName: "C", brandName: "B", purchasePrice: 1, salePrice: 2, isActive: true,
});
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 10, totalItems: items.length, totalPages: 1 });

describe("normalizeBarcode", () => {
  it("trims and removes control / zero-width characters", () => {
    expect(normalizeBarcode("  4600100000021\r\n")).toBe("4600100000021");
    expect(normalizeBarcode("46001​00000021\t")).toBe("4600100000021");
    expect(normalizeBarcode("   ")).toBe("");
  });
});

describe("barcodeCandidates", () => {
  it("adds the UPC-A / EAN-13 spelling of the same code", () => {
    expect(barcodeCandidates("0123456789012")).toEqual(["0123456789012", "123456789012"]);
    expect(barcodeCandidates("123456789012")).toEqual(["123456789012", "0123456789012"]);
    expect(barcodeCandidates("4600100000021")).toEqual(["4600100000021"]);
    expect(barcodeCandidates("ABC-1")).toEqual(["ABC-1"]);
  });
});

describe("findProductByCode", () => {
  beforeEach(() => {
    getProducts.mockReset();
  });

  it("returns the product with the EXACT barcode and only asks for active products", async () => {
    getProducts.mockResolvedValue(page([p(1, "Other", "O-1", "46001000000210"), p(2, "Hammer", "H-1", "4600100000021")]));
    const found = await findProductByCode(" 4600100000021 ");
    expect(found?.id).toBe(2);
    expect(getProducts).toHaveBeenCalledWith({ search: "4600100000021", isActive: true, pageSize: 10 });
  });

  it("never guesses: a partial match is not a match", async () => {
    getProducts.mockResolvedValue(page([p(1, "Other", "O-1", "46001000000210")]));
    expect(await findProductByCode("4600100000021")).toBeNull();
  });

  it("matches an exact SKU, case-insensitively", async () => {
    getProducts.mockResolvedValue(page([p(5, "Wrench", "WR-ADJ250", null)]));
    expect((await findProductByCode("wr-adj250"))?.id).toBe(5);
  });

  it("finds a UPC-A product when the camera reports a leading 0", async () => {
    getProducts.mockImplementation(async ({ search }: { search: string }) =>
      page(search === "036000291452" ? [p(7, "Upc item", "U-1", "036000291452")] : []));
    expect((await findProductByCode("0036000291452"))?.id).toBe(7);
  });

  it("returns null for an empty code and lets a network error through", async () => {
    expect(await findProductByCode("  ")).toBeNull();
    expect(getProducts).not.toHaveBeenCalled();
    getProducts.mockRejectedValue(new Error("network"));
    await expect(findProductByCode("123")).rejects.toThrow("network");
  });
});

describe("ScanDeduper (accidental repeats)", () => {
  it("accepts a code once while the camera keeps seeing it", () => {
    const d = new ScanDeduper();
    expect(d.accept("A", 0)).toBe(true);
    for (let t = 120; t <= 10_000; t += 120) expect(d.accept("A", t)).toBe(false); // held in front of the camera for 10 s
  });

  it("accepts the same code again only after it left the view", () => {
    const d = new ScanDeduper();
    expect(d.accept("A", 0)).toBe(true);
    expect(d.accept("A", 600)).toBe(false);
    expect(d.accept("A", 2_200)).toBe(true); // 1.6 s without being seen
  });

  it("does not accept two different codes closer than the minimum gap, but does not lose the second one", () => {
    const d = new ScanDeduper();
    expect(d.accept("A", 0)).toBe(true);
    expect(d.accept("B", 200)).toBe(false);
    expect(d.accept("B", 520)).toBe(true);
  });
});
