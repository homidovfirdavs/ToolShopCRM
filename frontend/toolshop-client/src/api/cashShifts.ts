import apiClient from "./client";
import { retryUncertain } from "../lib/idempotency";

export type CashShiftStatus = "Open" | "Closed";
export type CashMovementType = "CashIn" | "CashOut";

/** Money fields are null (shown as N/A) when the caller may not see them yet - never 0. */
export interface CashBreakdown {
  cashSales: number | null;
  cashRefunds: number | null;
  cashPaymentsVoidedFromOtherShifts: number | null;
  cashIn: number | null;
  cashOut: number | null;
  cardSales: number;
  bankTransferSales: number;
  qrSales: number;
  otherSales: number;
  paymentCount: number;
  refundCount: number;
  movementCount: number;
}

export interface CashShift {
  id: number;
  shiftNumber: string;
  cashRegisterId: number;
  cashRegisterName: string | null;
  status: CashShiftStatus;
  cashierUserId: string;
  cashierName: string | null;
  openedAt: string;
  openedByName: string | null;
  openingCash: number;
  closedAt: string | null;
  closedByName: string | null;
  closingCash: number | null;
  expectedCash: number | null;
  cashDifference: number | null;
  closeNote: string | null;
  closedByOther: boolean;
  requiresReview: boolean;
  reviewed: boolean;
  reviewedAt: string | null;
  reviewedByName: string | null;
  reviewNote: string | null;
  breakdown: CashBreakdown;
}

export interface CashMovement {
  id: number;
  cashShiftId: number;
  type: CashMovementType;
  amount: number;
  reason: string;
  expenseId: number | null;
  createdAt: string;
  createdByName: string | null;
  isVoided: boolean;
  voidedAt: string | null;
  voidedByName: string | null;
  voidReason: string | null;
}

export interface CashShiftListItem {
  id: number;
  shiftNumber: string;
  status: CashShiftStatus;
  cashierName: string | null;
  openedAt: string;
  closedAt: string | null;
  openingCash: number;
  closingCash: number | null;
  expectedCash: number | null;
  cashDifference: number | null;
  requiresReview: boolean;
  reviewed: boolean;
}

export type CurrentCashShift =
  | { isOpen: false; previousClosingCash: number | null }
  | { isOpen: true; shift: CashShift; movements: CashMovement[] };

export interface CashShiftPage {
  items: CashShiftListItem[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export async function getCurrentCashShift(): Promise<CurrentCashShift> {
  const res = await apiClient.get("/cash-shifts/current");
  return res.data;
}

export async function getCashShifts(page = 1): Promise<CashShiftPage> {
  const res = await apiClient.get("/cash-shifts", { params: { page, pageSize: 20 } });
  return res.data;
}

export async function getCashShift(id: number): Promise<{ shift: CashShift; movements: CashMovement[] }> {
  const res = await apiClient.get(`/cash-shifts/${id}`);
  return res.data;
}

export async function openCashShift(openingCash: number): Promise<CashShift> {
  const res = await apiClient.post("/cash-shifts/open", { openingCash });
  return res.data;
}

export async function closeCashShift(id: number, actualCash: number, note: string): Promise<CashShift> {
  const res = await apiClient.post(`/cash-shifts/${id}/close`, { actualCash, note: note.trim() || null });
  return res.data;
}

/**
 * Cash in / cash out. `idempotencyKey` identifies ONE intention (create it when the form opens): the server records the
 * movement once per key, so an uncertain outcome is re-sent with the SAME key.
 */
export async function addCashMovement(
  shiftId: number,
  type: CashMovementType,
  amount: number,
  reason: string,
  idempotencyKey: string,
  retryDelaysMs?: number[]
): Promise<CashMovement> {
  return retryUncertain(async () => {
    const res = await apiClient.post(
      `/cash-shifts/${shiftId}/movements`,
      { type, amount, reason: reason.trim() },
      { headers: { "Idempotency-Key": idempotencyKey } }
    );
    return res.data;
  }, retryDelaysMs);
}

export async function voidCashMovement(movementId: number, reason: string): Promise<CashMovement> {
  const res = await apiClient.post(`/cash-shifts/movements/${movementId}/void`, { reason: reason.trim() });
  return res.data;
}

export async function reviewCashShift(id: number, note: string): Promise<CashShift> {
  const res = await apiClient.post(`/cash-shifts/${id}/review`, { note: note.trim() || null });
  return res.data;
}
