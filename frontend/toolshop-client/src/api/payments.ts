import apiClient from "./client";
import { isUncertainOutcome } from "../lib/apiError";
import type { PagedResult } from "./catalog";

export type PaymentMethod = "Cash" | "Card" | "BankTransfer" | "QR" | "Other";
export type PaymentStatus = "Completed" | "Voided" | "Refunded";
export type OrderPaymentState = "Unpaid" | "PartiallyPaid" | "Paid" | "Refunded";

export interface Payment {
  id: number;
  paymentNumber: string;
  orderId: number;
  orderNumber: string;
  customerId: number | null;
  customerName: string;
  amount: number;
  method: PaymentMethod;
  status: PaymentStatus;
  referenceNumber: string | null;
  notes: string | null;
  receivedByUsername: string;
  createdAt: string;
  voidedAt: string | null;
  voidedByUsername: string | null;
  voidReason: string | null;
  orderTotal: number;
  totalPaid: number;
  outstandingBalance: number;
  paymentState: OrderPaymentState;
}

export interface PaymentListItem {
  id: number;
  paymentNumber: string;
  createdAt: string;
  orderNumber: string;
  customerName: string;
  amount: number;
  method: PaymentMethod;
  status: PaymentStatus;
  receivedByUsername: string;
}

export interface PaymentQuery {
  orderId?: number;
  customerId?: number;
  method?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface OrderPaymentSummary {
  orderTotal: number;
  totalPaid: number;
  /** Money actually paid back to the customer because of returns. */
  refundedTotal?: number;
  /** Value of the goods that came back (reduces what the customer owes). */
  returnedValue?: number;
  outstandingBalance: number;
  paymentState: OrderPaymentState;
  payments: PaymentListItem[];
}

export interface OutstandingOrder {
  orderId: number;
  orderNumber: string;
  orderDate: string;
  total: number;
  paid: number;
  outstanding: number;
  paymentState: OrderPaymentState;
}

export interface DebtListItem {
  customerId: number;
  customerName: string;
  phone: string | null;
  outstandingOrders: number;
  totalDebt: number;
  isActive?: boolean;
  lastPurchase: string | null;
}

export async function getPayments(query: PaymentQuery): Promise<PagedResult<PaymentListItem>> {
  const res = await apiClient.get("/payments", { params: query });
  return res.data;
}

export async function getPayment(id: number): Promise<Payment> {
  const res = await apiClient.get(`/payments/${id}`);
  return res.data;
}

/** Waits between automatic re-sends after an uncertain outcome (see createPayment). */
export const PAYMENT_RETRY_DELAYS_MS = [500, 1500];

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/**
 * Records a payment. `idempotencyKey` identifies ONE user intention (make it when the form opens, not per attempt): the server
 * stores the payment once per key, so re-sending the same request is always safe. That is why a request with an uncertain
 * outcome (no answer, timeout, 5xx) is re-sent automatically with the SAME key; the server then either returns the payment it
 * already stored or records it now. Definite answers (2xx, 4xx) are never re-sent.
 */
export async function createPayment(
  orderId: number,
  amount: number,
  method: PaymentMethod,
  referenceNumber: string,
  notes: string,
  idempotencyKey: string,
  options: { retryDelaysMs?: number[] } = {}
): Promise<Payment> {
  const delays = options.retryDelaysMs ?? PAYMENT_RETRY_DELAYS_MS;

  for (let attempt = 0; ; attempt++) {
    try {
      const res = await apiClient.post(
        "/payments",
        { orderId, amount, method, referenceNumber, notes },
        { headers: { "Idempotency-Key": idempotencyKey } }
      );
      return res.data;
    } catch (err) {
      if (!isUncertainOutcome(err) || attempt >= delays.length) throw err;
      await sleep(delays[attempt]);
    }
  }
}

export async function voidPayment(id: number, reason: string) {
  const res = await apiClient.post(`/payments/${id}/void`, { reason });
  return res.data;
}

export async function getOrderPayments(orderId: number): Promise<OrderPaymentSummary> {
  const res = await apiClient.get(`/orders/${orderId}/payments`);
  return res.data;
}

export async function getCustomerOutstandingOrders(customerId: number): Promise<OutstandingOrder[]> {
  const res = await apiClient.get(`/customers/${customerId}/outstanding-orders`);
  return res.data;
}

export async function getDebts(search: string, page = 1): Promise<{
  items: DebtListItem[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  /** Unpaid COMPLETED Walk-in orders (historical data; not a customer debt). Absent on older backends. */
  walkInUnresolvedDebt?: number;
  walkInUnresolvedOrderCount?: number;
}> {
  const res = await apiClient.get("/debts", { params: { search: search || undefined, page, pageSize: 20 } });
  return res.data;
}

export interface WalkInException {
  orderId: number;
  orderNumber: string;
  orderDate: string;
  total: number;
  paid: number;
  outstanding: number;
}

/** Historical unpaid Walk-in orders (Admin/Manager, read-only). */
export async function getWalkInExceptions(): Promise<{ items: WalkInException[]; totalOutstanding: number; count: number }> {
  const res = await apiClient.get("/debts/walk-in-exceptions");
  return res.data;
}
