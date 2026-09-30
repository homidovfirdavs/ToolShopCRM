import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import { getPurchases, type PurchaseListItem } from "../api/purchases";
import { getSuppliers, type SupplierListItem } from "../api/suppliers";
import { useAuth } from "../context/AuthContext";

function StatusBadge({ status }: { status: string }) {
  const styles: Record<string, string> = {
    Draft: "text-gray-600",
    Confirmed: "text-green-600",
    Cancelled: "text-red-600",
  };
  return <span className={`font-medium ${styles[status] ?? ""}`}>{status}</span>;
}

export default function PurchasesPage() {
  const { user } = useAuth();
  const canCreate =
    user?.roles.includes("Admin") ||
    user?.roles.includes("Manager") ||
    user?.roles.includes("Warehouse");

  const [items, setItems] = useState<PurchaseListItem[]>([]);
  const [suppliers, setSuppliers] = useState<SupplierListItem[]>([]);
  const [search, setSearch] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [status, setStatus] = useState("");

  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await getPurchases({
        search: search || undefined,
        supplierId: supplierId ? Number(supplierId) : undefined,
        status: status || undefined,
        page,
        pageSize: 20,
      });
      setItems(result.items);
      setTotalPages(result.totalPages);
      setTotalItems(result.totalItems);
    } catch {
      setError("Unable to load purchases.");
    } finally {
      setIsLoading(false);
    }
  }, [search, supplierId, status, page]);

  useEffect(() => {
    getSuppliers({}).then((r) => setSuppliers(r.items)).catch(() => {});
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    setPage(1);
  }, [search, supplierId, status]);

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-6xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-2xl font-bold text-gray-800">Purchases</h1>
          {canCreate && (
            <Link
              to="/purchases/new"
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 transition"
            >
              + New Purchase
            </Link>
          )}
        </div>

        <div className="bg-white rounded-lg shadow p-4 mb-4 flex flex-wrap gap-3">
          <input
            type="text"
            placeholder="Search purchase # or supplier..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2 flex-1 min-w-[200px]"
          />
          <select
            value={supplierId}
            onChange={(e) => setSupplierId(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="">All Suppliers</option>
            {suppliers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="">All Statuses</option>
            <option value="Draft">Draft</option>
            <option value="Confirmed">Confirmed</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </div>

        <div className="bg-white rounded-lg shadow overflow-hidden">
          {isLoading ? (
            <p className="p-6 text-center text-gray-500">Loading purchases...</p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-gray-500">No purchases found.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Purchase #</th>
                  <th className="px-4 py-3">Supplier</th>
                  <th className="px-4 py-3">Date</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Total</th>
                  <th className="px-4 py-3">Created By</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((p) => (
                  <tr key={p.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <Link to={`/purchases/${p.id}`} className="text-blue-600 hover:underline">
                        {p.purchaseNumber}
                      </Link>
                    </td>
                    <td className="px-4 py-3">{p.supplierName}</td>
                    <td className="px-4 py-3">{new Date(p.purchaseDate).toLocaleDateString()}</td>
                    <td className="px-4 py-3">
                      <StatusBadge status={p.status} />
                    </td>
                    <td className="px-4 py-3">{p.totalAmount.toFixed(2)}</td>
                    <td className="px-4 py-3">{p.createdByUsername}</td>
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