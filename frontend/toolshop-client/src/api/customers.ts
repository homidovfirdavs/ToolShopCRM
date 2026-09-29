import apiClient from "./client";
import type { PagedResult } from "./catalog";

export interface Customer {
  id: number;
  name: string;
  phone: string;
  address: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface CustomerListItem {
  id: number;
  name: string;
  phone: string;
  address: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CustomerQuery {
  search?: string;
  isActive?: boolean;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
  page?: number;
  pageSize?: number;
}

export interface CustomerFormValues {
  name: string;
  phone: string;
  address: string;
  notes: string;
  isActive?: boolean;
}

export async function getCustomers(
  query: CustomerQuery
): Promise<PagedResult<CustomerListItem>> {
  const res = await apiClient.get("/customers", { params: query });
  return res.data;
}

export async function getCustomer(id: number): Promise<Customer> {
  const res = await apiClient.get(`/customers/${id}`);
  return res.data;
}

export async function createCustomer(values: CustomerFormValues) {
  const res = await apiClient.post("/customers", values);
  return res.data;
}

export async function updateCustomer(id: number, values: CustomerFormValues) {
  const res = await apiClient.put(`/customers/${id}`, values);
  return res.data;
}

export async function deactivateCustomer(id: number) {
  await apiClient.delete(`/customers/${id}`);
}