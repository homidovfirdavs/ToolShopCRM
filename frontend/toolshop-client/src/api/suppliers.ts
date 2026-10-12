import apiClient from "./client";
import type { PagedResult } from "./catalog";

export interface Supplier {
  id: number;
  name: string;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface SupplierListItem {
  id: number;
  name: string;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
}

export interface SupplierQuery {
  search?: string;
  isActive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface SupplierFormValues {
  name: string;
  contactPerson: string;
  phone: string;
  email: string;
  address: string;
  notes: string;
  isActive?: boolean;
}

export async function getSuppliers(
  query: SupplierQuery
): Promise<PagedResult<SupplierListItem>> {
  const res = await apiClient.get("/suppliers", { params: query });
  return res.data;
}

export async function getSupplier(id: number): Promise<Supplier> {
  const res = await apiClient.get(`/suppliers/${id}`);
  return res.data;
}

export async function getSupplierPurchases(id: number, page = 1) {
  const res = await apiClient.get(`/suppliers/${id}/purchases`, {
    params: { page, pageSize: 10 },
  });
  return res.data;
}

/** Optional fields: empty string -> null (backend [EmailAddress] rejects ""). */
function cleanSupplier(values: SupplierFormValues) {
  const n = (v: string) => (v.trim() === "" ? null : v.trim());
  return {
    ...values,
    name: values.name.trim(),
    contactPerson: n(values.contactPerson),
    phone: n(values.phone),
    email: n(values.email),
    address: n(values.address),
    notes: n(values.notes),
  };
}

export async function createSupplier(values: SupplierFormValues) {
  const res = await apiClient.post("/suppliers", cleanSupplier(values));
  return res.data;
}

export async function updateSupplier(id: number, values: SupplierFormValues) {
  const res = await apiClient.put(`/suppliers/${id}`, cleanSupplier(values));
  return res.data;
}

export async function deactivateSupplier(id: number) {
  await apiClient.delete(`/suppliers/${id}`);
}