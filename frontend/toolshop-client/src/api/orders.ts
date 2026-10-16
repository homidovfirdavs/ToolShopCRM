import apiClient from "./client";
import type { PagedResult } from "./catalog";
import type { PaymentMethod } from "./payments";

export type OrderStatus = "Draft" | "Completed" | "Cancelled" | "Returned" | "PartiallyReturned";

export interface OrderItemLine {
  id: number;
  productId: number;
  productName: string;
  sku: string;
  barcode: string | null;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  lineTotal: number;
}

export interface Order {
  id: number;
  orderNumber: string;
  customerId: number | null;
  customerName: string;
  status: OrderStatus;
  orderDate: string;
  notes: string | null;
  subtotal: number;
  discountAmount: number;
  totalAmount: number;
  items: OrderItemLine[];
  createdByUsername: string;
  completedByUsername: string | null;
  createdAt: string;
  completedAt: string | null;
  cancelledAt: string | null;
  /** Filled when the order has at least one return (PartiallyReturned / Returned): the LATEST return + totals. */
  returnedAt?: string | null;
  returnedByUsername?: string | null;
  returnReason?: string | null;
  /** Money actually paid back to the customer over all returns. */
  refundedAmount?: number | null;
  /** Value of all returned goods (after the order discount was allocated). */
  returnedValue?: number;
  returns?: OrderReturn[];
}

export interface OrderReturnItem {
  orderItemId: number;
  productId: number;
  productName: string;
  quantity: number;
  restockedQuantity: number;
  writtenOffQuantity: number;
  netValue: number;
}

/** One immutable return operation (the ledger row). Each return is dated by the moment it happened. */
export interface OrderReturn {
  id: number;
  returnNumber: string;
  returnedAt: string;
  returnedByUsername: string;
  reason: string;
  goodsValue: number;
  refundAmount: number;
  refundMethod: PaymentMethod | null;
  items: OrderReturnItem[];
}

export interface OrderListItem {
  id: number;
  orderNumber: string;
  customerName: string;
  orderDate: string;
  status: OrderStatus;
  totalAmount: number;
  createdByUsername: string;
}

export interface OrderQuery {
  search?: string;
  status?: string;
  customerId?: number;
  page?: number;
  pageSize?: number;
}

export interface OrderItemInput {
  productId: number;
  quantity: number;
  discountAmount: number;
}

export interface OrderFormValues {
  customerId: number | null;
  notes: string;
  discountAmount: number;
  items: OrderItemInput[];
}

export async function getOrders(query: OrderQuery): Promise<PagedResult<OrderListItem>> {
  const res = await apiClient.get("/orders", { params: query });
  return res.data;
}

export async function getOrder(id: number): Promise<Order> {
  const res = await apiClient.get(`/orders/${id}`);
  return res.data;
}

export async function createOrder(values: OrderFormValues) {
  const res = await apiClient.post("/orders", values);
  return res.data;
}

/** Payment taken at the moment of sale; saved in the same server transaction as the completion. */
export interface InitialPayment {
  amount: number;
  method: PaymentMethod;
  referenceNumber?: string;
  notes?: string;
}

export async function completeOrder(id: number, initialPayment?: InitialPayment): Promise<Order> {
  const res = await apiClient.post(`/orders/${id}/complete`, initialPayment ? { initialPayment } : undefined);
  return res.data;
}

export async function cancelOrder(id: number) {
  await apiClient.post(`/orders/${id}/cancel`);
}

export interface ReturnLineInput {
  orderItemId: number;
  quantity: number;
  /** How many of the returned units are damaged / not sellable. They do NOT go back to stock. */
  damagedQuantity: number;
}

export interface ReturnOptions {
  /** Omit to return everything that is still left (all units sellable). */
  items?: ReturnLineInput[];
  /** Omit to use the method of the latest payment. */
  refundMethod?: PaymentMethod;
  /** Omit to refund everything the customer has overpaid (never more than that). */
  refundAmount?: number;
}

/**
 * Return goods of a sold order, whole or in part (Admin/Manager). Each call is one return operation dated now.
 * Sellable units go back to stock; damaged units are written off; payments are never rewritten.
 */
export async function returnOrder(id: number, reason: string, options: ReturnOptions = {}): Promise<Order> {
  const res = await apiClient.post(`/orders/${id}/return`, { reason, ...options });
  return res.data;
}
