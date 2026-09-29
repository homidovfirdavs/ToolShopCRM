import apiClient from "./client";
import type { PagedResult } from "./catalog";

export type StockStatus = "InStock" | "LowStock" | "OutOfStock";

export type MovementType =
  | "StockIn"
  | "StockOut"
  | "AdjustmentIncrease"
  | "AdjustmentDecrease"
  | "Sale"
  | "SaleReturn"
  | "Purchase"
  | "PurchaseReturn";

export interface StockItem {
  productId: number;
  productName: string;
  sku: string;
  barcode: string | null;
  categoryName: string;
  brandName: string;
  currentStock: number;
  minimumStockLevel: number;
  status: StockStatus;
}

export interface Movement {
  id: number;
  createdAt: string;
  productId: number;
  productName: string;
  sku: string;
  movementType: MovementType;
  quantity: number;
  reason: string;
  notes: string | null;
  referenceType: string | null;
  referenceId: number | null;
  createdByUserId: string;
  createdByUsername: string;
}

export interface StockQuery {
  search?: string;
  categoryId?: number;
  brandId?: number;
  status?: StockStatus;
  page?: number;
  pageSize?: number;
}

export interface MovementQuery {
  productId?: number;
  movementType?: MovementType;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export async function getStock(query: StockQuery): Promise<PagedResult<StockItem>> {
  const res = await apiClient.get("/inventory/stock", { params: query });
  return res.data;
}

export async function getSingleStock(productId: number): Promise<StockItem> {
  const res = await apiClient.get(`/inventory/stock/${productId}`);
  return res.data;
}

export async function getMovements(
  query: MovementQuery
): Promise<PagedResult<Movement>> {
  const res = await apiClient.get("/inventory/movements", { params: query });
  return res.data;
}

export async function stockIn(
  productId: number,
  quantity: number,
  reason: string,
  notes: string
) {
  const res = await apiClient.post("/inventory/stock-in", {
    productId,
    quantity,
    reason,
    notes,
  });
  return res.data;
}

export async function stockOut(
  productId: number,
  quantity: number,
  reason: string,
  notes: string
) {
  const res = await apiClient.post("/inventory/stock-out", {
    productId,
    quantity,
    reason,
    notes,
  });
  return res.data;
}

export async function createAdjustment(
  productId: number,
  type: "AdjustmentIncrease" | "AdjustmentDecrease",
  quantity: number,
  reason: string,
  notes: string
) {
  const res = await apiClient.post("/inventory/adjustment", {
    productId,
    type,
    quantity,
    reason,
    notes,
  });
  return res.data;
}