import { useEffect, useState, type FormEvent } from "react";
import {
  getCategories,
  createCategory,
  updateCategory,
  deactivateCategory,
  type Category,
} from "../api/catalog";
import { useAuth } from "../context/AuthContext";

export default function CategoriesPage() {
  const { user } = useAuth();
  const canManage = user?.roles.includes("Admin") || user?.roles.includes("Manager");

  const [categories, setCategories] = useState<Category[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [editingId, setEditingId] = useState<number | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  async function load() {
    setIsLoading(true);
    try {
      setCategories(await getCategories());
      setError(null);
    } catch {
      setError("Error loading categories.");
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  function startEdit(c: Category) {
    setEditingId(c.id);
    setName(c.name);
    setDescription(c.description ?? "");
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
      setFormError("Category name is required.");
      return;
    }

    try {
      if (editingId) {
        await updateCategory(editingId, name.trim(), description.trim(), true);
      } else {
        await createCategory(name.trim(), description.trim());
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
          ? "A category with this name already exists."
          : "Failed to save category."
      );
    }
  }

  async function handleDeactivate(id: number) {
    if (!confirm("Deactivate this category?")) return;
    try {
      await deactivateCategory(id);
      load();
    } catch {
      alert("Failed to deactivate category.");
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-3xl mx-auto">
        <h1 className="text-2xl font-bold text-gray-800 mb-6">Categories</h1>

        {canManage && (
          <form
            onSubmit={handleSubmit}
            className="bg-white rounded-lg shadow p-4 mb-4 flex flex-wrap gap-3 items-start"
          >
            <input
              type="text"
              placeholder="Category name"
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
          ) : categories.length === 0 ? (
            <p className="p-6 text-center text-gray-500">
              No categories found.
            </p>
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
                {categories.map((c) => (
                  <tr key={c.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">{c.name}</td>
                    <td className="px-4 py-3">{c.description ?? "—"}</td>
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
                    {canManage && (
                      <td className="px-4 py-3 space-x-3">
                        <button
                          onClick={() => startEdit(c)}
                          className="text-blue-600 hover:underline"
                        >
                          Edit
                        </button>
                        {c.isActive && (
                          <button
                            onClick={() => handleDeactivate(c.id)}
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