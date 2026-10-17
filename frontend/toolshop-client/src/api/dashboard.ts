import apiClient from "./client";

export interface DashboardSummary {
  dateFrom: string;
  dateTo: string;
  salesRevenue: number;
  grossProfit: number | null;
  paymentsReceived: number;
  accountsReceivable: number;
  lowStockCount: number;
  operatingExpenses: number;
  hasCompleteCostData: boolean;
  outOfStockCount: number;
  completedOrdersCount: number;
}

export interface SalesTrendPoint {
  date: string;
  revenue: number;
  grossProfit: number;
  hasCompleteCostData: boolean;
}

export interface CategoryBreakdown {
  category: string;
  revenue: number;
  percentage: number;
}

export interface AttentionItem {
  type: string;
  label: string;
  count: number;
  amount: number | null;
  link: string;
}

export interface LowStockItem {
  productId: number;
  productName: string;
  sku: string;
  currentStock: number;
  minimumStockLevel: number;
  status: string;
}

export interface FinancialSummaryShort {
  revenue: number;
  cogs: number | null;
  grossProfit: number | null;
  operatingExpenses: number;
  netOperatingProfit: number | null;
  hasCompleteCostData: boolean;
}

export interface TopProductItem {
  productId: number;
  productName: string;
  sku: string;
  quantitySold: number;
  revenue: number;
  grossProfit: number | null;
}

export interface TopProducts {
  dateFrom: string;
  dateTo: string;
  bestSelling: TopProductItem[];
  mostProfitable: TopProductItem[];
  unknownProfit: TopProductItem[];
}

export async function getDashboardSummary(from: string, to: string): Promise<DashboardSummary> {
  const res = await apiClient.get("/dashboard/summary", { params: { from, to } });
  return res.data;
}

export async function getSalesTrend(from: string, to: string): Promise<SalesTrendPoint[]> {
  const res = await apiClient.get("/dashboard/sales-trend", { params: { from, to } });
  return res.data;
}

export async function getSalesByCategory(from: string, to: string): Promise<CategoryBreakdown[]> {
  const res = await apiClient.get("/dashboard/sales-by-category", { params: { from, to } });
  return res.data;
}

export async function getAttention(from: string, to: string): Promise<AttentionItem[]> {
  const res = await apiClient.get("/dashboard/attention", { params: { from, to } });
  return res.data;
}

export async function getLowStock(top = 10): Promise<LowStockItem[]> {
  const res = await apiClient.get("/dashboard/low-stock", { params: { top } });
  return res.data;
}

export async function getFinancialSummaryShort(from: string, to: string): Promise<FinancialSummaryShort> {
  const res = await apiClient.get("/dashboard/financial-summary", { params: { from, to } });
  return res.data;
}

export async function getTopProducts(from: string, to: string, top = 5): Promise<TopProducts> {
  const res = await apiClient.get("/dashboard/top-products", { params: { from, to, top } });
  return res.data;
}