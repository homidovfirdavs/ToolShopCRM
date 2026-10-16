import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPayment } from "../api/payments";
import { isUncertainOutcome } from "../lib/apiError";
import { newIdempotencyKey } from "../lib/idempotency";

const post = vi.fn();
vi.mock("../api/client", () => ({ default: { post: (...a: unknown[]) => post(...a) } }));

const KEY = "0123456789abcdef0123456789abcdef";
const noWait = { retryDelaysMs: [0, 0] };

/** An axios-like error: no response = the request never got an answer (network failure / timeout). */
const networkError = () => ({ isAxiosError: true, code: "ERR_NETWORK", message: "Network Error" });
const httpError = (status: number, data: unknown = {}) => ({ isAxiosError: true, response: { status, data } });

describe("createPayment - Idempotency-Key", () => {
  beforeEach(() => { post.mockReset(); });

  it("sends the key as the Idempotency-Key header and the payment as the body", async () => {
    post.mockResolvedValue({ data: { id: 9 } });

    const payment = await createPayment(5, 300, "Cash", "ref", "", KEY, noWait);

    expect(payment).toEqual({ id: 9 });
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(
      "/payments",
      { orderId: 5, amount: 300, method: "Cash", referenceNumber: "ref", notes: "" },
      { headers: { "Idempotency-Key": KEY } },
    );
  });

  it("re-sends the SAME request with the SAME key after a network failure, then succeeds", async () => {
    post.mockRejectedValueOnce(networkError()).mockResolvedValueOnce({ data: { id: 9 } });

    await expect(createPayment(5, 300, "Cash", "", "", KEY, noWait)).resolves.toEqual({ id: 9 });

    expect(post).toHaveBeenCalledTimes(2);
    expect(post.mock.calls[1]).toEqual(post.mock.calls[0]); // identical body and identical key
  });

  it("re-sends after a 5xx gateway / server error too", async () => {
    post.mockRejectedValueOnce(httpError(503)).mockRejectedValueOnce(httpError(502)).mockResolvedValueOnce({ data: { id: 9 } });

    await expect(createPayment(5, 300, "Cash", "", "", KEY, noWait)).resolves.toEqual({ id: 9 });
    expect(post).toHaveBeenCalledTimes(3);
    for (const call of post.mock.calls) expect(call[2]).toEqual({ headers: { "Idempotency-Key": KEY } });
  });

  it("gives up after the last delay and reports the failure (the key stays valid for a manual retry)", async () => {
    post.mockImplementation(async () => {
      throw networkError();
    });

    await expect(createPayment(5, 300, "Cash", "", "", KEY, noWait)).rejects.toMatchObject({ code: "ERR_NETWORK" });
    expect(post).toHaveBeenCalledTimes(3); // first try + 2 automatic re-sends
  });

  it.each([400, 401, 403, 404, 409])("never re-sends a definite answer (HTTP %i)", async (status) => {
    post.mockImplementation(async () => {
      throw httpError(status, { message: "no" });
    });

    await expect(createPayment(5, 300, "Cash", "", "", KEY, noWait)).rejects.toMatchObject({ response: { status } });
    expect(post).toHaveBeenCalledTimes(1);
  });

  it("does not retry errors that are not HTTP errors (a bug must surface, not loop)", async () => {
    post.mockImplementation(async () => {
      throw new TypeError("boom");
    });

    await expect(createPayment(5, 300, "Cash", "", "", KEY, noWait)).rejects.toThrow("boom");
    expect(post).toHaveBeenCalledTimes(1);
  });
});

describe("isUncertainOutcome", () => {
  it("is true only when the server never answered or answered with a 5xx", () => {
    expect(isUncertainOutcome(networkError())).toBe(true);
    expect(isUncertainOutcome(httpError(500))).toBe(true);
    expect(isUncertainOutcome(httpError(504))).toBe(true);

    expect(isUncertainOutcome(httpError(400))).toBe(false);
    expect(isUncertainOutcome(httpError(409))).toBe(false);
    expect(isUncertainOutcome(new Error("x"))).toBe(false);
    expect(isUncertainOutcome(null)).toBe(false);
    expect(isUncertainOutcome("x")).toBe(false);
  });
});

describe("newIdempotencyKey", () => {
  const KEY_FORMAT = /^[A-Za-z0-9_-]{16,64}$/; // what the server accepts

  afterEach(() => vi.unstubAllGlobals());

  it("makes a different server-acceptable key every time", () => {
    const keys = new Set(Array.from({ length: 200 }, () => newIdempotencyKey()));
    expect(keys.size).toBe(200);
    for (const k of keys) expect(k).toMatch(KEY_FORMAT);
  });

  it("still works on a plain-HTTP page where crypto.randomUUID does not exist", () => {
    vi.stubGlobal("crypto", {
      getRandomValues: (a: Uint8Array) => {
        for (let i = 0; i < a.length; i++) a[i] = (i * 37 + 11) % 256;
        return a;
      },
    });
    const key = newIdempotencyKey();
    expect(key).toMatch(KEY_FORMAT);
    expect(key).toHaveLength(32);
  });

  it("falls back even without any crypto object", () => {
    vi.stubGlobal("crypto", undefined);
    expect(newIdempotencyKey()).toMatch(KEY_FORMAT);
  });
});
