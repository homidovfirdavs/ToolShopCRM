import { useAuth } from "../context/AuthContext";

export default function DashboardPage() {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-gray-100 flex flex-col items-center justify-center gap-4">
      <h1 className="text-3xl font-bold text-gray-800">TOOL SHOP CRM</h1>
      <p className="text-lg text-gray-700">Welcome, {user?.fullName}</p>
      <p className="text-gray-500">Role: {user?.roles.join(", ")}</p>
      <button
        onClick={() => logout()}
        className="mt-2 bg-red-600 text-white px-4 py-2 rounded-md hover:bg-red-700 transition"
      >
        Logout
      </button>
    </div>
  );
}