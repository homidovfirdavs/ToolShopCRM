import apiClient from "./client";

// ---------- Expense Categories ----------

export interface ExpenseCategory {
  id: number;
  name: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
}

export async function getExpenseCategories(includeInactive = false): Promise<ExpenseCategory[]> {
  const res = await apiClient.get("/expense-categories", { params: { includeInactive } });
  return res.data;
}

export async function createExpenseCategory(name: string, description: string | null) {
  const res = await apiClient.post("/expense-categories", { name, description });
  return res.data;
}

export async function updateExpenseCategory(id: number, name: string, description: string | null) {
  const res = await apiClient.put(`/expense-categories/${id}`, { name, description });
  return res.data;
}

export async function deactivateExpenseCategory(id: number) {
  await apiClient.delete(`/expense-categories/${id}`);
}

// ---------- Expenses ----------

export interface Expense {
  id: number;
  expenseNumber: string;
  categoryId: number;
  categoryName: string;
  amount: number;
  expenseDate: string;
  description: string;
  referenceNumber: string | null;
  notes: string | null;
  createdByUsername: string;
  createdAt: string;
  updatedAt: string | null;
  isVoided: boolean;
  voidedAt: string | null;
  voidedByUsername: string | null;
  voidReason: string | null;
}

export interface ExpenseListItem {
  id: number;
  expenseNumber: string;
  expenseDate: string;
  categoryName: string;
  description: string;
  amount: number;
  isVoided: boolean;
  createdByUsername: string;
}

export interface FinancePagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface ExpenseQuery {
  search?: string;
  categoryId?: number;
  fromDate?: string;
  toDate?: string;
  isVoided?: boolean;
  page?: number;
  pageSize?: number;
}

export async function getExpenses(query: ExpenseQuery): Promise<FinancePagedResult<ExpenseListItem>> {
  const res = await apiClient.get("/expenses", { params: query });
  return res.data;
}

export async function getExpense(id: number): Promise<Expense> {
  const res = await apiClient.get(`/expenses/${id}`);
  return res.data;
}

export async function createExpense(
  categoryId: number, amount: number, expenseDate: string, description: string,
  referenceNumber: string | null, notes: string | null
): Promise<Expense> {
  const res = await apiClient.post("/expenses", {
    categoryId, amount, expenseDate, description, referenceNumber, notes,
  });
  return res.data;
}

export async function updateExpense(
  id: number, categoryId: number, amount: number, expenseDate: string, description: string,
  referenceNumber: string | null, notes: string | null
): Promise<Expense> {
  const res = await apiClient.put(`/expenses/${id}`, {
    categoryId, amount, expenseDate, description, referenceNumber, notes,
  });
  return res.data;
}

export async function voidExpense(id: number, reason: string) {
  const res = await apiClient.post(`/expenses/${id}/void`, { reason });
  return res.data;
}

// ---------- Finance ----------

export interface FinanceSummary {
  revenue: number;
  cogs: number;
  grossProfit: number;
  operatingExpenses: number;
  netOperatingProfit: number;
  paymentsReceived: number;
  accountsReceivable: number;
  purchaseSpend: number;
  completedOrdersCount: number;
  averageOrderValue: number;
  grossMarginPercent: number;
  netOperatingMarginPercent: number;
  unknownCostItemsCount: number;
}

export interface DailyFinanceItem {
  date: string;
  revenue: number;
  cogs: number;
  grossProfit: number;
  expenses: number;
  netOperatingProfit: number;
  paymentsReceived: number;
}

export interface MonthlyFinanceItem {
  year: number;
  month: number;
  revenue: number;
  cogs: number;
  grossProfit: number;
  expenses: number;
  netOperatingProfit: number;
  paymentsReceived: number;
  purchaseSpend: number;
}

export interface ExpenseCategoryBreakdown {
  category: string;
  amount: number;
  percentage: number;
}

export interface PaymentMethodBreakdown {
  method: string;
  amount: number;
  count: number;
}

export interface ProfitByProductItem {
  productId: number;
  productName: string;
  sku: string;
  quantitySold: number;
  revenue: number;
  cogs: number;
  grossProfit: number;
  grossMarginPercent: number;
}

export interface ProfitByCategoryItem {
  category: string;
  quantitySold: number;
  revenue: number;
  cogs: number;
  grossProfit: number;
  marginPercent: number;
}

export async function getFinanceSummary(from: string, to: string): Promise<FinanceSummary> {
  const res = await apiClient.get("/finance/summary", { params: { from, to } });
  return res.data;
}

export async function getFinanceDaily(from: string, to: string): Promise<DailyFinanceItem[]> {
  const res = await apiClient.get("/finance/daily", { params: { from, to } });
  return res.data;
}

export async function getFinanceMonthly(from: string, to: string): Promise<MonthlyFinanceItem[]> {
  const res = await apiClient.get("/finance/monthly", { params: { from, to } });
  return res.data;
}

export async function getExpensesByCategory(from: string, to: string): Promise<ExpenseCategoryBreakdown[]> {
  const res = await apiClient.get("/finance/expenses-by-category", { params: { from, to } });
  return res.data;
}

export async function getPaymentsByMethod(from: string, to: string): Promise<PaymentMethodBreakdown[]> {
  const res = await apiClient.get("/finance/payments-by-method", { params: { from, to } });
  return res.data;
}

export async function getProfitByProduct(
  from: string, to: string, sortBy = "profit", page = 1, pageSize = 20
): Promise<FinancePagedResult<ProfitByProductItem>> {
  const res = await apiClient.get("/finance/profit-by-product", { params: { from, to, sortBy, page, pageSize } });
  return res.data;
}

export async function getProfitByCategory(from: string, to: string): Promise<ProfitByCategoryItem[]> {
  const res = await apiClient.get("/finance/profit-by-category", { params: { from, to } });
  return res.data;
}