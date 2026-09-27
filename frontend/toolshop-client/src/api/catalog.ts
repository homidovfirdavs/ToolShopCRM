import apiClient from "./client";

export interface Category {
  id: number;
  name: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface Brand {
  id: number;
  name: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface ProductListItem {
  id: number;
  name: string;
  sku: string;
  barcode: string | null;
  categoryName: string;
  brandName: string;
  purchasePrice: number;
  salePrice: number;
  isActive: boolean;
}

export interface Product {
  id: number;
  name: string;
  sku: string;
  barcode: string | null;
  categoryId: number;
  categoryName: string;
  brandId: number;
  brandName: string;
  purchasePrice: number;
  salePrice: number;
  minimumStockLevel: number;
  warrantyMonths: number;
  description: string | null;
  imageUrl: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface ProductQuery {
  search?: string;
  categoryId?: number;
  brandId?: number;
  isActive?: boolean;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
  page?: number;
  pageSize?: number;
}

export interface ProductFormValues {
  name: string;
  sku: string;
  barcode: string;
  categoryId: number;
  brandId: number;
  purchasePrice: number;
  salePrice: number;
  minimumStockLevel: number;
  warrantyMonths: number;
  description: string;
  imageUrl: string;
  isActive?: boolean;
}

// --- Categories ---

export async function getCategories(): Promise<Category[]> {
  const res = await apiClient.get("/categories");
  return res.data;
}

export async function createCategory(name: string, description: string) {
  const res = await apiClient.post("/categories", { name, description });
  return res.data;
}

export async function updateCategory(
  id: number,
  name: string,
  description: string,
  isActive: boolean
) {
  const res = await apiClient.put(`/categories/${id}`, {
    name,
    description,
    isActive,
  });
  return res.data;
}

export async function deactivateCategory(id: number) {
  await apiClient.delete(`/categories/${id}`);
}

// --- Brands ---

export async function getBrands(): Promise<Brand[]> {
  const res = await apiClient.get("/brands");
  return res.data;
}

export async function createBrand(name: string, description: string) {
  const res = await apiClient.post("/brands", { name, description });
  return res.data;
}

export async function updateBrand(
  id: number,
  name: string,
  description: string,
  isActive: boolean
) {
  const res = await apiClient.put(`/brands/${id}`, {
    name,
    description,
    isActive,
  });
  return res.data;
}

export async function deactivateBrand(id: number) {
  await apiClient.delete(`/brands/${id}`);
}

// --- Products ---

export async function getProducts(
  query: ProductQuery
): Promise<PagedResult<ProductListItem>> {
  const res = await apiClient.get("/products", { params: query });
  return res.data;
}

export async function getProduct(id: number): Promise<Product> {
  const res = await apiClient.get(`/products/${id}`);
  return res.data;
}

export async function createProduct(values: ProductFormValues) {
  const res = await apiClient.post("/products", values);
  return res.data;
}

export async function updateProduct(id: number, values: ProductFormValues) {
  const res = await apiClient.put(`/products/${id}`, values);
  return res.data;
}

export async function deactivateProduct(id: number) {
  await apiClient.delete(`/products/${id}`);
}