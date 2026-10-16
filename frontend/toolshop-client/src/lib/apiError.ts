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

/**
 * True when the request may or may not have been carried out: the server never answered (network failure, timeout) or a
 * server / gateway error (5xx) came back. A definite answer (2xx, 4xx) is NOT uncertain. For money operations the same
 * Idempotency-Key must be kept and sent again, never a new one.
 */
export function isUncertainOutcome(err: unknown): boolean {
  if (typeof err !== "object" || err === null) return false;
  if ((err as { isAxiosError?: boolean }).isAxiosError !== true) return false;
  const status = errorStatus(err);
  return status === undefined || status >= 500;
}
