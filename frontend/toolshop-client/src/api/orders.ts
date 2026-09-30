import apiClient from "./client";
import type { PagedResult } from "./catalog";

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

export async function completeOrder(id: number): Promise<Order> {
  const res = await apiClient.post(`/orders/${id}/complete`);
  return res.data;
}

export async function cancelOrder(id: number) {
  await apiClient.post(`/orders/${id}/cancel`);
}