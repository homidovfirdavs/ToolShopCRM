import { useEffect, useState, useCallback } from "react";
import { getMovements, type Movement } from "../api/inventory";

function MovementBadge({ type, quantity }: { type: string; quantity: number }) {
  const isIncrease = type === "StockIn" || type === "AdjustmentIncrease" || type === "Purchase" || type === "SaleReturn";
  return (
    <span className={isIncrease ? "text-green-600 font-medium" : "text-red-600 font-medium"}>
      {isIncrease ? "+" : "-"}
      {quantity}
    </span>
  );
}

export default function InventoryMovementsPage() {
  const [items, setItems] = useState<Movement[]>([]);
  const [movementType, setMovementType] = useState("");
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await getMovements({
        movementType: movementType
          ? (movementType as Movement["movementType"])
          : undefined,
        page,
        pageSize: 20,
      });
      setItems(result.items);
      setTotalPages(result.totalPages);
      setTotalItems(result.totalItems);
    } catch {
      setError("Unable to load movement history.");
    } finally {
      setIsLoading(false);
    }
  }, [movementType, page]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    setPage(1);
  }, [movementType]);

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-6xl mx-auto">
        <h1 className="text-2xl font-bold text-gray-800 mb-6">Movement History</h1>

        <div className="bg-white rounded-lg shadow p-4 mb-4">
          <select
            value={movementType}
            onChange={(e) => setMovementType(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-2"
          >
            <option value="">All Types</option>
            <option value="StockIn">Stock In</option>
            <option value="StockOut">Stock Out</option>
            <option value="AdjustmentIncrease">Adjustment Increase</option>
            <option value="AdjustmentDecrease">Adjustment Decrease</option>
          </select>
        </div>

        <div className="bg-white rounded-lg shadow overflow-hidden">
          {isLoading ? (
            <p className="p-6 text-center text-gray-500">Loading...</p>
          ) : error ? (
            <p className="p-6 text-center text-red-600">{error}</p>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-gray-500">No movements found.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-left text-gray-600">
                <tr>
                  <th className="px-4 py-3">Date</th>
                  <th className="px-4 py-3">Product</th>
                  <th className="px-4 py-3">SKU</th>
                  <th className="px-4 py-3">Type</th>
                  <th className="px-4 py-3">Quantity</th>
                  <th className="px-4 py-3">Reason</th>
                  <th className="px-4 py-3">User</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.map((m) => (
                  <tr key={m.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      {new Date(m.createdAt).toLocaleString()}
                    </td>
                    <td className="px-4 py-3">{m.productName}</td>
                    <td className="px-4 py-3">{m.sku}</td>
                    <td className="px-4 py-3">{m.movementType}</td>
                    <td className="px-4 py-3">
                      <MovementBadge type={m.movementType} quantity={m.quantity} />
                    </td>
                    <td className="px-4 py-3">{m.reason}</td>
                    <td className="px-4 py-3">{m.createdByUsername}</td>
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