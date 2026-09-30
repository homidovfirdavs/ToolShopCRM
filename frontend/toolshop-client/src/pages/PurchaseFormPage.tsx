import { useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { createPurchase, type PurchaseItemInput } from "../api/purchases";
import { getSuppliers, type SupplierListItem } from "../api/suppliers";
import { getProducts, type ProductListItem } from "../api/catalog";

interface DraftItem extends PurchaseItemInput {
  productName: string;
  sku: string;
}

export default function PurchaseFormPage() {
  const navigate = useNavigate();

  const [suppliers, setSuppliers] = useState<SupplierListItem[]>([]);
  const [supplierId, setSupplierId] = useState("");
  const [purchaseDate, setPurchaseDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [notes, setNotes] = useState("");
  const [discountAmount, setDiscountAmount] = useState("0");

  const [items, setItems] = useState<DraftItem[]>([]);

  const [productSearch, setProductSearch] = useState("");
  const [productResults, setProductResults] = useState<ProductListItem[]>([]);
  const [quantity, setQuantity] = useState("1");
  const [unitCost, setUnitCost] = useState("");
  const [selectedProduct, setSelectedProduct] = useState<ProductListItem | null>(null);

  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    getSuppliers({ isActive: true }).then((r) => setSuppliers(r.items)).catch(() => {});
  }, []);

  useEffect(() => {
    if (!productSearch.trim()) {
      setProductResults([]);
      return;
    }
    const timeout = setTimeout(() => {
      getProducts({ search: productSearch, isActive: true, pageSize: 10 })
        .then((r) => setProductResults(r.items))
        .catch(() => {});
    }, 300);
    return () => clearTimeout(timeout);
  }, [productSearch]);

  function selectProduct(p: ProductListItem) {
    setSelectedProduct(p);
    setProductSearch(p.name);
    setProductResults([]);
    setUnitCost(String(p.purchasePrice));
  }

  function addItem() {
    if (!selectedProduct) {
      setError("Please select a product first.");
      return;
    }
    const qty = Number(quantity);
    const cost = Number(unitCost);
    if (!qty || qty <= 0) {
      setError("Quantity must be greater than zero.");
      return;
    }
    if (cost < 0) {
      setError("Unit cost must be 0 or more.");
      return;
    }

    setError(null);

    setItems((prev) => {
      const existingIndex = prev.findIndex((i) => i.productId === selectedProduct.id);
      if (existingIndex >= 0) {
        const updated = [...prev];
        updated[existingIndex] = {
          ...updated[existingIndex],
          quantity: updated[existingIndex].quantity + qty,
        };
        return updated;
      }
      return [
        ...prev,
        {
          productId: selectedProduct.id,
          productName: selectedProduct.name,
          sku: selectedProduct.sku,
          quantity: qty,
          unitCost: cost,
        },
      ];
    });

    setSelectedProduct(null);
    setProductSearch("");
    setQuantity("1");
    setUnitCost("");
  }

  function removeItem(productId: number) {
    setItems((prev) => prev.filter((i) => i.productId !== productId));
  }

  const subtotal = items.reduce((sum, i) => sum + i.quantity * i.unitCost, 0);
  const discount = Number(discountAmount) || 0;
  const total = Math.max(0, subtotal - discount);

  async function handleSaveDraft(e: FormEvent) {
    e.preventDefault();
    setError(null);

    if (!supplierId) {
      setError("Please select a supplier.");
      return;
    }
    if (items.length === 0) {
      setError("Please add at least one product.");
      return;
    }
    if (discount > subtotal) {
      setError("Discount cannot exceed subtotal.");
      return;
    }

    setIsSaving(true);
    try {
      await createPurchase({
        supplierId: Number(supplierId),
        purchaseDate: new Date(purchaseDate).toISOString(),
        notes: notes.trim(),
        discountAmount: discount,
        items: items.map((i) => ({
          productId: i.productId,
          quantity: i.quantity,
          unitCost: i.unitCost,
        })),
      });
      navigate("/purchases");
    } catch {
      setError("Failed to save purchase draft.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-3xl mx-auto bg-white rounded-lg shadow p-6">
        <h1 className="text-xl font-bold text-gray-800 mb-6">New Purchase</h1>

        {error && (
          <p className="text-red-600 text-sm mb-4" role="alert">
            {error}
          </p>
        )}

        <form onSubmit={handleSaveDraft} className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Supplier *
              </label>
              <select
                value={supplierId}
                onChange={(e) => setSupplierId(e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              >
                <option value="">Select supplier</option>
                {suppliers.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Purchase Date *
              </label>
              <input
                type="date"
                value={purchaseDate}
                onChange={(e) => setPurchaseDate(e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2"
              />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Notes
            </label>
            <input
              type="text"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2"
            />
          </div>

          <div className="border-t border-gray-100 pt-4">
            <h2 className="text-sm font-semibold text-gray-700 mb-2">Add Product</h2>
            <div className="flex gap-2 items-end relative">
              <div className="flex-1 relative">
                <input
                  type="text"
                  placeholder="Search product by name, SKU, barcode..."
                  value={productSearch}
                  onChange={(e) => {
                    setProductSearch(e.target.value);
                    setSelectedProduct(null);
                  }}
                  className="w-full border border-gray-300 rounded-md px-3 py-2"
                />
                {productResults.length > 0 && (
                  <ul className="absolute z-10 bg-white border border-gray-200 rounded-md w-full mt-1 shadow-lg max-h-48 overflow-auto">
                    {productResults.map((p) => (
                      <li
                        key={p.id}
                        onClick={() => selectProduct(p)}
                        className="px-3 py-2 hover:bg-gray-50 cursor-pointer text-sm"
                      >
                        {p.name} — {p.sku}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <input
                type="number"
                placeholder="Qty"
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
                className="w-20 border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="number"
                step="0.01"
                placeholder="Unit Cost"
                value={unitCost}
                onChange={(e) => setUnitCost(e.target.value)}
                className="w-28 border border-gray-300 rounded-md px-3 py-2"
              />
              <button
                type="button"
                onClick={addItem}
                className="bg-gray-700 text-white px-4 py-2 rounded-md hover:bg-gray-800 transition"
              >
                + Add
              </button>
            </div>
          </div>

          {items.length > 0 && (
            <table className="w-full text-sm border-t border-gray-100 mt-2">
              <thead className="text-left text-gray-500">
                <tr>
                  <th className="py-2">Product</th>
                  <th className="py-2">SKU</th>
                  <th className="py-2">Qty</th>
                  <th className="py-2">Unit Cost</th>
                  <th className="py-2">Line Total</th>
                  <th className="py-2"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((i) => (
                  <tr key={i.productId}>
                    <td className="py-2">{i.productName}</td>
                    <td className="py-2">{i.sku}</td>
                    <td className="py-2">{i.quantity}</td>
                    <td className="py-2">{i.unitCost.toFixed(2)}</td>
                    <td className="py-2">{(i.quantity * i.unitCost).toFixed(2)}</td>
                    <td className="py-2">
                      <button
                        type="button"
                        onClick={() => removeItem(i.productId)}
                        className="text-red-600 hover:underline"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <div className="border-t border-gray-100 pt-4 space-y-1 text-sm">
            <div className="flex justify-between">
              <span className="text-gray-600">Subtotal</span>
              <span>{subtotal.toFixed(2)}</span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-gray-600">Discount</span>
              <input
                type="number"
                step="0.01"
                value={discountAmount}
                onChange={(e) => setDiscountAmount(e.target.value)}
                className="w-28 border border-gray-300 rounded-md px-2 py-1 text-right"
              />
            </div>
            <div className="flex justify-between font-bold text-gray-800 pt-1">
              <span>Total</span>
              <span>{total.toFixed(2)}</span>
            </div>
          </div>

          <div className="flex gap-3 pt-2">
            <button
              type="submit"
              disabled={isSaving}
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 disabled:opacity-50 transition"
            >
              {isSaving ? "Saving..." : "Save Draft"}
            </button>
            <button
              type="button"
              onClick={() => navigate("/purchases")}
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