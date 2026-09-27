import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  getCategories,
  getBrands,
  getProduct,
  createProduct,
  updateProduct,
  type Category,
  type Brand,
} from "../api/catalog";

const emptyForm = {
  name: "",
  sku: "",
  barcode: "",
  categoryId: "",
  brandId: "",
  purchasePrice: "",
  salePrice: "",
  minimumStockLevel: "0",
  warrantyMonths: "0",
  description: "",
  imageUrl: "",
  isActive: true,
};

export default function ProductFormPage() {
  const { id } = useParams();
  const isEdit = Boolean(id);
  const navigate = useNavigate();

  const [categories, setCategories] = useState<Category[]>([]);
  const [brands, setBrands] = useState<Brand[]>([]);
  const [form, setForm] = useState(emptyForm);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isLoading, setIsLoading] = useState(isEdit);
  const [isSaving, setIsSaving] = useState(false);
  const [generalError, setGeneralError] = useState<string | null>(null);

  useEffect(() => {
    getCategories().then(setCategories).catch(() => {});
    getBrands().then(setBrands).catch(() => {});
  }, []);

  useEffect(() => {
    if (!isEdit || !id) return;

    getProduct(Number(id))
      .then((p) => {
        setForm({
          name: p.name,
          sku: p.sku,
          barcode: p.barcode ?? "",
          categoryId: String(p.categoryId),
          brandId: String(p.brandId),
          purchasePrice: String(p.purchasePrice),
          salePrice: String(p.salePrice),
          minimumStockLevel: String(p.minimumStockLevel),
          warrantyMonths: String(p.warrantyMonths),
          description: p.description ?? "",
          imageUrl: p.imageUrl ?? "",
          isActive: p.isActive,
        });
      })
      .catch(() => setGeneralError("Failed to load product."))
      .finally(() => setIsLoading(false));
  }, [id, isEdit]);

  function validate(): boolean {
    const newErrors: Record<string, string> = {};

    if (!form.name.trim()) newErrors.name = "Product name is required.";
    if (!form.sku.trim()) newErrors.sku = "SKU is required.";
    if (!form.categoryId) newErrors.categoryId = "Category is required.";
    if (!form.brandId) newErrors.brandId = "Brand is required.";
    if (form.purchasePrice === "" || Number(form.purchasePrice) < 0)
      newErrors.purchasePrice = "Purchase price must be 0 or more.";
    if (form.salePrice === "" || Number(form.salePrice) < 0)
      newErrors.salePrice = "Sale price must be 0 or more.";

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setGeneralError(null);

    if (!validate()) return;

    setIsSaving(true);

    const payload = {
      name: form.name.trim(),
      sku: form.sku.trim(),
      barcode: form.barcode.trim(),
      categoryId: Number(form.categoryId),
      brandId: Number(form.brandId),
      purchasePrice: Number(form.purchasePrice),
      salePrice: Number(form.salePrice),
      minimumStockLevel: Number(form.minimumStockLevel || 0),
      warrantyMonths: Number(form.warrantyMonths || 0),
      description: form.description.trim(),
      imageUrl: form.imageUrl.trim(),
      isActive: form.isActive,
    };

    try {
      if (isEdit && id) {
        await updateProduct(Number(id), payload);
      } else {
        await createProduct(payload);
      }
      navigate("/products");
    } catch (err) {
      const status =
        typeof err === "object" && err !== null && "response" in err
          ? (err as { response?: { status?: number; data?: { message?: string } } })
              .response
          : undefined;

      if (status?.status === 409) {
        setGeneralError(status.data?.message ?? "A product with these details already exists.");
      } else if (status?.status === 400) {
        setGeneralError(status.data?.message ?? "Please check the form for errors.");
      } else {
        setGeneralError("Something went wrong. Please try again.");
      }
    } finally {
      setIsSaving(false);
    }
  }

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-gray-500">Loading...</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-2xl mx-auto bg-white rounded-lg shadow p-6">
        <h1 className="text-xl font-bold text-gray-800 mb-6">
          {isEdit ? "Edit Product" : "New Product"}
        </h1>

        {generalError && (
          <p className="text-red-600 text-sm mb-4" role="alert">
            {generalError}
          </p>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Product Name *
            </label>
            <input
              type="text"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2"
            />
            {errors.name && (
              <p className="text-red-600 text-xs mt-1">{errors.name}</p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                SKU *
              </label>
              <input
                type="text"
                value={form.sku}
                onChange={(e) => setForm({ ...form, sku: e.target.value })}
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
              {errors.sku && (
                <p className="text-red-600 text-xs mt-1">{errors.sku}</p>
              )}
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Barcode
              </label>
              <input
                type="text"
                value={form.barcode}
                onChange={(e) => setForm({ ...form, barcode: e.target.value })}
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Category *
              </label>
              <select
                value={form.categoryId}
                onChange={(e) =>
                  setForm({ ...form, categoryId: e.target.value })
                }
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              >
                <option value="">Select category</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
              {errors.categoryId && (
                <p className="text-red-600 text-xs mt-1">{errors.categoryId}</p>
              )}
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Brand *
              </label>
              <select
                value={form.brandId}
                onChange={(e) => setForm({ ...form, brandId: e.target.value })}
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              >
                <option value="">Select brand</option>
                {brands.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
              {errors.brandId && (
                <p className="text-red-600 text-xs mt-1">{errors.brandId}</p>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Purchase Price *
              </label>
              <input
                type="number"
                step="0.01"
                value={form.purchasePrice}
                onChange={(e) =>
                  setForm({ ...form, purchasePrice: e.target.value })
                }
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
              {errors.purchasePrice && (
                <p className="text-red-600 text-xs mt-1">
                  {errors.purchasePrice}
                </p>
              )}
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Sale Price *
              </label>
              <input
                type="number"
                step="0.01"
                value={form.salePrice}
                onChange={(e) =>
                  setForm({ ...form, salePrice: e.target.value })
                }
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
              {errors.salePrice && (
                <p className="text-red-600 text-xs mt-1">{errors.salePrice}</p>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Minimum Stock Level
              </label>
              <input
                type="number"
                value={form.minimumStockLevel}
                onChange={(e) =>
                  setForm({ ...form, minimumStockLevel: e.target.value })
                }
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Warranty (Months)
              </label>
              <input
                type="number"
                value={form.warrantyMonths}
                onChange={(e) =>
                  setForm({ ...form, warrantyMonths: e.target.value })
                }
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Description
            </label>
            <textarea
              value={form.description}
              onChange={(e) =>
                setForm({ ...form, description: e.target.value })
              }
              className="w-full border border-gray-300 rounded-md px-3 py-2"
              rows={3}
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Image URL
            </label>
            <input
              type="text"
              value={form.imageUrl}
              onChange={(e) => setForm({ ...form, imageUrl: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2"
            />
          </div>

          {isEdit && (
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) =>
                  setForm({ ...form, isActive: e.target.checked })
                }
              />
              Active
            </label>
          )}

          <div className="flex gap-3 pt-2">
            <button
              type="submit"
              disabled={isSaving}
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 disabled:opacity-50 transition"
            >
              {isSaving ? "Saving..." : "Save"}
            </button>
            <button
              type="button"
              onClick={() => navigate("/products")}
              className="border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50 transition"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}