import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getProducts, type ProductListItem } from "../api/catalog";
import { getCustomers, createCustomer, type CustomerListItem } from "../api/customers";
import { createOrder, completeOrder } from "../api/orders";

interface CartLine {
  productId: number;
  productName: string;
  sku: string;
  salePrice: number;
  availableStock: number;
  quantity: number;
}

export default function PosPage() {
  const navigate = useNavigate();

  const [search, setSearch] = useState("");
  const [results, setResults] = useState<ProductListItem[]>([]);
  const [cart, setCart] = useState<CartLine[]>([]);

  const [customerId, setCustomerId] = useState<number | null>(null);
  const [customerName, setCustomerName] = useState("Walk-in Customer");
  const [customerSearch, setCustomerSearch] = useState("");
  const [customerResults, setCustomerResults] = useState<CustomerListItem[]>([]);
  const [showQuickAdd, setShowQuickAdd] = useState(false);
  const [quickName, setQuickName] = useState("");
  const [quickPhone, setQuickPhone] = useState("");

  const [discountAmount, setDiscountAmount] = useState("0");
  const [error, setError] = useState<string | null>(null);
  const [isProcessing, setIsProcessing] = useState(false);
  const [confirming, setConfirming] = useState(false);

  const [successOrder, setSuccessOrder] = useState<{ orderNumber: string; total: number; customer: string } | null>(
    null
  );

  useEffect(() => {
    if (!search.trim()) {
      setResults([]);
      return;
    }
    const timeout = setTimeout(() => {
      getProducts({ search, isActive: true, pageSize: 8 })
        .then((r) => setResults(r.items))
        .catch(() => {});
    }, 250);
    return () => clearTimeout(timeout);
  }, [search]);

  useEffect(() => {
    if (!customerSearch.trim()) {
      setCustomerResults([]);
      return;
    }
    const timeout = setTimeout(() => {
      getCustomers({ search: customerSearch, isActive: true, pageSize: 5 })
        .then((r) => setCustomerResults(r.items))
        .catch(() => {});
    }, 250);
    return () => clearTimeout(timeout);
  }, [customerSearch]);

  function addToCart(p: ProductListItem) {
    if (p.salePrice === undefined) return;
    setCart((prev) => {
      const existing = prev.find((l) => l.productId === p.id);
      if (existing) {
        return prev.map((l) =>
          l.productId === p.id ? { ...l, quantity: l.quantity + 1 } : l
        );
      }
      return [
        ...prev,
        {
          productId: p.id,
          productName: p.name,
          sku: p.sku,
          salePrice: p.salePrice,
          availableStock: 999, // real availability enforced by backend on complete
          quantity: 1,
        },
      ];
    });
    setSearch("");
    setResults([]);
  }

  function updateQuantity(productId: number, quantity: number) {
    if (quantity <= 0) {
      setCart((prev) => prev.filter((l) => l.productId !== productId));
      return;
    }
    setCart((prev) =>
      prev.map((l) => (l.productId === productId ? { ...l, quantity } : l))
    );
  }

  function removeFromCart(productId: number) {
    setCart((prev) => prev.filter((l) => l.productId !== productId));
  }

  function selectCustomer(c: CustomerListItem) {
    setCustomerId(c.id);
    setCustomerName(c.name);
    setCustomerSearch("");
    setCustomerResults([]);
  }

  function clearCustomer() {
    setCustomerId(null);
    setCustomerName("Walk-in Customer");
  }

  async function handleQuickAddCustomer() {
    if (!quickName.trim() || !quickPhone.trim()) {
      setError("Name and phone are required for a new customer.");
      return;
    }
    try {
      const created = await createCustomer({
        name: quickName.trim(),
        phone: quickPhone.trim(),
        address: "",
        notes: "",
      });
      setCustomerId(created.id);
      setCustomerName(created.name);
      setShowQuickAdd(false);
      setQuickName("");
      setQuickPhone("");
      setError(null);
    } catch {
      setError("Failed to create customer.");
    }
  }

  const subtotal = cart.reduce((sum, l) => sum + l.quantity * l.salePrice, 0);
  const discount = Number(discountAmount) || 0;
  const total = Math.max(0, subtotal - discount);

  async function handleCompleteSale() {
    setError(null);

    if (cart.length === 0) {
      setError("Cart is empty. Add at least one product.");
      return;
    }
    if (discount > subtotal) {
      setError("Discount cannot exceed subtotal.");
      return;
    }

    setIsProcessing(true);
    try {
      const order = await createOrder({
        customerId,
        notes: "",
        discountAmount: discount,
        items: cart.map((l) => ({
          productId: l.productId,
          quantity: l.quantity,
          discountAmount: 0,
        })),
      });

      const completed = await completeOrder(order.id);

      setSuccessOrder({
        orderNumber: completed.orderNumber,
        total: completed.totalAmount,
        customer: completed.customerName,
      });
      setCart([]);
      clearCustomer();
      setDiscountAmount("0");
      setConfirming(false);
    } catch (err) {
      const response =
        typeof err === "object" && err !== null && "response" in err
          ? (err as { response?: { data?: { message?: string } } }).response
          : undefined;
      setError(response?.data?.message ?? "Failed to complete sale.");
      setConfirming(false);
    } finally {
      setIsProcessing(false);
    }
  }

  if (successOrder) {
    return (
      <div className="min-h-screen bg-gray-100 flex items-center justify-center p-6">
        <div className="bg-white rounded-lg shadow p-8 text-center max-w-sm w-full">
          <h1 className="text-2xl font-bold text-green-600 mb-4">SALE COMPLETED</h1>
          <p className="text-gray-600 mb-1">Order</p>
          <p className="text-lg font-semibold mb-4">{successOrder.orderNumber}</p>
          <p className="text-gray-600 mb-1">Total</p>
          <p className="text-2xl font-bold mb-4">{successOrder.total.toFixed(2)}</p>
          <p className="text-gray-600 mb-1">Customer</p>
          <p className="mb-6">{successOrder.customer}</p>
          <div className="flex gap-3 justify-center">
            <button
              onClick={() => setSuccessOrder(null)}
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 transition"
            >
              New Sale
            </button>
            <button
              onClick={() => navigate("/orders")}
              className="border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50 transition"
            >
              View Orders
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-100 p-4 md:p-6">
      <div className="max-w-6xl mx-auto grid grid-cols-1 md:grid-cols-3 gap-4">
        {/* LEFT / MAIN: Product Search + Cart */}
        <div className="md:col-span-2 space-y-4">
          <div className="bg-white rounded-lg shadow p-4">
            <input
              type="text"
              placeholder="Search by name, SKU, or scan barcode..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              autoFocus
              className="w-full border border-gray-300 rounded-md px-3 py-2"
            />
            {results.length > 0 && (
              <ul className="mt-2 divide-y divide-gray-100 max-h-64 overflow-auto">
                {results.map((p) => (
                  <li
                    key={p.id}
                    onClick={() => addToCart(p)}
                    className="py-2 flex justify-between items-center cursor-pointer hover:bg-gray-50 px-2 rounded"
                  >
                    <div>
                      <p className="font-medium">{p.name}</p>
                      <p className="text-xs text-gray-500">{p.sku}</p>
                    </div>
                    <span className="text-blue-600 font-medium">
                      {p.salePrice.toFixed(2)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="bg-white rounded-lg shadow overflow-hidden">
            {cart.length === 0 ? (
              <p className="p-6 text-center text-gray-500">Cart is empty.</p>
            ) : (
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-left text-gray-600">
                  <tr>
                    <th className="px-4 py-3">Product</th>
                    <th className="px-4 py-3">Price</th>
                    <th className="px-4 py-3">Qty</th>
                    <th className="px-4 py-3">Line Total</th>
                    <th className="px-4 py-3"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {cart.map((l) => (
                    <tr key={l.productId}>
                      <td className="px-4 py-3">
                        {l.productName}
                        <div className="text-xs text-gray-400">{l.sku}</div>
                      </td>
                      <td className="px-4 py-3">{l.salePrice.toFixed(2)}</td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => updateQuantity(l.productId, l.quantity - 1)}
                            className="w-6 h-6 border rounded hover:bg-gray-50"
                          >
                            −
                          </button>
                          <span className="w-8 text-center">{l.quantity}</span>
                          <button
                            onClick={() => updateQuantity(l.productId, l.quantity + 1)}
                            className="w-6 h-6 border rounded hover:bg-gray-50"
                          >
                            +
                          </button>
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        {(l.quantity * l.salePrice).toFixed(2)}
                      </td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => removeFromCart(l.productId)}
                          className="text-red-600 hover:underline text-xs"
                        >
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {/* RIGHT: Customer + Totals + Complete */}
        <div className="space-y-4">
          <div className="bg-white rounded-lg shadow p-4">
            <h2 className="text-sm font-semibold text-gray-700 mb-2">Customer</h2>
            <p className="text-sm mb-2">
              {customerName}{" "}
              {customerId && (
                <button onClick={clearCustomer} className="text-xs text-red-600 hover:underline ml-2">
                  Clear
                </button>
              )}
            </p>
            {!customerId && (
              <>
                <input
                  type="text"
                  placeholder="Search customer by name/phone..."
                  value={customerSearch}
                  onChange={(e) => setCustomerSearch(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm mb-1"
                />
                {customerResults.length > 0 && (
                  <ul className="divide-y divide-gray-100 mb-2 max-h-32 overflow-auto text-sm">
                    {customerResults.map((c) => (
                      <li
                        key={c.id}
                        onClick={() => selectCustomer(c)}
                        className="py-1 cursor-pointer hover:bg-gray-50"
                      >
                        {c.name} — {c.phone}
                      </li>
                    ))}
                  </ul>
                )}
                {!showQuickAdd ? (
                  <button
                    onClick={() => setShowQuickAdd(true)}
                    className="text-blue-600 text-sm hover:underline"
                  >
                    + New Customer
                  </button>
                ) : (
                  <div className="space-y-2 mt-2">
                    <input
                      type="text"
                      placeholder="Name"
                      value={quickName}
                      onChange={(e) => setQuickName(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
                    />
                    <input
                      type="text"
                      placeholder="Phone"
                      value={quickPhone}
                      onChange={(e) => setQuickPhone(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
                    />
                    <div className="flex gap-2">
                      <button
                        onClick={handleQuickAddCustomer}
                        className="bg-blue-600 text-white text-sm px-3 py-1 rounded-md hover:bg-blue-700"
                      >
                        Save
                      </button>
                      <button
                        onClick={() => setShowQuickAdd(false)}
                        className="border text-sm px-3 py-1 rounded-md hover:bg-gray-50"
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>

          <div className="bg-white rounded-lg shadow p-4 space-y-2 text-sm">
            {error && <p className="text-red-600">{error}</p>}
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
                className="w-24 border border-gray-300 rounded-md px-2 py-1 text-right"
              />
            </div>
            <div className="flex justify-between text-lg font-bold pt-2 border-t border-gray-100">
              <span>Total</span>
              <span>{total.toFixed(2)}</span>
            </div>

            {!confirming ? (
              <button
                onClick={() => setConfirming(true)}
                disabled={cart.length === 0}
                className="w-full bg-green-600 text-white py-2 rounded-md font-semibold hover:bg-green-700 disabled:opacity-50 transition mt-2"
              >
                COMPLETE SALE
              </button>
            ) : (
              <div className="mt-2 space-y-2">
                <p className="text-xs text-gray-600">
                  Items: {cart.length} · Total: {total.toFixed(2)} · Customer: {customerName}
                </p>
                <div className="flex gap-2">
                  <button
                    onClick={handleCompleteSale}
                    disabled={isProcessing}
                    className="flex-1 bg-green-600 text-white py-2 rounded-md hover:bg-green-700 disabled:opacity-50 transition"
                  >
                    {isProcessing ? "Processing..." : "Confirm Sale"}
                  </button>
                  <button
                    onClick={() => setConfirming(false)}
                    className="border px-3 py-2 rounded-md hover:bg-gray-50"
                  >
                    Back
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}