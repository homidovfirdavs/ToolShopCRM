import type { Page, Route } from "@playwright/test";

/**
 * Mock backend for the PWA / responsive checks. NOT a replacement for the real-backend e2e suite
 * (e2e/): it only gives the UI realistic data so layout, PWA and offline behaviour can be checked
 * without a database. Every request is recorded so tests can assert what was (not) sent.
 */

export type MockRole = "admin" | "cashier";

function b64url(obj: unknown): string {
  return Buffer.from(JSON.stringify(obj)).toString("base64url");
}

/** Unsigned token in JWT format; the UI only reads the "permission" claim from it. */
export function fakeJwt(permissions: string[]): string {
  return `${b64url({ alg: "none", typ: "JWT" })}.${b64url({ permission: permissions, exp: 4102444800 })}.sig`;
}

const CASHIER_PERMISSIONS = ["Products.View", "Sales.View", "Sales.Create"];

export const ADMIN_USER = { id: "u-admin", username: "admin", fullName: "System Administrator", email: "admin@example.test", roles: ["Admin"] };
export const CASHIER_USER = { id: "u-cash", username: "cashier", fullName: "Test Cashier", email: "cashier@example.test", roles: ["Cashier"] };

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, totalItems: items.length, totalPages: 1 });

const categories = ["Power Tools", "Hand Tools", "Fasteners", "Paint", "Plumbing"].map((name, i) => ({
  id: i + 1, name, description: null, isActive: true, productCount: 6 - i, totalProductCount: 6 - i, createdAt: "2026-09-01T08:00:00Z", updatedAt: null,
}));
const brands = ["Makita", "Bosch", "Stanley"].map((name, i) => ({ id: i + 1, name, description: null, isActive: true, createdAt: "2026-09-01T08:00:00Z", updatedAt: null }));

const productNames: [string, string, number, number, string, string][] = [
  ["Makita Rotary Hammer HR2470", "MAK-HR2470", 270, 400, "Power Tools", "Makita"],
  ["Bosch Cordless Drill GSR 12V", "BSH-GSR12", 310, 450, "Power Tools", "Bosch"],
  ["Stanley Claw Hammer 450 g", "STN-HM450", 22, 35, "Hand Tools", "Stanley"],
  ["Adjustable Wrench 250 mm", "WR-ADJ250", 18, 30, "Hand Tools", "Stanley"],
  ["Wood Screws 4x40 (200 pcs)", "FS-WS440", 9, 15, "Fasteners", "Bosch"],
  ["Anchor Bolts M10 (50 pcs)", "FS-AB10", 28, 45, "Fasteners", "Bosch"],
  ["Interior Paint White 10 L", "PT-INT10", 95, 140, "Paint", "Makita"],
  ["PPR Pipe 25 mm (4 m)", "PL-PPR25", 12, 20, "Plumbing", "Stanley"],
  ["Angle Grinder 125 mm", "MAK-AG125", 180, 260, "Power Tools", "Makita"],
  ["Tape Measure 5 m", "STN-TM5", 14, 25, "Hand Tools", "Stanley"],
];
/** EAN-13 with a correct check digit, so the camera scanner test can really decode it. */
function ean13(first12: string): string {
  const sum = [...first12].reduce((acc, ch, i) => acc + Number(ch) * (i % 2 ? 3 : 1), 0);
  return first12 + String((10 - (sum % 10)) % 10);
}
const products = productNames.map(([name, sku, purchasePrice, salePrice, categoryName, brandName], i) => ({
  id: i + 1, name, sku, barcode: ean13(`46001${String(i).padStart(7, "0")}`), categoryName, brandName, purchasePrice, salePrice, isActive: true,
}));

const stock = products.map((p, i) => ({
  productId: p.id, productName: p.name, sku: p.sku, barcode: p.barcode, categoryName: p.categoryName, brandName: p.brandName,
  currentStock: [12, 0, 40, 25, 3, 18, 7, 60, 2, 31][i], minimumStockLevel: 5,
  status: [12, 0, 40, 25, 3, 18, 7, 60, 2, 31][i] === 0 ? "OutOfStock" : [12, 0, 40, 25, 3, 18, 7, 60, 2, 31][i] <= 5 ? "LowStock" : "InStock",
}));

const customers = [
  ["Rustam Karimov", "+992 900 11 22 33"], ["Dilshod Construction LLC", "+992 927 44 55 66"], ["Nigora Saidova", "+992 918 77 88 99"],
].map(([name, phone], i) => ({ id: i + 1, name, phone, address: "Dushanbe", isActive: true, createdAt: "2026-09-02T09:00:00Z" }));

const orders = Array.from({ length: 8 }, (_, i) => ({
  id: i + 1, orderNumber: `ORD-${String(i + 1).padStart(6, "0")}`, customerName: i % 3 === 0 ? "Walk-in customer" : customers[i % 3].name,
  orderDate: `2026-10-0${(i % 9) + 1}T1${i}:30:00Z`, status: i === 6 ? "Returned" : i === 7 ? "Draft" : "Completed", totalAmount: 150 + i * 235, createdByUsername: "admin",
}));

const today = new Date("2026-10-11T00:00:00Z");
const day = (n: number) => new Date(today.getTime() - n * 86400000).toISOString().slice(0, 10);
const trend = Array.from({ length: 14 }, (_, i) => ({ date: day(13 - i), revenue: 800 + ((i * 377) % 1900), grossProfit: 250 + ((i * 131) % 600), hasCompleteCostData: true }));

const financeDaily = trend.map((p) => ({ date: p.date, revenue: p.revenue, cogs: p.revenue - p.grossProfit, grossProfit: p.grossProfit, expenses: 90, netOperatingProfit: p.grossProfit - 90, paymentsReceived: Math.round(p.revenue * 0.8) }));
const financeMonthly = [7, 8, 9, 10].map((m, i) => ({ year: 2026, month: m, revenue: 12000 + i * 2500, cogs: 8000 + i * 1500, grossProfit: 4000 + i * 1000, expenses: 1500, netOperatingProfit: 2500 + i * 1000, paymentsReceived: 11000 + i * 2000, purchaseSpend: 6000 }));

const expenses = [
  ["Rent", 800], ["Utilities", 210], ["Transport", 95], ["Salaries", 1500],
].map(([categoryName, amount], i) => ({ id: i + 1, expenseNumber: `EXP-${String(i + 1).padStart(5, "0")}`, expenseDate: `2026-10-0${i + 1}`, categoryName, description: `${categoryName} (October)`, amount, isVoided: false, createdByUsername: "admin" }));

function json(route: Route, data: unknown, status = 200) {
  return route.fulfill({ status, contentType: "application/json", headers: { "access-control-allow-origin": "*", "access-control-allow-headers": "*", "access-control-allow-methods": "*" }, body: JSON.stringify(data) });
}

export interface ApiLog {
  method: string;
  path: string;
}

export interface MockOptions {
  role?: MockRole;
}

/** Installs the mock API on the page's context. Returns the request log (writes are what matters). */
export async function mockApi(page: Page, opts: MockOptions = {}): Promise<ApiLog[]> {
  const role = opts.role ?? "admin";
  const log: ApiLog[] = [];

  await page.context().route("http://localhost:5252/api/**", async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname.replace(/^\/api/, "");
    const method = req.method();
    if (method === "OPTIONS") return json(route, {}, 204);
    log.push({ method, path });

    const session = () => ({
      accessToken: role === "admin" ? fakeJwt([]) : fakeJwt(CASHIER_PERMISSIONS),
      refreshToken: `refresh-${role}`,
      user: role === "admin" ? ADMIN_USER : CASHIER_USER,
    });

    if (path === "/auth/login" || path === "/auth/refresh") return json(route, session());
    if (path === "/auth/logout") return json(route, {});

    if (method !== "GET") {
      // Writes (sale, payment, stock adjustment, ...) succeed here; tests assert on whether they were sent at all.
      if (path === "/orders") return json(route, { id: 101 });
      if (/^\/orders\/\d+\/complete$/.test(path)) return json(route, { id: 101, orderNumber: "ORD-000101", totalAmount: 400, createdAt: "2026-10-11T10:00:00Z", completedAt: "2026-10-11T10:00:00Z" });
      return json(route, {});
    }

    switch (true) {
      case path === "/categories": return json(route, categories);
      case path === "/brands": return json(route, brands);
      case path === "/products": {
        const q = (url.searchParams.get("search") ?? "").trim().toLowerCase();
        const found = q ? products.filter((p) => p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q) || p.barcode.includes(q)) : products;
        return json(route, { ...paged(found), pageSize: 24 });
      }
      case /^\/inventory\/stock\/\d+$/.test(path): {
        const item = stock.find((s) => s.productId === Number(path.split("/").pop()));
        return item ? json(route, item) : json(route, { message: "Product not found." }, 404);
      }
      case path === "/customers": return json(route, paged(customers));
      case path === "/orders": return json(route, paged(orders));
      case path === "/inventory/stock": return json(route, paged(stock));
      case path === "/inventory/movements": return json(route, paged([]));
      case path === "/payments": return json(route, paged([]));
      case path === "/debts": return json(route, { ...paged([{ customerId: 2, customerName: customers[1].name, phone: customers[1].phone, outstandingOrders: 2, totalDebt: 5200, isActive: true, lastPurchase: "2026-10-08T10:00:00Z" }]), walkInUnresolvedDebt: 0, walkInUnresolvedOrderCount: 0 });
      case path === "/debts/walk-in-exceptions": return json(route, { items: [], totalOutstanding: 0, count: 0 });
      case path === "/expense-categories": return json(route, [{ id: 1, name: "Rent", description: null, isActive: true, createdAt: "2026-09-01T00:00:00Z" }, { id: 2, name: "Utilities", description: null, isActive: true, createdAt: "2026-09-01T00:00:00Z" }]);
      case path === "/expenses": return json(route, paged(expenses));
      case path === "/dashboard/summary": return json(route, { dateFrom: day(29), dateTo: day(0), salesRevenue: 20290, grossProfit: 6120, paymentsReceived: 15090, accountsReceivable: 5200, lowStockCount: 3, operatingExpenses: 2605, hasCompleteCostData: true, outOfStockCount: 1, completedOrdersCount: 36 });
      case path === "/dashboard/sales-trend": return json(route, trend);
      case path === "/dashboard/sales-by-category": return json(route, [{ category: "Power Tools", revenue: 11200, percentage: 55 }, { category: "Hand Tools", revenue: 4300, percentage: 21 }, { category: "Fasteners", revenue: 2900, percentage: 14 }, { category: "Paint", revenue: 1890, percentage: 10 }]);
      case path === "/dashboard/attention": return json(route, [{ type: "debts", label: "Customers with debt", count: 2, amount: 5200, link: "/debts" }, { type: "lowStock", label: "Low stock items", count: 3, amount: null, link: "/inventory" }]);
      case path === "/dashboard/low-stock": return json(route, stock.filter((s) => s.status !== "InStock").map((s) => ({ productId: s.productId, productName: s.productName, sku: s.sku, currentStock: s.currentStock, minimumStockLevel: s.minimumStockLevel, status: s.status })));
      case path === "/dashboard/financial-summary": return json(route, { revenue: 20290, cogs: 14170, grossProfit: 6120, operatingExpenses: 2605, netOperatingProfit: 3515, hasCompleteCostData: true });
      case path === "/finance/summary": return json(route, { revenue: 20290, cogs: 14170, grossProfit: 6120, operatingExpenses: 2605, netOperatingProfit: 3515, paymentsReceived: 15090, accountsReceivable: 5200, purchaseSpend: 9000, completedOrdersCount: 36, averageOrderValue: 563, grossMarginPercent: 30.2, netOperatingMarginPercent: 17.3, unknownCostItemsCount: 0 });
      case path === "/finance/daily": return json(route, financeDaily);
      case path === "/finance/monthly": return json(route, financeMonthly);
      case path === "/finance/expenses-by-category": return json(route, [{ category: "Salaries", amount: 1500, percentage: 57.6 }, { category: "Rent", amount: 800, percentage: 30.7 }, { category: "Utilities", amount: 210, percentage: 8.1 }, { category: "Transport", amount: 95, percentage: 3.6 }]);
      case path === "/finance/payments-by-method": return json(route, [{ method: "Cash", amount: 9000, count: 25 }, { method: "Card", amount: 6090, count: 11 }]);
      case path === "/reports/sales": return json(route, paged(orders.map((o) => ({ orderId: o.id, orderNumber: o.orderNumber, orderDate: o.orderDate, customerName: o.customerName, cashierName: "admin", status: o.status, subtotal: o.totalAmount, discountAmount: 0, totalAmount: o.totalAmount, totalPaid: o.totalAmount, outstandingBalance: 0, paymentStatus: "Paid" }))));
      case path === "/reports/sales/summary": return json(route, { orderCount: 8, subtotal: 9000, discountAmount: 0, totalAmount: 9000, totalPaid: 9000, outstandingBalance: 0, completedOrderCount: 6, completedTotalAmount: 7000, completedTotalPaid: 7000, completedOutstandingBalance: 0 });
      case path === "/reports/inventory": return json(route, paged(stock.map((s, i) => ({ productId: s.productId, productName: s.productName, sku: s.sku, categoryName: s.categoryName, brandName: s.brandName, currentStock: s.currentStock, minimumStockLevel: 5, stockStatus: s.status, purchasePrice: products[i].purchasePrice, salePrice: products[i].salePrice, stockValue: s.currentStock * products[i].purchasePrice }))));
      case path === "/users": return json(route, [{ id: "u-admin", username: "admin", fullName: "System Administrator", email: "admin@example.test", roles: ["Admin"], isActive: true, createdAt: "2026-09-01T00:00:00Z" }]);
      default:
        // Any other list endpoint: an empty page keeps the screen valid (empty state) instead of failing.
        return json(route, paged([]));
    }
  });

  return log;
}
