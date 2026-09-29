import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

export default function DashboardPage() {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-gray-100 p-6">
      <div className="max-w-3xl mx-auto text-center">
        <h1 className="text-3xl font-bold text-gray-800 mb-2">TOOL SHOP CRM</h1>
        <p className="text-lg text-gray-700">Welcome, {user?.fullName}</p>
        <p className="text-gray-500 mb-6">Role: {user?.roles.join(", ")}</p>

        <div className="flex justify-center gap-4 mb-6">
          <Link
            to="/products"
            className="bg-white shadow rounded-lg px-6 py-4 hover:shadow-md transition text-gray-800 font-medium"
          >
            Products
          </Link>
          <Link
            to="/categories"
            className="bg-white shadow rounded-lg px-6 py-4 hover:shadow-md transition text-gray-800 font-medium"
          >
            Categories
          </Link>
                    <Link
            to="/brands"
            className="bg-white shadow rounded-lg px-6 py-4 hover:shadow-md transition text-gray-800 font-medium"
          >
            Brands
          </Link>
          <Link
            to="/customers"
            className="bg-white shadow rounded-lg px-6 py-4 hover:shadow-md transition text-gray-800 font-medium"
          >
            Customers
          </Link>
        </div>

        <button
          onClick={() => logout()}
          className="bg-red-600 text-white px-4 py-2 rounded-md hover:bg-red-700 transition"
        >
          Logout
        </button>
      </div>
    </div>
  );
}