import { describe, it, expect, vi, beforeEach } from "vitest";
import { completeOrder, returnOrder } from "../api/orders";
import { retryUncertain } from "../lib/idempotency";

const post = vi.fn();
vi.mock("../api/client", () => ({ default: { post: (...a: unknown[]) => post(...a) } }));

const KEY = "0123456789abcdef0123456789abcdef";
const noWait = { retryDelaysMs: [0, 0] };

/** An axios-like error: no response = the request never got an answer (network failure / timeout). */
const networkError = () => ({ isAxiosError: true, code: "ERR_NETWORK", message: "Network Error" });
const httpError = (status: number, data: unknown = {}) => ({ isAxiosError: true, response: { status, data } });

describe("completeOrder - Idempotency-Key", () => {
  beforeEach(() => {
    post.mockReset();
  });

  it("sends the key as the Idempotency-Key header, with the initial payment as the body", async () => {
    post.mockResolvedValue({ data: { id: 5 } });

    const order = await completeOrder(5, { amount: 200, method: "Cash", referenceNumber: "", notes: "" }, KEY, noWait);

    expect(order).toEqual({ id: 5 });
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(
      "/orders/5/complete",
      { initialPayment: { amount: 200, method: "Cash", referenceNumber: "", notes: "" } },
      { headers: { "Idempotency-Key": KEY } },
    );
  });

  it("sends no body when there is no initial payment, but still the key", async () => {
    post.mockResolvedValue({ data: { id: 5 } });

    await completeOrder(5, undefined, KEY, noWait);

    expect(post).toHaveBeenCalledWith("/orders/5/complete", undefined, { headers: { "Idempotency-Key": KEY } });
  });

  it("re-sends the SAME request with the SAME key after a network failure, then succeeds", async () => {
    post.mockRejectedValueOnce(networkError()).mockResolvedValueOnce({ data: { id: 5 } });

    await expect(completeOrder(5, undefined, KEY, noWait)).resolves.toEqual({ id: 5 });

    expect(post).toHaveBeenCalledTimes(2);
    expect(post.mock.calls[1]).toEqual(post.mock.calls[0]);
  });

  it("re-sends after 5xx too, and gives up after the last delay", async () => {
    post.mockImplementation(async () => {
      throw httpError(503);
    });

    await expect(completeOrder(5, undefined, KEY, noWait)).rejects.toMatchObject({ response: { status: 503 } });
    expect(post).toHaveBeenCalledTimes(3); // first try + 2 automatic re-sends
    for (const call of post.mock.calls) expect(call[2]).toEqual({ headers: { "Idempotency-Key": KEY } });
  });

  it.each([400, 401, 403, 404, 409])("never re-sends a definite answer (HTTP %i)", async (status) => {
    post.mockImplementation(async () => {
      throw httpError(status, { message: "no" });
    });

    await expect(completeOrder(5, undefined, KEY, noWait)).rejects.toMatchObject({ response: { status } });
    expect(post).toHaveBeenCalledTimes(1);
  });
});

describe("returnOrder - Idempotency-Key", () => {
  beforeEach(() => {
    post.mockReset();
  });

  const options = { items: [{ orderItemId: 1, quantity: 2, damagedQuantity: 0 }], refundMethod: "Card" as const, refundAmount: 50 };

  it("sends the key as the Idempotency-Key header and the return as the body", async () => {
    post.mockResolvedValue({ data: { id: 5, status: "PartiallyReturned" } });

    await returnOrder(5, "Wrong item", options, KEY, noWait);

    expect(post).toHaveBeenCalledWith("/orders/5/return", { reason: "Wrong item", ...options }, { headers: { "Idempotency-Key": KEY } });
  });

  it("re-sends the SAME return with the SAME key after no answer (never a second refund)", async () => {
    post.mockRejectedValueOnce(networkError()).mockRejectedValueOnce(httpError(502)).mockResolvedValueOnce({ data: { id: 5 } });

    await expect(returnOrder(5, "Wrong item", options, KEY, noWait)).resolves.toEqual({ id: 5 });

    expect(post).toHaveBeenCalledTimes(3);
    expect(post.mock.calls[1]).toEqual(post.mock.calls[0]);
    expect(post.mock.calls[2]).toEqual(post.mock.calls[0]);
  });

  it.each([400, 403, 409])("never re-sends a definite answer (HTTP %i)", async (status) => {
    post.mockImplementation(async () => {
      throw httpError(status, { code: "ORDER_NOT_RETURNABLE" });
    });

    await expect(returnOrder(5, "Wrong item", options, KEY, noWait)).rejects.toMatchObject({ response: { status } });
    expect(post).toHaveBeenCalledTimes(1);
  });
});

describe("retryUncertain", () => {
  it("does not retry errors that are not HTTP errors (a bug must surface, not loop)", async () => {
    const send = vi.fn(async () => {
      throw new TypeError("boom");
    });

    await expect(retryUncertain(send, [0, 0])).rejects.toThrow("boom");
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("returns the first successful result without waiting or re-sending", async () => {
    const send = vi.fn(async () => "ok");

    await expect(retryUncertain(send, [0, 0])).resolves.toBe("ok");
    expect(send).toHaveBeenCalledTimes(1);
  });
});
