import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from "react";
import { Link } from "react-router-dom";
import { getCategories, getProducts, type ProductListItem } from "../api/catalog";
import { getCustomers, createCustomer, type CustomerListItem } from "../api/customers";
import { createOrder, completeOrder, cancelOrder } from "../api/orders";
import type { PaymentMethod } from "../api/payments";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorCode, errorMessage, errorShortages } from "../lib/apiError";
import { formatDateTime, formatMoney, formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import ConfirmDialog from "../components/ui/ConfirmDialog";
import FormField from "../components/ui/FormField";
import SearchInput from "../components/ui/SearchInput";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { useOnline } from "../pwa/useOnline";
import { inputClass } from "../components/ui/formStyles";
import CategoryPanel from "../components/pos/CategoryPanel";
import { useCategoryNames } from "../lib/categoryNames";

interface CartLine {
  productId: number;
  productName: string;
  sku: string;
  salePrice: number;
  quantity: number;
}

interface HeldSale {
  id: string;
  savedAt: string;
  cart: CartLine[];
  customerId: number | null;
  customerName: string;
  discount: string;
}

interface Receipt {
  orderNumber: string;
  date: string;
  customer: string;
  lines: CartLine[];
  subtotal: number;
  discount: number;
  total: number;
  paid: number;
  received: number | null;
  change: number | null;
  debt: number;
  method: PaymentMethod | null;
}

type PayOption = "full" | "partial" | "later";

const HELD_KEY = "toolshop_pos_held";
const METHODS: PaymentMethod[] = ["Cash", "Card", "BankTransfer", "QR"];

function loadHeld(): HeldSale[] {
  try {
    const raw = localStorage.getItem(HELD_KEY);
    return raw ? (JSON.parse(raw) as HeldSale[]) : [];
  } catch {
    return [];
  }
}

function saveHeld(list: HeldSale[]) {
  try {
    localStorage.setItem(HELD_KEY, JSON.stringify(list));
  } catch {
    /* storage unavailable — hold is a convenience only */
  }
}

export default function PosPage() {
  const { t } = useI18n();
  const toast = useToast();
  const online = useOnline();
  const catNames = useCategoryNames();

  // ---------- product browsing ----------
  const [searchText, setSearchText] = useState("");
  const [query, setQuery] = useState("");
  const [categoryId, setCategoryId] = useState<number | null>(null);

  useEffect(() => {
    const id = window.setTimeout(() => setQuery(searchText.trim()), 250);
    return () => window.clearTimeout(id);
  }, [searchText]);

  const categories = useAsyncData(() => getCategories(), "pos-categories");
  const products = useAsyncData(
    () =>
      getProducts({
        search: query || undefined,
        categoryId: categoryId ?? undefined,
        isActive: true,
        pageSize: 24,
      }),
    `${query}|${categoryId ?? ""}`,
  );

  // ---------- cart ----------
  const [cart, setCart] = useState<CartLine[]>([]);
  const [discountAmount, setDiscountAmount] = useState("0");

  function addToCart(p: ProductListItem) {
    setCart((prev) => {
      const existing = prev.find((l) => l.productId === p.id);
      if (existing) return prev.map((l) => (l.productId === p.id ? { ...l, quantity: l.quantity + 1 } : l));
      return [...prev, { productId: p.id, productName: p.name, sku: p.sku, salePrice: p.salePrice, quantity: 1 }];
    });
  }

  function setQuantity(productId: number, quantity: number) {
    if (!(quantity > 0)) {
      setCart((prev) => prev.filter((l) => l.productId !== productId));
      return;
    }
    setCart((prev) => prev.map((l) => (l.productId === productId ? { ...l, quantity } : l)));
  }

  async function onSearchKey(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key !== "Enter") return;
    const code = searchText.trim();
    if (!code) return;
    try {
      const r = await getProducts({ search: code, isActive: true, pageSize: 5 });
      const lower = code.toLowerCase();
      const exact = r.items.find((p) => p.barcode?.toLowerCase() === lower || p.sku.toLowerCase() === lower);
      const match = exact ?? (r.items.length === 1 ? r.items[0] : undefined);
      if (match) {
        addToCart(match);
        setSearchText("");
      } else {
        toast.error(t("pos.noProducts"));
      }
    } catch {
      toast.error(t("common.loadFailed"));
    }
  }

  // ---------- customer ----------
  const [customerId, setCustomerId] = useState<number | null>(null);
  const [customerName, setCustomerName] = useState("");
  const [customerSearch, setCustomerSearch] = useState("");
  const [customerResults, setCustomerResults] = useState<CustomerListItem[]>([]);
  const customerTimer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(customerTimer.current), []);

  function onCustomerSearch(value: string) {
    setCustomerSearch(value);
    window.clearTimeout(customerTimer.current);
    if (!value.trim()) {
      setCustomerResults([]);
      return;
    }
    customerTimer.current = window.setTimeout(() => {
      getCustomers({ search: value, isActive: true, pageSize: 5 })
        .then((r) => setCustomerResults(r.items))
        .catch(() => setCustomerResults([]));
    }, 250);
  }

  function selectCustomer(c: CustomerListItem) {
    setCustomerId(c.id);
    setCustomerName(c.name);
    setCustomerSearch("");
    setCustomerResults([]);
  }

  function clearCustomer() {
    setCustomerId(null);
    setCustomerName("");
    setPayOption("full");
  }

  const [quickOpen, setQuickOpen] = useState(false);
  const [quickName, setQuickName] = useState("");
  const [quickPhone, setQuickPhone] = useState("");
  const [quickError, setQuickError] = useState<string | null>(null);
  const [quickSaving, setQuickSaving] = useState(false);

  async function saveQuickCustomer() {
    if (quickSaving) return;
    if (!quickName.trim() || !quickPhone.trim()) return setQuickError(t("pos.customerRequired"));
    setQuickSaving(true);
    try {
      const created = await createCustomer({ name: quickName.trim(), phone: quickPhone.trim(), address: "", notes: "" });
      setCustomerId(created.id);
      setCustomerName(created.name);
      setQuickOpen(false);
      setQuickName("");
      setQuickPhone("");
      setQuickError(null);
    } catch (err) {
      setQuickError(errorMessage(err) ?? t("pos.customerFailed"));
    } finally {
      setQuickSaving(false);
    }
  }

  // ---------- totals & payment ----------
  const subtotal = cart.reduce((s, l) => s + l.quantity * l.salePrice, 0);
  const discount = Number(discountAmount) || 0;
  const total = Math.max(0, subtotal - discount);

  const [payOption, setPayOption] = useState<PayOption>("full");
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [partialAmount, setPartialAmount] = useState("");
  const [reference, setReference] = useState("");
  const [cashReceived, setCashReceived] = useState("");

  const payNow = payOption === "full" ? total : payOption === "partial" ? Number(partialAmount) || 0 : 0;
  const received = cashReceived.trim() === "" ? null : Number(cashReceived);
  const showCash = method === "Cash" && payOption !== "later";
  const change = showCash && received !== null && received >= payNow ? received - payNow : null;

  const [error, setError] = useState<string | null>(null);
  const [walkInHint, setWalkInHint] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [processing, setProcessing] = useState(false);
  const submitting = useRef(false);
  const [receipt, setReceipt] = useState<Receipt | null>(null);

  const validate = useCallback((): boolean => {
    setError(null);
    setWalkInHint(false);
    if (!navigator.onLine) return (setError(t("offline.blocked")), false);
    if (cart.length === 0) return (setError(t("pos.errEmpty")), false);
    if (discount < 0 || discount > subtotal) return (setError(t("pos.errDiscount")), false);
    if (!customerId && payOption !== "full") return (setError(t("pos.errWalkInFull")), setWalkInHint(true), false);
    if (payOption === "partial" && (payNow <= 0 || payNow >= total)) return (setError(t("pos.errPartial")), false);
    if (showCash && received !== null && received < payNow) return (setError(t("pos.notEnough")), false);
    return true;
  }, [cart.length, discount, subtotal, customerId, payOption, payNow, total, showCash, received, t]);

  const openConfirm = useCallback(() => {
    if (validate()) setConfirmOpen(true);
  }, [validate]);

  // F12 = complete sale
  const openConfirmRef = useRef(openConfirm);
  useEffect(() => {
    openConfirmRef.current = openConfirm;
  });
  useEffect(() => {
    function onKey(e: globalThis.KeyboardEvent) {
      if (e.key === "F12") {
        e.preventDefault();
        openConfirmRef.current();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  function resetSale() {
    setCart([]);
    setDiscountAmount("0");
    setCustomerId(null);
    setCustomerName("");
    setPayOption("full");
    setMethod("Cash");
    setPartialAmount("");
    setReference("");
    setCashReceived("");
    setError(null);
  }

  async function completeSale() {
    if (submitting.current) return;
    if (!navigator.onLine) {
      setConfirmOpen(false);
      setError(t("offline.blocked"));
      return;
    }
    submitting.current = true;
    setProcessing(true);
    let draftId: number | null = null;
    try {
      const order = await createOrder({
        customerId,
        notes: "",
        discountAmount: discount,
        items: cart.map((l) => ({ productId: l.productId, quantity: l.quantity, discountAmount: 0 })),
      });
      draftId = order.id;
      // One atomic server call: stock deduction + completion + initial payment (all-or-nothing).
      // The server also enforces "walk-in must be paid in full".
      const completed = await completeOrder(
        order.id,
        payNow > 0 ? { amount: payNow, method, referenceNumber: reference.trim(), notes: "" } : undefined,
      );
      draftId = null;
      setReceipt({
        orderNumber: completed.orderNumber,
        date: completed.completedAt ?? completed.createdAt,
        customer: customerId ? customerName : t("pos.walkIn"),
        lines: cart,
        subtotal,
        discount,
        total: completed.totalAmount,
        paid: payNow,
        received: showCash && received !== null ? received : null,
        change,
        debt: Math.max(0, completed.totalAmount - payNow),
        method: payNow > 0 ? method : null,
      });
      resetSale();
      setConfirmOpen(false);
    } catch (err) {
      setConfirmOpen(false);
      if (draftId !== null) {
        // Sale was rejected: do not leave an orphan draft behind (best effort).
        await cancelOrder(draftId).catch(() => undefined);
      }
      if (errorCode(err) === "WALKIN_FULL_PAYMENT_REQUIRED") {
        setError(t("pos.errWalkInFull"));
        setWalkInHint(true);
      } else if (errorShortages(err).length > 0) {
        const list = errorShortages(err)
          .map((s) => `${s.productName} (${t("pos.stockHave")} ${formatNumber(s.available)}, ${t("pos.stockNeed")} ${formatNumber(s.requested)})`)
          .join("; ");
        setError(`${t("pos.errStock")} ${list}`);
      } else {
        setError(errorMessage(err) ?? t("pos.saleFailed"));
      }
    } finally {
      submitting.current = false;
      setProcessing(false);
    }
  }

  // ---------- held sales ----------
  const [held, setHeld] = useState<HeldSale[]>(loadHeld);
  const [heldOpen, setHeldOpen] = useState(false);

  function holdSale() {
    if (cart.length === 0) return setError(t("pos.errEmpty"));
    const savedAt = new Date().toISOString();
    const entry: HeldSale = {
      id: savedAt,
      savedAt,
      cart,
      customerId,
      customerName,
      discount: discountAmount,
    };
    const next = [entry, ...held];
    setHeld(next);
    saveHeld(next);
    resetSale();
    toast.success(t("pos.holdSaved"));
  }

  function resumeHeld(h: HeldSale) {
    setCart(h.cart);
    setCustomerId(h.customerId);
    setCustomerName(h.customerName);
    setDiscountAmount(h.discount);
    setPayOption("full");
    const next = held.filter((x) => x.id !== h.id);
    setHeld(next);
    saveHeld(next);
    setHeldOpen(false);
  }

  function discardHeld(h: HeldSale) {
    const next = held.filter((x) => x.id !== h.id);
    setHeld(next);
    saveHeld(next);
  }

  // ---------- receipt view ----------
  if (receipt) {
    return (
      <div className="mx-auto max-w-md">
        <Card>
          <div className="text-center">
            <h1 className="text-xl font-bold text-green-600">{t("pos.saleDone")}</h1>
            <p className="mt-1 text-sm text-slate-500">
              {t("pos.receipt")} {receipt.orderNumber} · {formatDateTime(receipt.date)}
            </p>
            <p className="text-sm text-slate-700">{receipt.customer}</p>
          </div>
          <table className="my-4 w-full text-sm">
            <tbody className="divide-y divide-slate-100">
              {receipt.lines.map((l) => (
                <tr key={l.productId}>
                  <td className="py-1.5">
                    {l.productName}
                    <div className="text-xs text-slate-400">
                      {formatNumber(l.quantity)} × {formatMoney(l.salePrice)}
                    </div>
                  </td>
                  <td className="py-1.5 text-right">{formatMoney(l.quantity * l.salePrice)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="space-y-1 border-t border-slate-200 pt-3 text-sm">
            <Row label={t("pos.subtotal")} value={formatMoney(receipt.subtotal)} />
            <Row label={t("pos.discount")} value={formatMoney(receipt.discount)} />
            <Row label={t("pos.total")} value={formatMoney(receipt.total)} bold />
            <Row label={t("pos.paidNow")} value={formatMoney(receipt.paid)} />
            {receipt.received !== null && <Row label={t("pos.cashReceived")} value={formatMoney(receipt.received)} />}
            {receipt.change !== null && <Row label={t("pos.change")} value={formatMoney(receipt.change)} />}
            {receipt.debt > 0 && <Row label={t("pos.debt")} value={formatMoney(receipt.debt)} danger />}
          </div>
          <div className="mt-5 flex flex-wrap justify-center gap-2 print:hidden">
            <Button onClick={() => setReceipt(null)}>{t("pos.newSale")}</Button>
            <Button variant="secondary" onClick={() => window.print()}>
              {t("pos.print")}
            </Button>
            <Link to="/orders">
              <Button variant="secondary">{t("pos.viewOrders")}</Button>
            </Link>
          </div>
        </Card>
      </div>
    );
  }

  const pdata = products.data;
  const selectedCategory = (categories.data ?? []).find((c) => c.id === categoryId) ?? null;
  const walkIn = !customerId;

  return (
    <div className="w-full pb-24 xl:pb-0">
      <PageHeader
        title={t("pos.title")}
        subtitle={t("pos.shortcut")}
        actions={
          <Button variant="secondary" onClick={() => setHeldOpen(true)}>
            {t("pos.held")} ({held.length})
          </Button>
        }
      />

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
        {/* LEFT: search + categories + product cards */}
        <div className="min-w-0 space-y-4">
          <Card>
            <div onKeyDown={onSearchKey}>
              <SearchInput value={searchText} onChange={setSearchText} placeholder={t("pos.searchProducts")} />
            </div>
          </Card>

          <CategoryPanel
            categories={categories.data}
            loading={categories.loading}
            error={categories.error}
            onRetry={categories.reload}
            selectedId={categoryId}
            onSelect={setCategoryId}
          />

          <div id="pos-products" className="scroll-mt-20" />
          {selectedCategory && (
            <div className="flex items-center gap-2">
              <span className="min-w-0 truncate rounded-full bg-blue-50 px-3 py-1.5 text-sm font-semibold text-blue-700">{catNames.of(selectedCategory)}</span>
              <button
                type="button"
                onClick={() => setCategoryId(null)}
                aria-label={t("pos.catClear")}
                title={t("pos.catClear")}
                className="flex h-8 w-8 items-center justify-center rounded-full text-slate-500 hover:bg-slate-100 hover:text-slate-800"
              >
                ✕
              </button>
            </div>
          )}

          {products.loading && <LoadingSkeleton rows={4} />}
          {products.error && <ErrorState message={t("common.loadFailed")} onRetry={products.reload} />}
          {!products.loading && !products.error && pdata && pdata.items.length === 0 && (
            <Card>
              <EmptyState message={categoryId !== null && !query ? t("pos.categoryEmpty") : t("pos.noProducts")} />
            </Card>
          )}
          {!products.loading && !products.error && pdata && pdata.items.length > 0 && (
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 2xl:grid-cols-4">
              {pdata.items.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => addToCart(p)}
                  className="flex min-h-28 flex-col rounded-xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:border-blue-400 hover:shadow-md focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 active:scale-[0.98] active:border-blue-500"
                >
                  <span className="line-clamp-2 text-[15px] font-semibold text-slate-900">{p.name}</span>
                  <span className="mt-0.5 text-[13px] text-slate-500">{p.sku}</span>
                  <span className="mt-auto pt-3 text-lg font-bold text-blue-600">{formatMoney(p.salePrice)}</span>
                </button>
              ))}
            </div>
          )}
        </div>

        {/* RIGHT: cart, customer, totals, payment */}
        <div id="pos-cart" className="scroll-mt-20 space-y-4">
          <Card
            title={`${t("pos.cart")} (${cart.length} ${t("pos.items")})`}
            actions={
              cart.length > 0 ? (
                <Button size="sm" variant="ghost" onClick={() => setCart([])}>
                  {t("pos.clear")}
                </Button>
              ) : undefined
            }
          >
            {cart.length === 0 ? (
              <p className="py-4 text-center text-sm text-slate-500">{t("pos.cartEmpty")}</p>
            ) : (
              <ul className="divide-y divide-slate-100">
                {cart.map((l) => (
                  <li key={l.productId} className="py-3">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0 flex-1">
                        <p className="break-words text-[15px] font-medium leading-snug text-slate-900">{l.productName}</p>
                        <p className="mt-0.5 text-[13px] text-slate-600">{formatMoney(l.salePrice)}</p>
                      </div>
                      <button
                        type="button"
                        aria-label={t("pos.remove")}
                        onClick={() => setQuantity(l.productId, 0)}
                        className="-mr-2 -mt-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-lg text-lg text-slate-500 hover:text-red-600 active:bg-red-50 xl:-mt-1 xl:h-8 xl:w-8 xl:text-base"
                      >
                        ✕
                      </button>
                    </div>
                    <div className="mt-1 flex items-center justify-between gap-3">
                      <div className="flex items-center gap-1">
                        <button
                          type="button"
                          aria-label="−"
                          onClick={() => setQuantity(l.productId, l.quantity - 1)}
                          className="h-11 w-11 rounded-lg border border-slate-300 text-lg hover:bg-slate-50 active:bg-slate-100 xl:h-8 xl:w-8 xl:text-base"
                        >
                          −
                        </button>
                        <input
                          type="number"
                          inputMode="numeric"
                          min="1"
                          value={l.quantity}
                          onChange={(e) => setQuantity(l.productId, Number(e.target.value))}
                          className="h-11 w-16 rounded-lg border border-slate-300 text-center text-base xl:h-8 xl:w-14 xl:text-sm"
                        />
                        <button
                          type="button"
                          aria-label="+"
                          onClick={() => setQuantity(l.productId, l.quantity + 1)}
                          className="h-11 w-11 rounded-lg border border-slate-300 text-lg hover:bg-slate-50 active:bg-slate-100 xl:h-8 xl:w-8 xl:text-base"
                        >
                          +
                        </button>
                      </div>
                      <span className="text-base font-semibold text-slate-900">{formatMoney(l.quantity * l.salePrice)}</span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card title={t("pos.customer")}>
            {customerId ? (
              <div className="flex items-center justify-between">
                <span className="font-medium text-slate-900">{customerName}</span>
                <Button size="sm" variant="ghost" onClick={clearCustomer}>
                  {t("pos.clear")}
                </Button>
              </div>
            ) : (
              <div className="space-y-2">
                <p className="text-sm font-medium text-slate-700">{t("pos.walkIn")}</p>
                <input
                  className={inputClass}
                  placeholder={t("pos.searchCustomer")}
                  value={customerSearch}
                  onChange={(e) => onCustomerSearch(e.target.value)}
                />
                {customerResults.length > 0 && (
                  <ul className="max-h-40 divide-y divide-slate-100 overflow-auto rounded-lg border border-slate-200">
                    {customerResults.map((c) => (
                      <li key={c.id}>
                        <button
                          type="button"
                          onClick={() => selectCustomer(c)}
                          className="block w-full px-3 py-2 text-left text-sm hover:bg-slate-50"
                        >
                          {c.name} <span className="text-slate-400">— {c.phone}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                <Button size="sm" variant="secondary" onClick={() => setQuickOpen(true)}>
                  + {t("pos.newCustomer")}
                </Button>
                <p className="rounded-lg bg-amber-50 p-2 text-xs text-amber-800">{t("pos.walkInNote")}</p>
              </div>
            )}
          </Card>

          <Card>
            <div className="space-y-3 text-sm">
              <Row label={t("pos.subtotal")} value={formatMoney(subtotal)} />
              <div className="flex items-center justify-between gap-3">
                <span className="text-slate-600">{t("pos.discount")}</span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  className={`${inputClass} !w-32 text-right`}
                  value={discountAmount}
                  onChange={(e) => setDiscountAmount(e.target.value)}
                />
              </div>
              <div className="flex justify-between border-t border-slate-100 pt-3 text-xl font-bold text-slate-900">
                <span>{t("pos.total")}</span>
                <span>{formatMoney(total)}</span>
              </div>

              <div className="space-y-2 border-t border-slate-100 pt-3">
                <p className="font-semibold text-slate-800">{t("pos.payment")}</p>
                <Radio checked={payOption === "full"} onChange={() => setPayOption("full")} label={t("pos.payFull")} />
                <Radio
                  checked={payOption === "partial"}
                  onChange={() => setPayOption("partial")}
                  label={t("pos.payPartial")}
                  disabled={walkIn}
                />
                <Radio
                  checked={payOption === "later"}
                  onChange={() => setPayOption("later")}
                  label={t("pos.payLater")}
                  disabled={walkIn}
                />

                {payOption === "partial" && (
                  <input
                    type="number"
                    min="0"
                    className={inputClass}
                    placeholder={t("pos.amountNow")}
                    value={partialAmount}
                    onChange={(e) => setPartialAmount(e.target.value)}
                  />
                )}

                {payOption !== "later" && (
                  <>
                    <select className={inputClass} value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
                      {METHODS.map((m) => (
                        <option key={m} value={m}>
                          {t(`pay.method.${m}`)}
                        </option>
                      ))}
                    </select>
                    {method === "Cash" ? (
                      <div className="grid grid-cols-2 gap-2">
                        <FormField label={t("pos.cashReceived")}>
                          <input
                            type="number"
                            min="0"
                            className={inputClass}
                            value={cashReceived}
                            onChange={(e) => setCashReceived(e.target.value)}
                          />
                        </FormField>
                        <FormField label={t("pos.change")}>
                          <div className="rounded-lg bg-slate-50 px-3 py-2 text-[15px] font-semibold text-slate-900">
                            {change !== null ? formatMoney(change) : "—"}
                          </div>
                        </FormField>
                      </div>
                    ) : (
                      <input
                        className={inputClass}
                        placeholder={t("pos.reference")}
                        value={reference}
                        onChange={(e) => setReference(e.target.value)}
                      />
                    )}
                  </>
                )}
              </div>

              {error && (
                <div className="space-y-2 rounded-lg bg-red-50 p-2 text-sm text-red-700" role="alert">
                  <p>{error}</p>
                  {walkInHint && !customerId && (
                    <Button size="sm" variant="secondary" onClick={() => setQuickOpen(true)}>
                      + {t("pos.newCustomer")}
                    </Button>
                  )}
                </div>
              )}

              <div className="flex gap-2 pt-1">
                <Button className="flex-1 !py-3 text-base" onClick={openConfirm} disabled={cart.length === 0 || processing || !online}>
                  {t("pos.complete")} (F12)
                </Button>
                <Button variant="secondary" onClick={holdSale} disabled={cart.length === 0 || processing}>
                  {t("pos.hold")}
                </Button>
              </div>
            </div>
          </Card>
        </div>
      </div>

      {/* Phone / tablet: the cart is below the product list, so keep a total + "go to cart" bar in reach. */}
      {cart.length > 0 && (
        <div
          data-testid="pos-cart-bar"
          className="fixed inset-x-0 bottom-0 z-40 border-t border-slate-200 bg-white p-3 pb-[calc(env(safe-area-inset-bottom)+0.75rem)] shadow-[0_-4px_12px_rgba(15,23,42,0.08)] lg:left-60 xl:hidden"
        >
          <div className="mx-auto flex max-w-3xl items-center gap-3">
            <div className="min-w-0 flex-1">
              <p className="text-xs text-slate-500">
                {t("pos.cart")}: {cart.length} {t("pos.items")}
              </p>
              <p className="truncate text-lg font-bold text-slate-900">{formatMoney(total)}</p>
            </div>
            <Button
              className="!py-3 text-base"
              onClick={() => document.getElementById("pos-cart")?.scrollIntoView({ behavior: "smooth", block: "start" })}
            >
              {t("pos.goToCart")}
            </Button>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={confirmOpen}
        title={t("pos.confirmSale")}
        message={
          <div className="space-y-1">
            <p>
              {cart.length} {t("pos.items")} · {customerId ? customerName : t("pos.walkIn")}
            </p>
            <p className="text-lg font-bold text-slate-900">{formatMoney(total)}</p>
            <p>
              {t("pos.paidNow")}: {formatMoney(payNow)}
              {total - payNow > 0 && ` · ${t("pos.debt")}: ${formatMoney(total - payNow)}`}
            </p>
          </div>
        }
        confirmLabel={processing ? t("pos.processing") : t("pos.confirmSale")}
        loading={processing}
        onConfirm={completeSale}
        onCancel={() => !processing && setConfirmOpen(false)}
      />

      <Modal
        open={quickOpen}
        title={t("pos.newCustomer")}
        onClose={() => !quickSaving && setQuickOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setQuickOpen(false)} disabled={quickSaving}>
              {t("common.cancel")}
            </Button>
            <Button onClick={saveQuickCustomer} loading={quickSaving}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <FormField label={t("common.name")} required>
            <input className={inputClass} value={quickName} onChange={(e) => setQuickName(e.target.value)} autoFocus />
          </FormField>
          <FormField label={t("pos.phone")} required>
            <input className={inputClass} value={quickPhone} onChange={(e) => setQuickPhone(e.target.value)} />
          </FormField>
          {quickError && <p className="text-sm text-red-600">{quickError}</p>}
        </div>
      </Modal>

      <Modal open={heldOpen} title={t("pos.held")} onClose={() => setHeldOpen(false)}>
        {held.length === 0 ? (
          <p className="text-sm text-slate-500">{t("pos.noHeld")}</p>
        ) : (
          <ul className="divide-y divide-slate-100">
            {held.map((h) => (
              <li key={h.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                <div>
                  <p className="font-medium text-slate-900">
                    {h.cart.length} {t("pos.items")} ·{" "}
                    {formatMoney(h.cart.reduce((s, l) => s + l.quantity * l.salePrice, 0))}
                  </p>
                  <p className="text-xs text-slate-500">
                    {formatDateTime(h.savedAt)} · {h.customerId ? h.customerName : t("pos.walkIn")}
                  </p>
                </div>
                <div className="flex gap-2">
                  <Button size="sm" onClick={() => resumeHeld(h)}>
                    {t("pos.resume")}
                  </Button>
                  <Button size="sm" variant="ghost" className="!text-red-600" onClick={() => discardHeld(h)}>
                    {t("pos.discard")}
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Modal>
    </div>
  );
}

function Row({ label, value, bold, danger }: { label: string; value: string; bold?: boolean; danger?: boolean }) {
  return (
    <div className={`flex justify-between ${bold ? "text-base font-bold text-slate-900" : ""} ${danger ? "text-red-600" : ""}`}>
      <span className={bold || danger ? "" : "text-slate-600"}>{label}</span>
      <span>{value}</span>
    </div>
  );
}

function Radio({
  checked,
  onChange,
  label,
  disabled,
}: {
  checked: boolean;
  onChange: () => void;
  label: string;
  disabled?: boolean;
}) {
  return (
    <label className={`flex min-h-11 items-center gap-2 xl:min-h-0 ${disabled ? "text-slate-400" : "text-slate-700"}`}>
      <input type="radio" className="h-5 w-5 xl:h-4 xl:w-4" checked={checked} onChange={onChange} disabled={disabled} />
      {label}
    </label>
  );
}
