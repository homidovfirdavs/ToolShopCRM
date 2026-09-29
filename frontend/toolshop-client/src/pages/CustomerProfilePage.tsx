import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getCustomer, type Customer } from "../api/customers";

export default function CustomerProfilePage() {
  const { id } = useParams();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;

    getCustomer(Number(id))
      .then(setCustomer)
      .catch(() => setError("Customer not found."))
      .finally(() => setIsLoading(false));
  }, [id]);

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-gray-500">Loading...</p>
      </div>
    );
  }

  if (error || !customer) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-100">
        <p className="text-red-600">{error ?? "Customer not found."}</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-xl mx-auto bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-start mb-6">
          <h1 className="text-xl font-bold text-gray-800">{customer.name}</h1>
          <span
            className={
              customer.isActive
                ? "text-green-600 font-medium"
                : "text-gray-400"
            }
          >
            {customer.isActive ? "Active" : "Inactive"}
          </span>
        </div>

        <dl className="space-y-3 text-sm">
          <div>
            <dt className="text-gray-500">Phone</dt>
            <dd className="text-gray-800">{customer.phone}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Address</dt>
            <dd className="text-gray-800">{customer.address ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Notes</dt>
            <dd className="text-gray-800">{customer.notes ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Created At</dt>
            <dd className="text-gray-800">
              {new Date(customer.createdAt).toLocaleString()}
            </dd>
          </div>
        </dl>

        <div className="mt-6 pt-6 border-t border-gray-100">
          <h2 className="text-sm font-semibold text-gray-700 mb-2">
            Purchase History
          </h2>
          <p className="text-gray-500 text-sm">
            Purchase history will appear after sales are recorded.
          </p>
        </div>

        <div className="mt-6">
          <Link
            to={`/customers/${customer.id}/edit`}
            className="text-blue-600 hover:underline text-sm"
          >
            Edit Customer
          </Link>
          <span className="mx-2 text-gray-300">|</span>
          <Link to="/customers" className="text-blue-600 hover:underline text-sm">
            Back to list
          </Link>
        </div>
      </div>
    </div>
  );
}