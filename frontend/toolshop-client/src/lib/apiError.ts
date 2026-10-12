/** HTTP status of an axios-like error, if any. */
export function errorStatus(err: unknown): number | undefined {
  if (typeof err === "object" && err !== null && "response" in err) {
    return (err as { response?: { status?: number } }).response?.status;
  }
  return undefined;
}

/** Server-provided message (message / error / title), if any. */
export function errorMessage(err: unknown): string | undefined {
  if (typeof err === "object" && err !== null && "response" in err) {
    const data = (err as { response?: { data?: unknown } }).response?.data;
    if (typeof data === "string" && data.length < 300) return data;
    if (typeof data === "object" && data !== null) {
      const d = data as Record<string, unknown>;
      for (const k of ["message", "error", "title", "detail"]) {
        if (typeof d[k] === "string") return d[k] as string;
      }
    }
  }
  return undefined;
}

export interface StockShortage {
  productId: number;
  productName: string;
  available: number;
  requested: number;
}

/** Per-product stock shortages from a 409 INSUFFICIENT_STOCK response, if any. */
export function errorShortages(err: unknown): StockShortage[] {
  if (typeof err === "object" && err !== null && "response" in err) {
    const data = (err as { response?: { data?: unknown } }).response?.data;
    if (typeof data === "object" && data !== null) {
      const s = (data as Record<string, unknown>).shortages;
      if (Array.isArray(s)) return s as StockShortage[];
    }
  }
  return [];
}

/** Machine-readable error code from the server (e.g. WALKIN_FULL_PAYMENT_REQUIRED), if any. */
export function errorCode(err: unknown): string | undefined {
  if (typeof err === "object" && err !== null && "response" in err) {
    const data = (err as { response?: { data?: unknown } }).response?.data;
    if (typeof data === "object" && data !== null && typeof (data as Record<string, unknown>).code === "string") {
      return (data as Record<string, string>).code;
    }
  }
  return undefined;
}
