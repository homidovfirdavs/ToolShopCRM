import { lazy, Suspense, type ReactNode } from "react";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext";
import { I18nProvider } from "./i18n/I18nProvider";
import { ToastProvider } from "./components/ui/ToastProvider";
import ProtectedRoute from "./components/ProtectedRoute";
import AppShell from "./components/layout/AppShell";
import { homePathFor } from "./components/layout/navConfig";
import { usePermissions } from "./auth/usePermissions";
import LoginPage from "./pages/LoginPage";
import { LoadingSkeleton } from "./components/ui/States";

const DashboardPage = lazy(() => import("./pages/DashboardPage"));
const ProductsPage = lazy(() => import("./pages/ProductsPage"));
const ProductFormPage = lazy(() => import("./pages/ProductFormPage"));
const CategoriesPage = lazy(() => import("./pages/CategoriesPage"));
const BrandsPage = lazy(() => import("./pages/BrandsPage"));
const CustomersPage = lazy(() => import("./pages/CustomersPage"));
const CustomerFormPage = lazy(() => import("./pages/CustomerFormPage"));
const CustomerProfilePage = lazy(() => import("./pages/CustomerProfilePage"));
const InventoryPage = lazy(() => import("./pages/InventoryPage"));
const InventoryMovementsPage = lazy(() => import("./pages/InventoryMovementsPage"));
const SuppliersPage = lazy(() => import("./pages/SuppliersPage"));
const SupplierProfilePage = lazy(() => import("./pages/SupplierProfilePage"));
const PurchasesPage = lazy(() => import("./pages/PurchasesPage"));
const PurchaseFormPage = lazy(() => import("./pages/PurchaseFormPage"));
const PurchaseDetailsPage = lazy(() => import("./pages/PurchaseDetailsPage"));
const PosPage = lazy(() => import("./pages/PosPage"));
const OrdersPage = lazy(() => import("./pages/OrdersPage"));
const OrderDetailsPage = lazy(() => import("./pages/OrderDetailsPage"));
const DebtsPage = lazy(() => import("./pages/DebtsPage"));
const CashShiftPage = lazy(() => import("./pages/CashShiftPage"));
const PaymentsPage = lazy(() => import("./pages/PaymentsPage"));
const ExpensesPage = lazy(() => import("./pages/ExpensesPage"));
const ExpenseFormPage = lazy(() => import("./pages/ExpenseFormPage"));
const FinancePage = lazy(() => import("./pages/FinancePage"));
const ReportsPage = lazy(() => import("./pages/ReportsPage"));
const UsersPage = lazy(() => import("./pages/UsersPage"));

function HomeRedirect() {
  const { has } = usePermissions();
  return <Navigate to={homePathFor(has)} replace />;
}

/** UI-only route guard (backend authorization remains authoritative). */
function Guard({ permission, children }: { permission: string; children: ReactNode }) {
  const { has } = usePermissions();
  if (!has(permission)) return <Navigate to={homePathFor(has)} replace />;
  return <>{children}</>;
}

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <I18nProvider>
          <ToastProvider>
            <Suspense fallback={<LoadingSkeleton rows={6} />}>
              <Routes>
                <Route path="/login" element={<LoginPage />} />

                <Route
                  element={
                    <ProtectedRoute>
                      <AppShell />
                    </ProtectedRoute>
                  }
                >
                  <Route path="/dashboard" element={<Guard permission="Finance.Profit.View"><DashboardPage /></Guard>} />
                  <Route path="/products" element={<Guard permission="Products.View"><ProductsPage /></Guard>} />
                  <Route path="/products/new" element={<Guard permission="Products.View"><ProductFormPage /></Guard>} />
                  <Route path="/products/:id/edit" element={<Guard permission="Products.View"><ProductFormPage /></Guard>} />
                  <Route path="/categories" element={<Guard permission="Products.View"><CategoriesPage /></Guard>} />
                  <Route path="/brands" element={<Guard permission="Products.View"><BrandsPage /></Guard>} />
                  <Route path="/customers" element={<Guard permission="Sales.View"><CustomersPage /></Guard>} />
                  <Route path="/customers/new" element={<Guard permission="Sales.View"><CustomerFormPage /></Guard>} />
                  <Route path="/customers/:id/edit" element={<Guard permission="Sales.View"><CustomerFormPage /></Guard>} />
                  <Route path="/customers/:id" element={<Guard permission="Sales.View"><CustomerProfilePage /></Guard>} />
                  <Route path="/inventory" element={<Guard permission="Inventory.View"><InventoryPage /></Guard>} />
                  <Route path="/inventory/movements" element={<Guard permission="Inventory.View"><InventoryMovementsPage /></Guard>} />
                  <Route path="/suppliers" element={<Guard permission="Inventory.View"><SuppliersPage /></Guard>} />
                  <Route path="/suppliers/:id" element={<Guard permission="Inventory.View"><SupplierProfilePage /></Guard>} />
                  <Route path="/purchases" element={<Guard permission="Inventory.View"><PurchasesPage /></Guard>} />
                  <Route path="/purchases/new" element={<Guard permission="Inventory.View"><PurchaseFormPage /></Guard>} />
                  <Route path="/purchases/:id" element={<Guard permission="Inventory.View"><PurchaseDetailsPage /></Guard>} />
                  <Route path="/pos" element={<Guard permission="Sales.Create"><PosPage /></Guard>} />
                  <Route path="/orders" element={<Guard permission="Sales.View"><OrdersPage /></Guard>} />
                  <Route path="/orders/:id" element={<Guard permission="Sales.View"><OrderDetailsPage /></Guard>} />
                  <Route path="/debts" element={<Guard permission="Sales.View"><DebtsPage /></Guard>} />
                  <Route path="/cash-shift" element={<Guard permission="CashShifts.View"><CashShiftPage /></Guard>} />
                  <Route path="/payments" element={<Guard permission="Sales.View"><PaymentsPage /></Guard>} />
                  <Route path="/expenses" element={<Guard permission="Expenses.View"><ExpensesPage /></Guard>} />
                  <Route path="/expenses/new" element={<Guard permission="Expenses.View"><ExpenseFormPage /></Guard>} />
                  <Route path="/expenses/:id/edit" element={<Guard permission="Expenses.View"><ExpenseFormPage /></Guard>} />
                  <Route path="/finance" element={<Guard permission="Finance.Profit.View"><FinancePage /></Guard>} />
                  <Route path="/reports" element={<Guard permission="Reports.View"><ReportsPage /></Guard>} />
                  <Route path="/users" element={<Guard permission="Users.Manage"><UsersPage /></Guard>} />
                  <Route path="/" element={<HomeRedirect />} />
                </Route>

                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </Suspense>
          </ToastProvider>
        </I18nProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
