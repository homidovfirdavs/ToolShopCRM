import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import {
  getProducts,
  getCategories,
  getBrands,
  deactivateProduct,
  type ProductListItem,
  type Category,
  type Brand,
} from "../api/catalog";
import { useAuth } from "../context/AuthContext";

export default function ProductsPage() {
  const { user } = useAuth();
  const canManage = user?.roles.includes("Admin") || user?.roles.includes("Manager");

  const [items, setItems] = useState<ProductListItem[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [brands, setBrands] = useState<Brand[]>([]);

  const [search, setSearch] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [brandId, setBrandId] = useState("");
  const [status, setStatus] = useState("");

  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);

  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadProducts = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await getProducts({
        search: search || undefined,
        categoryId: categoryId ? Number(categoryId) : undefined,
        brandId: brandId ? Number(brandId) : undefined,
        isActive: status === "" ? undefined : status === "active",
        page,
        pageSize: 20,
      });
      setItems(result.items);
      setTotalPages(result.totalPages);
      setTotalItems(result.totalItems);
    } catch {
      setError("Error loading products.");
    } finally {
      setIsLoading(false);
    }
  }, [search, categoryId, brandId, status, page]);

  useEffect(() => {
    getCategories().then(setCategories).catch(() => {});
    getBrands().then(setBrands).catch(() => {});
  }, []);

  useEffect(() => {
    loadProducts();
  }, [loadProducts]);

  useEffect(() => {
    setPage(1);
  }, [search, categoryId, brandId, status]);

  async function handleDeactivate(id: number) {
    if (!confirm("Deactivate this product?")) return;
    try {
      await deactivateProduct(id);
      loadProducts();
    } catch {
      alert("Failed to deactivate product.");
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-7xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-2xl font-bold text-gray-800">Products</h1>
          {canManage && (
            <Link
              to="/products/new"
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 transition"
            >
              + Add Product
            </Link>
          )}
        </div>

        <div className="bg-white rounded-lg shadow p-4 mb-4 flex flex-wrap gap-3">
          <input
            type="text"
            placeholder="Search products..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2 flex-1 min-w-[200px]"
          />

          <select
            value={categoryId}
            onChange={(e) => setCategoryId(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="">All Categories</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>

          <select
            value={brandId}
            onChange={(e) => setBrandId(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="">All Brands</option>
            {brands.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </select>

          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="">All Statuses</option>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
          </select>
        </div>

        <div className="bg-white rounded-lg shadow overflow-hidden">
          {isLoading ? (
            <p className="p-6 text-center text-gray-500">Loading...</p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-gray-500">No products found.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">SKU</th>
                  <th className="px-4 py-3">Barcode</th>
                  <th className="px-4 py-3">Category</th>
                  <th className="px-4 py-3">Brand</th>
                  <th className="px-4 py-3">Purchase</th>
                  <th className="px-4 py-3">Sale</th>
                  <th className="px-4 py-3">Status</th>
                  {canManage && <th className="px-4 py-3">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((p) => (
                  <tr key={p.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <Link
                        to={`/products/${p.id}`}
                        className="text-blue-600 hover:underline"
                      >
                        {p.name}
                      </Link>
                    </td>
                    <td className="px-4 py-3">{p.sku}</td>
                    <td className="px-4 py-3">{p.barcode ?? "—"}</td>
                    <td className="px-4 py-3">{p.categoryName}</td>
                    <td className="px-4 py-3">{p.brandName}</td>
                    <td className="px-4 py-3">{p.purchasePrice.toFixed(2)}</td>
                    <td className="px-4 py-3">{p.salePrice.toFixed(2)}</td>
                    <td className="px-4 py-3">
                      <span
                        className={
                          p.isActive
                            ? "text-green-600 font-medium"
                            : "text-gray-400"
                        }
                      >
                        {p.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    {canManage && (
                      <td className="px-4 py-3 space-x-3">
                        <Link
                          to={`/products/${p.id}/edit`}
                          className="text-blue-600 hover:underline"
                        >
                          Edit
                        </Link>
                        {p.isActive && (
                          <button
                            onClick={() => handleDeactivate(p.id)}
                            className="text-red-600 hover:underline"
                          >
                            Deactivate
                          </button>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {totalPages > 1 && (
          <div className="flex justify-between items-center mt-4 text-sm text-gray-600">
            <span>
              Page {page} of {totalPages} ({totalItems} items)
            </span>
            <div className="space-x-2">
              <button
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
                className="px-3 py-1 border rounded-md disabled:opacity-40"
              >
                Previous
              </button>
              <button
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="px-3 py-1 border rounded-md disabled:opacity-40"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}