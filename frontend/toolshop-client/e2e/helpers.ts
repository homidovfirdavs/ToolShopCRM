import { randomUUID } from "node:crypto";
import { expect, request, type APIRequestContext, type Page } from "@playwright/test";
import { translations } from "../src/i18n/translations";
import { formatMoney } from "../src/lib/format";

export { formatMoney };

export const API_URL = process.env.E2E_API_URL ?? "http://localhost:5252";

/** English UI text for a translation key (tests run with the English UI). */
export const T = (key: string): string => translations.en[key] ?? key;

function credentials() {
  const username = process.env.E2E_ADMIN_USER;
  const password = process.env.E2E_ADMIN_PASSWORD;
  if (!username || !password) {
    throw new Error("E2E_ADMIN_USER / E2E_ADMIN_PASSWORD are not set. Start the tests with backend/scripts/Run-E2E.ps1.");
  }
  return { username, password };
}

export const adminUser = () => credentials().username;
export const adminPassword = () => credentials().password;

export const uid = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 7);

interface LoginResult {
  accessToken: string;
  refreshToken: string;
}

/** Thin authenticated API client used to arrange and verify data (never to bypass what the test is checking). */
export class Api {
  private constructor(
    private readonly ctx: APIRequestContext,
    private readonly token: string,
  ) {}

  static async login(): Promise<{ api: Api; refreshToken: string }> {
    const { username, password } = credentials();
    const ctx = await request.newContext({ baseURL: API_URL });
    const res = await ctx.post("/api/auth/login", { data: { usernameOrEmail: username, password } });
    expect(res.ok(), `E2E admin login failed (HTTP ${res.status()}). Is the backend running with the E2E admin?`).toBeTruthy();
    const body = (await res.json()) as LoginResult;
    return { api: new Api(ctx, body.accessToken), refreshToken: body.refreshToken };
  }

  private headers() {
    return { Authorization: `Bearer ${this.token}` };
  }

  async get<R = any>(path: string): Promise<R> { // eslint-disable-line @typescript-eslint/no-explicit-any
    const res = await this.ctx.get(path, { headers: this.headers() });
    expect(res.ok(), `GET ${path} -> ${res.status()}`).toBeTruthy();
    return (await res.json()) as R;
  }

  async post<R = any>(path: string, data?: unknown, expectOk = true): Promise<R> { // eslint-disable-line @typescript-eslint/no-explicit-any
    // Sales, returns and payments need an Idempotency-Key (one per intention). Every call here is a new intention.
    const needsKey = /\/(complete|return)$|\/payments$/.test(path);
    const headers = needsKey ? { ...this.headers(), "Idempotency-Key": randomUUID() } : this.headers();
    const res = await this.ctx.post(path, { headers, data });
    if (expectOk) expect(res.ok(), `POST ${path} -> ${res.status()} ${await res.text()}`).toBeTruthy();
    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as R;
  }

  async dispose() {
    await this.ctx.dispose();
  }

  // ---------- arrange helpers (unique names so tests never collide with existing data) ----------

  async seedProduct(opts: { price?: number; cost?: number; stock?: number } = {}) {
    const id = uid();
    const category = await this.post("/api/categories", { name: `E2E Cat ${id}` });
    const brand = await this.post("/api/brands", { name: `E2E Brand ${id}` });
    const price = opts.price ?? 100;
    const product = await this.post("/api/products", {
      name: `E2E Product ${id}`,
      sku: `E2E-${id}`,
      categoryId: category.id,
      brandId: brand.id,
      purchasePrice: opts.cost ?? 60,
      salePrice: price,
      minimumStockLevel: 0,
      warrantyMonths: 0,
    });
    const stock = opts.stock ?? 10;
    if (stock > 0) await this.post("/api/inventory/stock-in", { productId: product.id, quantity: stock, reason: "e2e seed" });
    return { id: product.id as number, name: `E2E Product ${id}`, sku: `E2E-${id}`, price };
  }

  async seedCustomer() {
    const id = uid();
    const phone = "9" + Math.floor(Math.random() * 1e9).toString().padStart(9, "0");
    const c = await this.post("/api/customers", { name: `E2E Customer ${id}`, phone });
    return { id: c.id as number, name: `E2E Customer ${id}` };
  }

  /** Completed order (stock is deducted by the backend). `paid` is paid atomically at completion. */
  async sell(productId: number, quantity: number, customerId: number | null, paid = 0) {
    const order = await this.post("/api/orders", {
      customerId,
      notes: "",
      discountAmount: 0,
      items: [{ productId, quantity, discountAmount: 0 }],
    });
    const completed = await this.post(
      `/api/orders/${order.id}/complete`,
      paid > 0 ? { initialPayment: { amount: paid, method: "Cash", referenceNumber: "", notes: "" } } : undefined,
    );
    return { orderId: order.id as number, total: completed.totalAmount as number };
  }

  async stockOf(productId: number): Promise<number> {
    const s = await this.get(`/api/inventory/stock/${productId}`);
    return s.currentStock as number;
  }

  async debtOf(customerName: string): Promise<number> {
    const d = await this.get(`/api/debts?search=${encodeURIComponent(customerName)}&pageSize=20`);
    const row = (d.items as { customerName: string; totalDebt: number }[]).find((i) => i.customerName === customerName);
    return row?.totalDebt ?? 0;
  }
}

/** Opens a page that is already signed in (refresh token injected before the app starts) with the English UI. */
export async function signedInPage(page: Page, refreshToken: string) {
  await page.addInitScript(
    ([token]) => {
      // Refresh tokens are single-use: after the first load the app stores a rotated token, which must survive
      // later navigations/reloads in the same tab, so only seed the token when none is stored yet.
      if (!sessionStorage.getItem("toolshop_refresh_token")) sessionStorage.setItem("toolshop_refresh_token", token);
      localStorage.setItem("toolshop_lang", "en");
    },
    [refreshToken],
  );
}
