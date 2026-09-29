import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import {
  getStock,
  stockIn,
  stockOut,
  createAdjustment,
  type StockItem,
  type StockStatus,
} from "../api/inventory";
import { getCategories, getBrands, type Category, type Brand } from "../api/catalog";
import { useAuth } from "../context/AuthContext";

type ModalMode = "in" | "out" | "adjust" | null;

function StatusBadge({ status }: { status: StockStatus }) {
  const styles: Record<StockStatus, string> = {
    InStock: "text-green-600",
    LowStock: "text-yellow-600",
    OutOfStock: "text-red-600",
  };
  const labels: Record<StockStatus, string> = {
    InStock: "In Stock",
    LowStock: "Low Stock",
    OutOfStock: "Out of Stock",
  };
  return <span className={`font-medium ${styles[status]}`}>{labels[status]}</span>;
}

export default function InventoryPage() {
  const { user } = useAuth();
  const canManage =
    user?.roles.includes("Admin") ||
    user?.roles.includes("Manager") ||
    user?.roles.includes("Warehouse");

  const [items, setItems] = useState<StockItem[]>([]);
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

  const [modalMode, setModalMode] = useState<ModalMode>(null);
  const [selectedItem, setSelectedItem] = useState<StockItem | null>(null);
  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [adjustDirection, setAdjustDirection] = useState<"increase" | "decrease">("increase");
  const [modalError, setModalError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const loadStock = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await getStock({
        search: search || undefined,
        categoryId: categoryId ? Number(categoryId) : undefined,
        brandId: brandId ? Number(brandId) : undefined,
        status: status ? (status as StockStatus) : undefined,
        page,
        pageSize: 20,
      });
      setItems(result.items);
      setTotalPages(result.totalPages);
      setTotalItems(result.totalItems);
    } catch {
      setError("Unable to load inventory.");
    } finally {
      setIsLoading(false);
    }
  }, [search, categoryId, brandId, status, page]);

  useEffect(() => {
    getCategories().then(setCategories).catch(() => {});
    getBrands().then(setBrands).catch(() => {});
  }, []);

  useEffect(() => {
    loadStock();
  }, [loadStock]);

  useEffect(() => {
    setPage(1);
  }, [search, categoryId, brandId, status]);

  function openModal(mode: ModalMode, item: StockItem) {
    setModalMode(mode);
    setSelectedItem(item);
    setQuantity("");
    setReason("");
    setNotes("");
    setAdjustDirection("increase");
    setModalError(null);
  }

  function closeModal() {
    setModalMode(null);
    setSelectedItem(null);
  }

  async function handleConfirm() {
    if (!selectedItem) return;
    setModalError(null);

    const qty = Number(quantity);
    if (!qty || qty <= 0) {
      setModalError("Quantity must be greater than zero.");
      return;
    }
    if (!reason.trim()) {
      setModalError("Reason is required.");
      return;
    }
    if (modalMode === "out" && qty > selectedItem.currentStock) {
      setModalError(
        `Warning: quantity exceeds available stock (${selectedItem.currentStock}).`
      );
      return;
    }

    setIsSaving(true);
    try {
      if (modalMode === "in") {
        await stockIn(selectedItem.productId, qty, reason.trim(), notes.trim());
      } else if (modalMode === "out") {
        await stockOut(selectedItem.productId, qty, reason.trim(), notes.trim());
      } else if (modalMode === "adjust") {
        await createAdjustment(
          selectedItem.productId,
          adjustDirection === "increase" ? "AdjustmentIncrease" : "AdjustmentDecrease",
          qty,
          reason.trim(),
          notes.trim()
        );
      }
      closeModal();
      loadStock();
    } catch (err) {
      const response =
        typeof err === "object" && err !== null && "response" in err
          ? (err as { response?: { status?: number; data?: { message?: string } } })
              .response
          : undefined;

      if (response?.status === 409) {
        setModalError(response.data?.message ?? "Insufficient stock.");
      } else {
        setModalError("Something went wrong. Please try again.");
      }
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-7xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-2xl font-bold text-gray-800">Inventory</h1>
          <Link
            to="/inventory/movements"
            className="text-blue-600 hover:underline text-sm"
          >
            View Movement History
          </Link>
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
            <option value="InStock">In Stock</option>
            <option value="LowStock">Low Stock</option>
            <option value="OutOfStock">Out of Stock</option>
          </select>
        </div>

        <div className="bg-white rounded-lg shadow overflow-hidden">
          {isLoading ? (
            <p className="p-6 text-center text-gray-500">Loading inventory...</p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-gray-500">No products found.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Product</th>
                  <th className="px-4 py-3">SKU</th>
                  <th className="px-4 py-3">Category</th>
                  <th className="px-4 py-3">Brand</th>
                  <th className="px-4 py-3">Current Stock</th>
                  <th className="px-4 py-3">Minimum</th>
                  <th className="px-4 py-3">Status</th>
                  {canManage && <th className="px-4 py-3">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((item) => (
                  <tr key={item.productId} className="hover:bg-gray-50">
                    <td className="px-4 py-3">{item.productName}</td>
                    <td className="px-4 py-3">{item.sku}</td>
                    <td className="px-4 py-3">{item.categoryName}</td>
                    <td className="px-4 py-3">{item.brandName}</td>
                    <td className="px-4 py-3">{item.currentStock}</td>
                    <td className="px-4 py-3">{item.minimumStockLevel}</td>
                    <td className="px-4 py-3">
                      <StatusBadge status={item.status} />
                    </td>
                    {canManage && (
                      <td className="px-4 py-3 space-x-3">
                        <button
                          onClick={() => openModal("in", item)}
                          className="text-blue-600 hover:underline"
                        >
                          Stock In
                        </button>
                        <button
                          onClick={() => openModal("out", item)}
                          className="text-red-600 hover:underline"
                        >
                          Stock Out
                        </button>
                        <button
                          onClick={() => openModal("adjust", item)}
                          className="text-gray-600 hover:underline"
                        >
                          Adjust
                        </button>
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

      {modalMode && selectedItem && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-lg shadow-lg p-6 w-full max-w-md">
            <h2 className="text-lg font-bold text-gray-800 mb-1">
              {modalMode === "in" && "Stock In"}
              {modalMode === "out" && "Stock Out"}
              {modalMode === "adjust" && "Adjust Stock"}
            </h2>
            <p className="text-sm text-gray-500 mb-4">
              {selectedItem.productName} — Current stock: {selectedItem.currentStock}
            </p>

            {modalError && (
              <p className="text-red-600 text-sm mb-3" role="alert">
                {modalError}
              </p>
            )}

            <div className="space-y-3">
              {modalMode === "adjust" && (
                <div className="flex gap-4 text-sm">
                  <label className="flex items-center gap-1">
                    <input
                      type="radio"
                      checked={adjustDirection === "increase"}
                      onChange={() => setAdjustDirection("increase")}
                    />
                    Increase
                  </label>
                  <label className="flex items-center gap-1">
                    <input
                      type="radio"
                      checked={adjustDirection === "decrease"}
                      onChange={() => setAdjustDirection("decrease")}
                    />
                    Decrease
                  </label>
                </div>
              )}

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Quantity *
                </label>
                <input
                  type="number"
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-3 py-2"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Reason *
                </label>
                <input
                  type="text"
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-3 py-2"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Notes
                </label>
                <textarea
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-3 py-2"
                  rows={2}
                />
              </div>
            </div>

            <div className="flex gap-3 mt-5">
              <button
                onClick={handleConfirm}
                disabled={isSaving}
                className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 disabled:opacity-50 transition"
              >
                {isSaving ? "Saving..." : "Confirm"}
              </button>
              <button
                onClick={closeModal}
                className="border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50 transition"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}