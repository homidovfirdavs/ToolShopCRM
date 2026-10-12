import { useEffect, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { getStock, stockIn, stockOut, createAdjustment, type StockItem, type StockStatus } from "../api/inventory";
import { getCategories, getBrands } from "../api/catalog";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorMessage, errorStatus } from "../lib/apiError";
import { formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import FormField from "../components/ui/FormField";
import StatusBadge from "../components/ui/StatusBadge";
import Pagination from "../components/ui/Pagination";
import SearchInput from "../components/ui/SearchInput";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

type ModalMode = "in" | "out" | "adjust";

const STATUS_TONE: Record<StockStatus, "green" | "yellow" | "red"> = {
  InStock: "green",
  LowStock: "yellow",
  OutOfStock: "red",
};

interface Filters {
  search: string;
  categoryId: string;
  brandId: string;
  status: string;
  page: number;
}

export default function InventoryPage() {
  const { t } = useI18n();
  const { has } = usePermissions();
  const toast = useToast();
  const [params] = useSearchParams();
  const canManage = has("Inventory.Adjust");

  const [filters, setFilters] = useState<Filters>(() => ({
    search: "",
    categoryId: "",
    brandId: "",
    status: params.get("status") ?? "",
    page: 1,
  }));
  const [searchText, setSearchText] = useState("");

  useEffect(() => {
    const id = window.setTimeout(() => {
      setFilters((f) => (f.search === searchText ? f : { ...f, search: searchText, page: 1 }));
    }, 300);
    return () => window.clearTimeout(id);
  }, [searchText]);

  function patch(p: Partial<Filters>) {
    setFilters((f) => ({ ...f, ...p, page: p.page ?? 1 }));
  }

  const lookups = useAsyncData(async () => {
    const [categories, brands] = await Promise.all([getCategories(), getBrands()]);
    return { categories, brands };
  }, "lookups");

  const list = useAsyncData(
    () =>
      getStock({
        search: filters.search || undefined,
        categoryId: filters.categoryId ? Number(filters.categoryId) : undefined,
        brandId: filters.brandId ? Number(filters.brandId) : undefined,
        status: filters.status ? (filters.status as StockStatus) : undefined,
        page: filters.page,
        pageSize: 20,
      }),
    JSON.stringify(filters),
  );

  // ---- stock operation modal ----
  const [mode, setMode] = useState<ModalMode | null>(null);
  const [item, setItem] = useState<StockItem | null>(null);
  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [direction, setDirection] = useState<"increase" | "decrease">("increase");
  const [modalError, setModalError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  function openModal(m: ModalMode, it: StockItem) {
    setMode(m);
    setItem(it);
    setQuantity("");
    setReason("");
    setNotes("");
    setDirection("increase");
    setModalError(null);
  }

  function closeModal() {
    if (saving) return;
    setMode(null);
    setItem(null);
  }

  async function handleConfirm(e: FormEvent) {
    e.preventDefault();
    if (!item || !mode || saving) return;
    setModalError(null);

    const qty = Number(quantity);
    if (!qty || qty <= 0) return setModalError(t("inv.qtyPositive"));
    if (!reason.trim()) return setModalError(t("inv.reasonRequired"));
    const reducing = mode === "out" || (mode === "adjust" && direction === "decrease");
    if (reducing && qty > item.currentStock) {
      return setModalError(`${t("inv.exceeds")} (${formatNumber(item.currentStock)}).`);
    }

    setSaving(true);
    try {
      if (mode === "in") await stockIn(item.productId, qty, reason.trim(), notes.trim());
      else if (mode === "out") await stockOut(item.productId, qty, reason.trim(), notes.trim());
      else
        await createAdjustment(
          item.productId,
          direction === "increase" ? "AdjustmentIncrease" : "AdjustmentDecrease",
          qty,
          reason.trim(),
          notes.trim(),
        );
      toast.success(t("common.saved"));
      setMode(null);
      setItem(null);
      list.reload();
    } catch (err) {
      if (errorStatus(err) === 409) setModalError(errorMessage(err) ?? t("inv.insufficient"));
      else setModalError(t("common.somethingWrong"));
    } finally {
      setSaving(false);
    }
  }

  const data = list.data;
  const categories = lookups.data?.categories ?? [];
  const brands = lookups.data?.brands ?? [];
  const modalTitle = mode === "in" ? t("inv.stockIn") : mode === "out" ? t("inv.stockOut") : t("inv.adjustTitle");
  const hint = mode === "out" ? t("inv.hintOut") : mode === "adjust" ? t("inv.hintAdjust") : null;

  return (
    <div className="w-full">
      <PageHeader
        title={t("inv.title")}
        actions={
          <Link to="/inventory/movements">
            <Button variant="secondary">{t("inv.history")}</Button>
          </Link>
        }
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <SearchInput value={searchText} onChange={setSearchText} placeholder={t("prod.search")} />
          <select className={inputClass} value={filters.categoryId} onChange={(e) => patch({ categoryId: e.target.value })}>
            <option value="">{t("prod.allCategories")}</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          <select className={inputClass} value={filters.brandId} onChange={(e) => patch({ brandId: e.target.value })}>
            <option value="">{t("prod.allBrands")}</option>
            {brands.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </select>
          <select className={inputClass} value={filters.status} onChange={(e) => patch({ status: e.target.value })}>
            <option value="">{t("inv.allStatuses")}</option>
            <option value="InStock">{t("inv.status.InStock")}</option>
            <option value="LowStock">{t("inv.status.LowStock")}</option>
            <option value="OutOfStock">{t("inv.status.OutOfStock")}</option>
          </select>
        </div>
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("inv.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <>
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("inv.product")}</th>
                  <th className={thClass}>SKU</th>
                  <th className={thClass}>{t("prod.category")}</th>
                  <th className={thClass}>{t("prod.brand")}</th>
                  <th className={`${thClass} text-right`}>{t("inv.current")}</th>
                  <th className={`${thClass} text-right`}>{t("inv.minimum")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  {canManage && <th className={thClass}>{t("common.actions")}</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((it) => (
                  <tr key={it.productId} className="hover:bg-slate-50">
                    <td className={`${tdClass} font-medium text-slate-900`}>{it.productName}</td>
                    <td className={tdClass}>{it.sku}</td>
                    <td className={tdClass}>{it.categoryName}</td>
                    <td className={tdClass}>{it.brandName}</td>
                    <td className={`${tdClass} text-right font-semibold`}>{formatNumber(it.currentStock)}</td>
                    <td className={`${tdClass} text-right`}>{formatNumber(it.minimumStockLevel)}</td>
                    <td className={tdClass}>
                      <StatusBadge label={t(`inv.status.${it.status}`)} tone={STATUS_TONE[it.status]} />
                    </td>
                    {canManage && (
                      <td className={tdClass}>
                        <div className="flex flex-wrap gap-2">
                          <Button size="sm" variant="secondary" onClick={() => openModal("in", it)}>
                            {t("inv.stockIn")}
                          </Button>
                          <Button size="sm" variant="secondary" onClick={() => openModal("out", it)}>
                            {t("inv.stockOut")}
                          </Button>
                          <Button size="sm" variant="ghost" onClick={() => openModal("adjust", it)}>
                            {t("inv.adjust")}
                          </Button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <ul className="divide-y divide-slate-100 md:hidden" data-testid="inventory-cards">
            {data.items.map((it) => (
              <li key={it.productId} className="space-y-2 px-4 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-medium text-slate-900">{it.productName}</p>
                    <p className="text-xs text-slate-500">
                      {it.sku} · {it.categoryName} · {it.brandName}
                    </p>
                  </div>
                  <StatusBadge label={t(`inv.status.${it.status}`)} tone={STATUS_TONE[it.status]} />
                </div>
                <p className="text-sm text-slate-600">
                  {t("inv.current")}: <span className="font-semibold text-slate-900">{formatNumber(it.currentStock)}</span>
                  {" · "}
                  {t("inv.minimum")}: {formatNumber(it.minimumStockLevel)}
                </p>
                {canManage && (
                  <div className="flex flex-wrap gap-2">
                    <Button size="sm" variant="secondary" className="min-h-10" onClick={() => openModal("in", it)}>
                      {t("inv.stockIn")}
                    </Button>
                    <Button size="sm" variant="secondary" className="min-h-10" onClick={() => openModal("out", it)}>
                      {t("inv.stockOut")}
                    </Button>
                    <Button size="sm" variant="ghost" className="min-h-10" onClick={() => openModal("adjust", it)}>
                      {t("inv.adjust")}
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
          </>
        )}
      </Card>

      {data && (
        <Pagination
          page={filters.page}
          totalPages={data.totalPages}
          totalItems={data.totalItems}
          onChange={(page) => patch({ page })}
        />
      )}

      <Modal
        open={mode !== null && item !== null}
        title={modalTitle}
        onClose={closeModal}
        footer={
          <>
            <Button variant="secondary" onClick={closeModal} disabled={saving}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" form="stock-form" loading={saving}>
              {t("common.confirm")}
            </Button>
          </>
        }
      >
        {item && (
          <form id="stock-form" onSubmit={handleConfirm} className="space-y-4">
            <p className="text-sm text-slate-600">
              <span className="font-medium text-slate-900">{item.productName}</span> — {t("inv.current")}:{" "}
              <span className="font-semibold">{formatNumber(item.currentStock)}</span>
            </p>
            {hint && <p className="rounded-lg bg-slate-50 p-2 text-xs text-slate-600">{hint}</p>}

            {mode === "adjust" && (
              <div className="flex gap-4 text-sm">
                <label className="flex items-center gap-1">
                  <input type="radio" checked={direction === "increase"} onChange={() => setDirection("increase")} />
                  {t("inv.increase")}
                </label>
                <label className="flex items-center gap-1">
                  <input type="radio" checked={direction === "decrease"} onChange={() => setDirection("decrease")} />
                  {t("inv.decrease")}
                </label>
              </div>
            )}

            <FormField label={t("inv.quantity")} required>
              <input
                type="number"
                min="0"
                className={inputClass}
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
                autoFocus
              />
            </FormField>
            <FormField label={t("inv.reason")} required>
              <input className={inputClass} value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
            <FormField label={t("inv.notes")}>
              <textarea className={inputClass} rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
            </FormField>
            {modalError && (
              <p className="text-sm text-red-600" role="alert">
                {modalError}
              </p>
            )}
          </form>
        )}
      </Modal>
    </div>
  );
}
