import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import {
  getCustomers,
  deactivateCustomer,
  type CustomerListItem,
} from "../api/customers";
import { useAuth } from "../context/AuthContext";

export default function CustomersPage() {
  const { user } = useAuth();
  const canDeactivate =
    user?.roles.includes("Admin") || user?.roles.includes("Manager");
  const canCreate =
    canDeactivate || user?.roles.includes("Cashier");

  const [items, setItems] = useState<CustomerListItem[]>([]);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("active");

  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);

  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadCustomers = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await getCustomers({
        search: search || undefined,
        isActive: status === "" ? undefined : status === "active",
        page,
        pageSize: 20,
      });
      setItems(result.items);
      setTotalPages(result.totalPages);
      setTotalItems(result.totalItems);
    } catch {
      setError("Unable to load customers.");
    } finally {
      setIsLoading(false);
    }
  }, [search, status, page]);

  useEffect(() => {
    loadCustomers();
  }, [loadCustomers]);

  useEffect(() => {
    setPage(1);
  }, [search, status]);

  async function handleDeactivate(id: number) {
    if (!confirm("Deactivate this customer?")) return;
    try {
      await deactivateCustomer(id);
      loadCustomers();
    } catch {
      alert("Failed to deactivate customer.");
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-6xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-2xl font-bold text-gray-800">Customers</h1>
          {canCreate && (
            <Link
              to="/customers/new"
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 transition"
            >
              + Add Customer
            </Link>
          )}
        </div>

        <div className="bg-white rounded-lg shadow p-4 mb-4 flex flex-wrap gap-3">
          <input
            type="text"
            placeholder="Search customers..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2 flex-1 min-w-[200px]"
          />
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
            <option value="">All</option>
          </select>
        </div>

        <div className="bg-white rounded-lg shadow overflow-hidden">
          {isLoading ? (
            <p className="p-6 text-center text-gray-500">
              Loading customers...
            </p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-gray-500">
              No customers found.
            </p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Phone</th>
                  <th className="px-4 py-3">Address</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Created At</th>
                  <th className="px-4 py-3">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((c) => (
                  <tr key={c.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <Link
                        to={`/customers/${c.id}`}
                        className="text-blue-600 hover:underline"
                      >
                        {c.name}
                      </Link>
                    </td>
                    <td className="px-4 py-3">{c.phone}</td>
                    <td className="px-4 py-3">{c.address ?? "—"}</td>
                    <td className="px-4 py-3">
                      <span
                        className={
                          c.isActive
                            ? "text-green-600 font-medium"
                            : "text-gray-400"
                        }
                      >
                        {c.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {new Date(c.createdAt).toLocaleDateString()}
                    </td>
                    <td className="px-4 py-3 space-x-3">
                      {canCreate && (
                        <Link
                          to={`/customers/${c.id}/edit`}
                          className="text-blue-600 hover:underline"
                        >
                          Edit
                        </Link>
                      )}
                      {canDeactivate && c.isActive && (
                        <button
                          onClick={() => handleDeactivate(c.id)}
                          className="text-red-600 hover:underline"
                        >
                          Deactivate
                        </button>
                      )}
                    </td>
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