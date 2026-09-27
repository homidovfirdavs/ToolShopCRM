import { useEffect, useState, type FormEvent } from "react";
import {
  getBrands,
  createBrand,
  updateBrand,
  deactivateBrand,
  type Brand,
} from "../api/catalog";
import { useAuth } from "../context/AuthContext";

export default function BrandsPage() {
  const { user } = useAuth();
  const canManage = user?.roles.includes("Admin") || user?.roles.includes("Manager");

  const [brands, setBrands] = useState<Brand[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [editingId, setEditingId] = useState<number | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  async function load() {
    setIsLoading(true);
    try {
      setBrands(await getBrands());
      setError(null);
    } catch {
      setError("Error loading brands.");
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  function startEdit(b: Brand) {
    setEditingId(b.id);
    setName(b.name);
    setDescription(b.description ?? "");
    setFormError(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setName("");
    setDescription("");
    setFormError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setFormError(null);

    if (!name.trim()) {
      setFormError("Brand name is required.");
      return;
    }

    try {
      if (editingId) {
        await updateBrand(editingId, name.trim(), description.trim(), true);
      } else {
        await createBrand(name.trim(), description.trim());
      }
      cancelEdit();
      load();
    } catch (err) {
      const status =
        typeof err === "object" && err !== null && "response" in err
          ? (err as { response?: { status?: number } }).response?.status
          : undefined;
      setFormError(
        status === 409
          ? "A brand with this name already exists."
          : "Failed to save brand."
      );
    }
  }

  async function handleDeactivate(id: number) {
    if (!confirm("Deactivate this brand?")) return;
    try {
      await deactivateBrand(id);
      load();
    } catch {
      alert("Failed to deactivate brand.");
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-3xl mx-auto">
        <h1 className="text-2xl font-bold text-gray-800 mb-6">Brands</h1>

        {canManage && (
          <form
            onSubmit={handleSubmit}
            className="bg-white rounded-lg shadow p-4 mb-4 flex flex-wrap gap-3 items-start"
          >
            <input
              type="text"
              placeholder="Brand name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="border border-gray-300 rounded-md px-3 py-2 flex-1 min-w-[150px]"
            />
            <input
              type="text"
              placeholder="Description (optional)"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="border border-gray-300 rounded-md px-3 py-2 flex-1 min-w-[150px]"
            />
            <button
              type="submit"
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 transition"
            >
              {editingId ? "Update" : "Add"}
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
            {formError && (
              <p className="text-red-600 text-sm w-full">{formError}</p>
            )}
          </form>
        )}

        <div className="bg-white rounded-lg shadow overflow-hidden">
          {isLoading ? (
            <p className="p-6 text-center text-gray-500">Loading...</p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : brands.length === 0 ? (
            <p className="p-6 text-center text-gray-500">No brands found.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Description</th>
                  <th className="px-4 py-3">Status</th>
                  {canManage && <th className="px-4 py-3">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {brands.map((b) => (
                  <tr key={b.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">{b.name}</td>
                    <td className="px-4 py-3">{b.description ?? "—"}</td>
                    <td className="px-4 py-3">
                      <span
                        className={
                          b.isActive
                            ? "text-green-600 font-medium"
                            : "text-gray-400"
                        }
                      >
                        {b.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    {canManage && (
                      <td className="px-4 py-3 space-x-3">
                        <button
                          onClick={() => startEdit(b)}
                          className="text-blue-600 hover:underline"
                        >
                          Edit
                        </button>
                        {b.isActive && (
                          <button
                            onClick={() => handleDeactivate(b.id)}
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
      </div>
    </div>
  );
}