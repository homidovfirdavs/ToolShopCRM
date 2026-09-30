import apiClient from "./client";
import type { PagedResult } from "./catalog";

export type PurchaseStatus = "Draft" | "Confirmed" | "Cancelled";

export interface PurchaseItemLine {
  id: number;
  productId: number;
  productName: string;
  sku: string;
  quantity: number;
  unitCost: number;
  lineTotal: number;
}

export interface Purchase {
  id: number;
  purchaseNumber: string;
  supplierId: number;
  supplierName: string;
  status: PurchaseStatus;
  purchaseDate: string;
  notes: string | null;
  subtotal: number;
  discountAmount: number;
  totalAmount: number;
  items: PurchaseItemLine[];
  createdByUsername: string;
  confirmedByUsername: string | null;
  createdAt: string;
  confirmedAt: string | null;
  cancelledAt: string | null;
}

export interface PurchaseListItem {
  id: number;
  purchaseNumber: string;
  supplierName: string;
  purchaseDate: string;
  status: PurchaseStatus;
  totalAmount: number;
  createdByUsername: string;
  createdAt: string;
}

export interface PurchaseQuery {
  search?: string;
  supplierId?: number;
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface PurchaseItemInput {
  productId: number;
  quantity: number;
  unitCost: number;
}

export interface PurchaseFormValues {
  supplierId: number;
  purchaseDate: string;
  notes: string;
  discountAmount: number;
  items: PurchaseItemInput[];
}

export async function getPurchases(
  query: PurchaseQuery
): Promise<PagedResult<PurchaseListItem>> {
  const res = await apiClient.get("/purchases", { params: query });
  return res.data;
}

export async function getPurchase(id: number): Promise<Purchase> {
  const res = await apiClient.get(`/purchases/${id}`);
  return res.data;
}

export async function createPurchase(values: PurchaseFormValues) {
  const res = await apiClient.post("/purchases", values);
  return res.data;
}

export async function updatePurchase(id: number, values: PurchaseFormValues) {
  const res = await apiClient.put(`/purchases/${id}`, values);
  return res.data;
}

export async function confirmPurchase(id: number) {
  const res = await apiClient.post(`/purchases/${id}/confirm`);
  return res.data;
}

export async function cancelPurchase(id: number) {
  await apiClient.post(`/purchases/${id}/cancel`);
}