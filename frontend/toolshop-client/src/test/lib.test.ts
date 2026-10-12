import { describe, expect, it } from "vitest";
import { errorCode, errorMessage, errorShortages, errorStatus } from "../lib/apiError";
import { formatDate, formatMoney, formatNumber } from "../lib/format";

const axiosErr = (status: number, data: unknown) => ({ response: { status, data } });

describe("apiError", () => {
  it("reads the HTTP status", () => {
    expect(errorStatus(axiosErr(409, {}))).toBe(409);
    expect(errorStatus(new Error("network"))).toBeUndefined();
    expect(errorStatus(null)).toBeUndefined();
  });

  it("reads the server message from message/error/title/detail or a short string body", () => {
    expect(errorMessage(axiosErr(400, { message: "Bad" }))).toBe("Bad");
    expect(errorMessage(axiosErr(400, { title: "Validation" }))).toBe("Validation");
    expect(errorMessage(axiosErr(400, "plain text"))).toBe("plain text");
    expect(errorMessage(axiosErr(500, "x".repeat(400)))).toBeUndefined(); // never dump a huge body on screen
    expect(errorMessage(axiosErr(400, { other: 1 }))).toBeUndefined();
  });

  it("reads the machine code", () => {
    expect(errorCode(axiosErr(400, { code: "WALKIN_FULL_PAYMENT_REQUIRED" }))).toBe("WALKIN_FULL_PAYMENT_REQUIRED");
    expect(errorCode(axiosErr(400, { message: "x" }))).toBeUndefined();
  });

  it("reads stock shortages and falls back to an empty list", () => {
    const s = [{ productId: 1, productName: "Drill", available: 3, requested: 5 }];
    expect(errorShortages(axiosErr(409, { shortages: s }))).toEqual(s);
    expect(errorShortages(axiosErr(409, { shortages: "nope" }))).toEqual([]);
    expect(errorShortages(undefined)).toEqual([]);
  });
});

describe("format", () => {
  it("never shows a missing amount as zero", () => {
    expect(formatMoney(null)).toBe("N/A");
    expect(formatMoney(undefined)).toBe("N/A");
    expect(formatMoney(Number.NaN)).toBe("N/A");
    expect(formatMoney(0)).toBe("0 TJS"); // a real zero is still zero
    expect(formatNumber(null)).toBe("N/A");
  });

  it("formats money with a space as the thousands separator", () => {
    expect(formatMoney(17290)).toBe("17 290 TJS");
    expect(formatMoney(5200)).toBe("5 200 TJS");
    expect(formatMoney(12350.5)).toBe("12 350.50 TJS");
    expect(formatMoney(1234567)).toBe("1 234 567 TJS");
  });

  it("formats dates as dd.MM.yyyy and tolerates bad input", () => {
    expect(formatDate(new Date(2026, 9, 7))).toBe("07.10.2026");
    expect(formatDate(null)).toBe("—");
    expect(formatDate("not a date")).toBe("—");
  });
});
