import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  getSuppliers,
  createSupplier,
  updateSupplier,
  deactivateSupplier,
  type Supplier,
  type SupplierListItem,
} from "../api/suppliers";
import { useAuth } from "../context/AuthContext";

const emptyForm = {
  name: "",
  contactPerson: "",
  phone: "",
  email: "",
  address: "",
  notes: "",
};

export default function SuppliersPage() {
  const { user } = useAuth();
  const canManage =
    user?.roles.includes("Admin") ||
    user?.roles.includes("Manager") ||
    user?.roles.includes("Warehouse");
  const canDeactivate = user?.roles.includes("Admin") || user?.roles.includes("Manager");

  const [items, setItems] = useState<SupplierListItem[]>([]);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("active");
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [form, setForm] = useState(emptyForm);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  async function load() {
    setIsLoading(true);
    setError(null);
    try {
      const result = await getSuppliers({
        search: search || undefined,
        isActive: status === "" ? undefined : status === "active",
      });
      setItems(result.items);
    } catch {
      setError("Unable to load suppliers.");
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, status]);

  function startEdit(s: Supplier | SupplierListItem) {
    setEditingId(s.id);
    setForm({
      name: s.name,
      contactPerson: s.contactPerson ?? "",
      phone: s.phone ?? "",
      email: s.email ?? "",
      address: "address" in s ? (s.address ?? "") : "",
      notes: "notes" in s ? (s.notes ?? "") : "",
    });
    setFormError(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(emptyForm);
    setFormError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setFormError(null);

    if (!form.name.trim()) {
      setFormError("Supplier name is required.");
      return;
    }

    try {
      if (editingId) {
        await updateSupplier(editingId, { ...form, isActive: true });
      } else {
        await createSupplier(form);
      }
      cancelEdit();
      load();
    } catch {
      setFormError("Failed to save supplier.");
    }
  }

  async function handleDeactivate(id: number) {
    if (!confirm("Deactivate this supplier?")) return;
    try {
      await deactivateSupplier(id);
      load();
    } catch {
      alert("Failed to deactivate supplier.");
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-6xl mx-auto">
        <h1 className="text-2xl font-bold text-gray-800 mb-6">Suppliers</h1>

        {canManage && (
          <form
            onSubmit={handleSubmit}
            className="bg-white rounded-lg shadow p-4 mb-4 space-y-3"
          >
            <div className="grid grid-cols-2 gap-3">
              <input
                type="text"
                placeholder="Supplier name *"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="Contact person"
                value={form.contactPerson}
                onChange={(e) => setForm({ ...form, contactPerson: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="Phone"
                value={form.phone}
                onChange={(e) => setForm({ ...form, phone: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="Email"
                value={form.email}
                onChange={(e) => setForm({ ...form, email: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              />
            </div>
            {formError && <p className="text-red-600 text-sm">{formError}</p>}
            <div className="flex gap-3">
              <button
                type="submit"
                className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 transition"
              >
                {editingId ? "Update" : "Add Supplier"}
              </button>
              {editingId && (
                <button
                  type="button"
                  onClick={cancelEdit}
                  className="border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50 transition"
                >
                  Cancel
                </button>
              )}
            </div>
          </form>
        )}

        <div className="bg-white rounded-lg shadow p-4 mb-4 flex flex-wrap gap-3">
          <input
            type="text"
            placeholder="Search suppliers..."
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
            <p className="p-6 text-center text-gray-500">Loading suppliers...</p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-gray-500">No suppliers found.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Supplier</th>
                  <th className="px-4 py-3">Contact Person</th>
                  <th className="px-4 py-3">Phone</th>
                  <th className="px-4 py-3">Email</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((s) => (
                  <tr key={s.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <Link to={`/suppliers/${s.id}`} className="text-blue-600 hover:underline">
                        {s.name}
                      </Link>
                    </td>
                    <td className="px-4 py-3">{s.contactPerson ?? "—"}</td>
                    <td className="px-4 py-3">{s.phone ?? "—"}</td>
                    <td className="px-4 py-3">{s.email ?? "—"}</td>
                    <td className="px-4 py-3">
                      <span className={s.isActive ? "text-green-600 font-medium" : "text-gray-400"}>
                        {s.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td className="px-4 py-3 space-x-3">
                      {canManage && (
                        <button
                          onClick={() => startEdit(s)}
                          className="text-blue-600 hover:underline"
                        >
                          Edit
                        </button>
                      )}
                      {canDeactivate && s.isActive && (
                        <button
                          onClick={() => handleDeactivate(s.id)}
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
      </div>
    </div>
  );
}