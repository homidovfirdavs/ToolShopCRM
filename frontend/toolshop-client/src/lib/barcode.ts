import { getProducts, type ProductListItem } from "../api/catalog";

/**
 * Helpers for the camera barcode scanner in the POS.
 * Scanning is only a faster way to find a product and put it in the cart: it uses the same product search
 * and the same cart as typing in the POS search box. It never touches stock (stock changes only when a sale
 * is completed on the server).
 */

/** Trim and drop control characters that some scanners append (CR/LF/TAB, zero-width). */
export function normalizeBarcode(raw: string): string {
  let out = "";
  for (const ch of raw) {
    const c = ch.codePointAt(0)!;
    const control = c < 0x20 || c === 0x7f || (c >= 0x200b && c <= 0x200d) || c === 0xfeff;
    if (!control) out += ch;
  }
  return out.trim();
}

/**
 * Codes to try for one scanned value. A phone camera may report a UPC-A (12 digits) as EAN-13 with a leading 0
 * (or the reverse), so both spellings are looked up. The scanned value always comes first.
 */
export function barcodeCandidates(code: string): string[] {
  const list = [code];
  if (/^\d{13}$/.test(code) && code.startsWith("0")) list.push(code.slice(1));
  if (/^\d{12}$/.test(code)) list.push("0" + code);
  return list;
}

/**
 * Finds ONE active product by exact barcode (or exact SKU). Never guesses: a partial match returns null, so a
 * mis-read code can not put a wrong product in the cart. Throws when the server can not be reached.
 */
export async function findProductByCode(rawCode: string): Promise<ProductListItem | null> {
  const code = normalizeBarcode(rawCode);
  if (!code) return null;
  for (const candidate of barcodeCandidates(code)) {
    const lower = candidate.toLowerCase();
    const r = await getProducts({ search: candidate, isActive: true, pageSize: 10 });
    const byBarcode = r.items.find((p) => p.barcode?.trim().toLowerCase() === lower);
    if (byBarcode) return byBarcode;
    if (candidate === code) {
      const bySku = r.items.find((p) => p.sku.trim().toLowerCase() === lower);
      if (bySku) return bySku;
    }
  }
  return null;
}

/**
 * Protects against accidental repeats while the camera keeps seeing the same barcode.
 *  - A code that is still in front of the camera is accepted ONCE: it can be accepted again only after the
 *    camera has not seen it for `rearmMs` (the user moved it away and back).
 *  - Two different codes can not be accepted closer than `minGapMs`.
 */
export class ScanDeduper {
  private lastSeen = new Map<string, number>();
  private lastAccepted = -Infinity;
  private readonly rearmMs: number;
  private readonly minGapMs: number;

  constructor(rearmMs = 1500, minGapMs = 500) {
    this.rearmMs = rearmMs;
    this.minGapMs = minGapMs;
  }

  accept(code: string, now: number): boolean {
    const prev = this.lastSeen.get(code);
    const armed = prev === undefined || now - prev >= this.rearmMs;
    if (!armed) {
      this.lastSeen.set(code, now); // still in view: keep it blocked
      return false;
    }
    if (now - this.lastAccepted < this.minGapMs) return false; // too soon after another code; stays armed
    this.lastSeen.set(code, now);
    this.lastAccepted = now;
    return true;
  }
}
