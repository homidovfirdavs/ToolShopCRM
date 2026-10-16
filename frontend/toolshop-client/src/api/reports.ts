import apiClient from "./client";

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

// ===================== SALES REPORT =====================

export interface SalesReportRow {
  orderId: number;
  orderNumber: string;
  orderDate: string;
  customerName: string | null;
  cashierName: string;
  status: string;
  subtotal: number;
  discountAmount: number;
  totalAmount: number;
  totalPaid: number;
  outstandingBalance: number;
  paymentStatus: string;
}

export interface SalesReportParams {
  from?: string;
  to?: string;
  customerId?: number;
  status?: string;
  cashierUserId?: string;
  paymentStatus?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export async function getSalesReport(params: SalesReportParams): Promise<PagedResult<SalesReportRow>> {
  const res = await apiClient.get("/reports/sales", { params });
  return res.data;
}

export interface SalesReportSummary {
  orderCount: number;
  subtotal: number;
  discountAmount: number;
  totalAmount: number;
  totalPaid: number;
  outstandingBalance: number;
  completedOrderCount: number;
  completedTotalAmount: number;
  completedTotalPaid: number;
  completedOutstandingBalance: number;
}

/** Totals over ALL orders matching the filters (not just the current page). */
export async function getSalesSummary(
  params: Omit<SalesReportParams, "page" | "pageSize">,
): Promise<SalesReportSummary> {
  const res = await apiClient.get("/reports/sales/summary", { params });
  return res.data;
}

export function exportSalesReport(params: Omit<SalesReportParams, "page" | "pageSize">) {
  return downloadCsv("/reports/sales/export", params, "sales-report.csv");
}

// ===================== INVENTORY REPORT =====================

export interface InventoryReportRow {
  productId: number;
  productName: string;
  sku: string;
  categoryName: string;
  brandName: string;
  currentStock: number;
  minimumStockLevel: number;
  stockStatus: string;
  purchasePrice: number;
  salePrice: number;
  stockValue: number;
}

export interface InventoryReportParams {
  search?: string;
  categoryId?: number;
  brandId?: number;
  stockStatus?: string;
  page?: number;
  pageSize?: number;
}

export async function getInventoryReport(params: InventoryReportParams): Promise<PagedResult<InventoryReportRow>> {
  const res = await apiClient.get("/reports/inventory", { params });
  return res.data;
}

export function exportInventoryReport(params: Omit<InventoryReportParams, "page" | "pageSize">) {
  return downloadCsv("/reports/inventory/export", params, "inventory-report.csv");
}

// ===================== CUSTOMER DEBT REPORT =====================

export interface CustomerDebtReportRow {
  customerId: number;
  customerName: string;
  phone: string;
  outstandingOrderCount: number;
  totalDebt: number;
  lastPurchaseDate: string | null;
}

export interface CustomerDebtReportParams {
  search?: string;
  page?: number;
  pageSize?: number;
}

export async function getCustomerDebtReport(params: CustomerDebtReportParams): Promise<PagedResult<CustomerDebtReportRow>> {
  const res = await apiClient.get("/reports/customer-debt", { params });
  return res.data;
}

export function exportCustomerDebtReport(params: Omit<CustomerDebtReportParams, "page" | "pageSize">) {
  return downloadCsv("/reports/customer-debt/export", params, "customer-debt-report.csv");
}

// ===================== PURCHASE REPORT =====================

export interface PurchaseReportRow {
  purchaseId: number;
  purchaseNumber: string;
  purchaseDate: string;
  supplierName: string;
  status: string;
  subtotal: number;
  discountAmount: number;
  totalAmount: number;
  createdByUserName: string;
  confirmedAt: string | null;
}

export interface PurchaseReportParams {
  from?: string;
  to?: string;
  supplierId?: number;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export async function getPurchaseReport(params: PurchaseReportParams): Promise<PagedResult<PurchaseReportRow>> {
  const res = await apiClient.get("/reports/purchases", { params });
  return res.data;
}

export function exportPurchaseReport(params: Omit<PurchaseReportParams, "page" | "pageSize">) {
  return downloadCsv("/reports/purchases/export", params, "purchase-report.csv");
}

// ===================== EXPENSE REPORT =====================

export interface ExpenseReportRow {
  expenseId: number;
  expenseNumber: string;
  expenseDate: string;
  categoryName: string;
  description: string;
  amount: number;
  createdByUserName: string;
  isVoided: boolean;
}

export interface ExpenseReportParams {
  from?: string;
  to?: string;
  categoryId?: number;
  isVoided?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

export async function getExpenseReport(params: ExpenseReportParams): Promise<PagedResult<ExpenseReportRow>> {
  const res = await apiClient.get("/reports/expenses", { params });
  return res.data;
}

export function exportExpenseReport(params: Omit<ExpenseReportParams, "page" | "pageSize">) {
  return downloadCsv("/reports/expenses/export", params, "expense-report.csv");
}

// ===================== PAYMENT REPORT =====================

export interface PaymentReportRow {
  paymentId: number;
  paymentNumber: string;
  createdAt: string;
  orderNumber: string;
  customerName: string;
  amount: number;
  method: string;
  status: string;
  receivedByUserName: string;
}

export interface PaymentReportParams {
  from?: string;
  to?: string;
  customerId?: number;
  method?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export async function getPaymentReport(params: PaymentReportParams): Promise<PagedResult<PaymentReportRow>> {
  const res = await apiClient.get("/reports/payments", { params });
  return res.data;
}

export function exportPaymentReport(params: Omit<PaymentReportParams, "page" | "pageSize">) {
  return downloadCsv("/reports/payments/export", params, "payment-report.csv");
}

// ===================== RETURNS REPORT (by date of the RETURN) =====================

export interface ReturnReportRow {
  returnId: number;
  returnNumber: string;
  returnedAt: string;
  orderId: number;
  orderNumber: string;
  customerName: string | null;
  returnedByUserName: string;
  reason: string;
  returnedUnits: number;
  restockedUnits: number;
  writtenOffUnits: number;
  goodsValue: number;
  refundAmount: number;
  refundMethod: string | null;
  /** null = cost unknown (show N/A, never 0). */
  costReversed: number | null;
  writeOffCost: number | null;
}

export interface ReturnsReport {
  items: ReturnReportRow[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  returnsCount: number;
  goodsValue: number;
  refundsPaid: number;
  /** null = at least one return in the period has an unknown cost. */
  costReversed: number | null;
  writeOffCost: number | null;
}

export async function getReturnsReport(params: {
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}): Promise<ReturnsReport> {
  const res = await apiClient.get("/reports/returns", { params });
  return res.data;
}

// ===================== PRODUCT PERFORMANCE REPORT =====================

export interface ProductPerformanceRow {
  productId: number;
  productName: string;
  sku: string;
  quantitySold: number;
  revenue: number;
  cogs: number | null;
  grossProfit: number | null;
  grossMarginPercent: number | null;
  hasCompleteCostData: boolean;
}

export interface ProductPerformanceParams {
  from?: string;
  to?: string;
  sortBy?: string;
  page?: number;
  pageSize?: number;
}

export async function getProductPerformance(params: ProductPerformanceParams): Promise<PagedResult<ProductPerformanceRow>> {
  const res = await apiClient.get("/reports/product-performance", { params });
  return res.data;
}

// ===================== CATEGORY PERFORMANCE REPORT =====================

export interface CategoryPerformanceRow {
  category: string;
  quantitySold: number;
  revenue: number;
  cogs: number | null;
  grossProfit: number | null;
  marginPercent: number | null;
  hasCompleteCostData: boolean;
}

export async function getCategoryPerformance(from?: string, to?: string): Promise<CategoryPerformanceRow[]> {
  const res = await apiClient.get("/reports/category-performance", { params: { from, to } });
  return res.data;
}

// ===================== PROFITABILITY REPORT =====================

export interface ProfitabilityReport {
  dateFrom: string;
  dateTo: string;
  revenue: number;
  cogs: number | null;
  grossProfit: number | null;
  grossMarginPercent: number | null;
  operatingExpenses: number;
  netOperatingProfit: number | null;
  netOperatingMarginPercent: number | null;
  paymentsReceived: number;
  accountsReceivable: number;
  purchaseSpend: number;
  completedOrdersCount: number;
  averageOrderValue: number;
  hasCompleteCostData: boolean;
}

export async function getProfitabilityReport(from?: string, to?: string): Promise<ProfitabilityReport> {
  const res = await apiClient.get("/reports/profitability", { params: { from, to } });
  return res.data;
}

// ===================== CSV DOWNLOAD HELPER =====================

async function downloadCsv(url: string, params: Record<string, unknown>, fallbackFileName: string) {
  const res = await apiClient.get(url, { params, responseType: "blob" });
  const blob = new Blob([res.data], { type: "text/csv;charset=utf-8;" });

  let fileName = fallbackFileName;
  const disposition = res.headers["content-disposition"];
  if (disposition) {
    const match = disposition.match(/filename="?([^"]+)"?/);
    if (match) fileName = match[1];
  }

  const link = document.createElement("a");
  const objectUrl = URL.createObjectURL(blob);
  link.href = objectUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(objectUrl);
}