import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getSupplier, getSupplierPurchases, type Supplier } from "../api/suppliers";

interface RecentPurchase {
  id: number;
  purchaseNumber: string;
  purchaseDate: string;
  status: string;
  totalAmount: number;
}

export default function SupplierProfilePage() {
  const { id } = useParams();
  const [supplier, setSupplier] = useState<Supplier | null>(null);
  const [purchases, setPurchases] = useState<RecentPurchase[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;

    getSupplier(Number(id))
      .then(setSupplier)
      .catch(() => setError("Supplier not found."))
      .finally(() => setIsLoading(false));

    getSupplierPurchases(Number(id))
      .then((r) => setPurchases(r.items))
      .catch(() => {});
  }, [id]);

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-gray-500">Loading...</p>
      </div>
    );
  }

  if (error || !supplier) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-red-600">{error ?? "Supplier not found."}</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-2xl mx-auto bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-start mb-6">
          <h1 className="text-xl font-bold text-gray-800">{supplier.name}</h1>
          <span className={supplier.isActive ? "text-green-600 font-medium" : "text-gray-400"}>
            {supplier.isActive ? "Active" : "Inactive"}
          </span>
        </div>

        <dl className="space-y-3 text-sm">
          <div>
            <dt className="text-gray-500">Contact Person</dt>
            <dd className="text-gray-800">{supplier.contactPerson ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Phone</dt>
            <dd className="text-gray-800">{supplier.phone ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Email</dt>
            <dd className="text-gray-800">{supplier.email ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Address</dt>
            <dd className="text-gray-800">{supplier.address ?? "—"}</dd>
          </div>
        </dl>

        <div className="mt-6 pt-6 border-t border-gray-100">
          <div className="flex justify-between items-center mb-3">
            <h2 className="text-sm font-semibold text-gray-700">Recent Purchases</h2>
            <Link to="/purchases" className="text-blue-600 hover:underline text-sm">
              View All Purchases
            </Link>
          </div>

          {purchases.length === 0 ? (
            <p className="text-gray-500 text-sm">No purchases yet.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-gray-500">
                <tr>
                  <th className="py-1">Purchase #</th>
                  <th className="py-1">Date</th>
                  <th className="py-1">Status</th>
                  <th className="py-1">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {purchases.map((p) => (
                  <tr key={p.id}>
                    <td className="py-1">
                      <Link to={`/purchases/${p.id}`} className="text-blue-600 hover:underline">
                        {p.purchaseNumber}
                      </Link>
                    </td>
                    <td className="py-1">{new Date(p.purchaseDate).toLocaleDateString()}</td>
                    <td className="py-1">{p.status}</td>
                    <td className="py-1">{p.totalAmount.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="mt-6">
          <Link to="/suppliers" className="text-blue-600 hover:underline text-sm">
            Back to list
          </Link>
        </div>
      </div>
    </div>
  );
}