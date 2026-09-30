import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { getOrder, type Order } from "../api/orders";

export default function OrderDetailsPage() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [order, setOrder] = useState<Order | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    getOrder(Number(id))
      .then(setOrder)
      .catch(() => setError("Order not found."))
      .finally(() => setIsLoading(false));
  }, [id]);

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-gray-500">Loading...</p>
      </div>
    );
  }

  if (!order) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-red-600">{error ?? "Order not found."}</p>
      </div>
    );
  }

  const statusColor =
    order.status === "Completed"
      ? "text-green-600"
      : order.status === "Cancelled"
        ? "text-red-600"
        : "text-gray-600";

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-3xl mx-auto bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-start mb-6">
          <div>
            <h1 className="text-xl font-bold text-gray-800">{order.orderNumber}</h1>
            <p className="text-sm text-gray-500">{order.customerName}</p>
          </div>
          <span className={`font-medium ${statusColor}`}>{order.status}</span>
        </div>

        <div className="grid grid-cols-2 gap-4 text-sm mb-6">
          <div>
            <dt className="text-gray-500">Order Date</dt>
            <dd>{new Date(order.orderDate).toLocaleString()}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Cashier</dt>
            <dd>{order.createdByUsername}</dd>
          </div>
          {order.completedByUsername && (
            <>
              <div>
                <dt className="text-gray-500">Completed By</dt>
                <dd>{order.completedByUsername}</dd>
              </div>
              <div>
                <dt className="text-gray-500">Completed At</dt>
                <dd>{order.completedAt ? new Date(order.completedAt).toLocaleString() : "—"}</dd>
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
              <th className="py-2">Unit Price</th>
              <th className="py-2">Discount</th>
              <th className="py-2">Line Total</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {order.items.map((i) => (
              <tr key={i.id}>
                <td className="py-2">{i.productName}</td>
                <td className="py-2">{i.sku}</td>
                <td className="py-2">{i.quantity}</td>
                <td className="py-2">{i.unitPrice.toFixed(2)}</td>
                <td className="py-2">{i.discountAmount.toFixed(2)}</td>
                <td className="py-2">{i.lineTotal.toFixed(2)}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="border-t border-gray-100 pt-3 space-y-1 text-sm max-w-xs ml-auto">
          <div className="flex justify-between">
            <span className="text-gray-600">Subtotal</span>
            <span>{order.subtotal.toFixed(2)}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-600">Discount</span>
            <span>{order.discountAmount.toFixed(2)}</span>
          </div>
          <div className="flex justify-between font-bold text-gray-800">
            <span>Total</span>
            <span>{order.totalAmount.toFixed(2)}</span>
          </div>
        </div>

        <div className="flex gap-3 mt-6 pt-6 border-t border-gray-100">
          <button
            onClick={() => navigate("/orders")}
            className="border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50 transition"
          >
            Back to List
          </button>
        </div>
      </div>
    </div>
  );
}