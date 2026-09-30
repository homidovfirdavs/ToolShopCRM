import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
  getPurchase,
  confirmPurchase,
  cancelPurchase,
  type Purchase,
} from "../api/purchases";
import { useAuth } from "../context/AuthContext";

export default function PurchaseDetailsPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const canConfirm = user?.roles.includes("Admin") || user?.roles.includes("Manager");
  const canCancel =
    user?.roles.includes("Admin") ||
    user?.roles.includes("Manager") ||
    user?.roles.includes("Warehouse");

  const [purchase, setPurchase] = useState<Purchase | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isProcessing, setIsProcessing] = useState(false);

  function load() {
    if (!id) return;
    setIsLoading(true);
    getPurchase(Number(id))
      .then(setPurchase)
      .catch(() => setError("Purchase not found."))
      .finally(() => setIsLoading(false));
  }

  useEffect(load, [id]);

  async function handleConfirm() {
    if (!purchase) return;
    if (!confirm("Confirm this purchase? Stock will be added automatically.")) return;

    setIsProcessing(true);
    setError(null);
    try {
      await confirmPurchase(purchase.id);
      load();
    } catch (err) {
      const response =
        typeof err === "object" && err !== null && "response" in err
          ? (err as { response?: { data?: { message?: string } } }).response
          : undefined;
      setError(response?.data?.message ?? "Failed to confirm purchase.");
    } finally {
      setIsProcessing(false);
    }
  }

  async function handleCancel() {
    if (!purchase) return;
    if (!confirm("Cancel this draft purchase?")) return;

    setIsProcessing(true);
    setError(null);
    try {
      await cancelPurchase(purchase.id);
      load();
    } catch {
      setError("Failed to cancel purchase.");
    } finally {
      setIsProcessing(false);
    }
  }

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-gray-500">Loading...</p>
      </div>
    );
  }

  if (!purchase) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-red-600">{error ?? "Purchase not found."}</p>
      </div>
    );
  }

  const statusColor =
    purchase.status === "Confirmed"
      ? "text-green-600"
      : purchase.status === "Cancelled"
        ? "text-red-600"
        : "text-gray-600";

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-3xl mx-auto bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-start mb-6">
          <div>
            <h1 className="text-xl font-bold text-gray-800">
              {purchase.purchaseNumber}
            </h1>
            <p className="text-sm text-gray-500">{purchase.supplierName}</p>
          </div>
          <span className={`font-medium ${statusColor}`}>{purchase.status}</span>
        </div>

        {error && (
          <p className="text-red-600 text-sm mb-4" role="alert">
            {error}
          </p>
        )}

        <div className="grid grid-cols-2 gap-4 text-sm mb-6">
          <div>
            <dt className="text-gray-500">Purchase Date</dt>
            <dd>{new Date(purchase.purchaseDate).toLocaleDateString()}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Created By</dt>
            <dd>{purchase.createdByUsername}</dd>
          </div>
          {purchase.confirmedByUsername && (
            <>
              <div>
                <dt className="text-gray-500">Confirmed By</dt>
                <dd>{purchase.confirmedByUsername}</dd>
              </div>
              <div>
                <dt className="text-gray-500">Confirmed At</dt>
                <dd>
                  {purchase.confirmedAt
                    ? new Date(purchase.confirmedAt).toLocaleString()
                    : "—"}
                </dd>
              </div>
            </>
          )}
        </div>

        <table className="w-full text-sm mb-4">
          <thead className="text-left text-gray-500 border-b border-gray-100">
            <tr>
              <th className="py-2">Product</th>
              <th className="py-2">SKU</th>
              <th className="py-2">Qty</th>
              <th className="py-2">Unit Cost</th>
              <th className="py-2">Line Total</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {purchase.items.map((i) => (
              <tr key={i.id}>
                <td className="py-2">{i.productName}</td>
                <td className="py-2">{i.sku}</td>
                <td className="py-2">{i.quantity}</td>
                <td className="py-2">{i.unitCost.toFixed(2)}</td>
                <td className="py-2">{i.lineTotal.toFixed(2)}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="border-t border-gray-100 pt-3 space-y-1 text-sm max-w-xs ml-auto">
          <div className="flex justify-between">
            <span className="text-gray-600">Subtotal</span>
            <span>{purchase.subtotal.toFixed(2)}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-600">Discount</span>
            <span>{purchase.discountAmount.toFixed(2)}</span>
          </div>
          <div className="flex justify-between font-bold text-gray-800">
            <span>Total</span>
            <span>{purchase.totalAmount.toFixed(2)}</span>
          </div>
        </div>

        <div className="flex gap-3 mt-6 pt-6 border-t border-gray-100">
          {purchase.status === "Draft" && canConfirm && (
            <button
              onClick={handleConfirm}
              disabled={isProcessing}
              className="bg-green-600 text-white px-4 py-2 rounded-md hover:bg-green-700 disabled:opacity-50 transition"
            >
              Confirm Purchase
            </button>
          )}
          {purchase.status === "Draft" && canCancel && (
            <button
              onClick={handleCancel}
              disabled={isProcessing}
              className="bg-red-600 text-white px-4 py-2 rounded-md hover:bg-red-700 disabled:opacity-50 transition"
            >
              Cancel Purchase
            </button>
          )}
          <button
            onClick={() => navigate("/purchases")}
            className="border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50 transition"
          >
            Back to List
          </button>
        </div>
      </div>
    </div>
  );
}