import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getProducts, getCategories, getBrands, deactivateProduct, type ProductListItem } from "../api/catalog";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import ConfirmDialog from "../components/ui/ConfirmDialog";
import StatusBadge from "../components/ui/StatusBadge";
import Pagination from "../components/ui/Pagination";
import SearchInput from "../components/ui/SearchInput";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

interface Filters {
  search: string;
  categoryId: string;
  brandId: string;
  status: string;
  page: number;
}

const INITIAL: Filters = { search: "", categoryId: "", brandId: "", status: "", page: 1 };

export default function ProductsPage() {
  const { t } = useI18n();
  const { has } = usePermissions();
  const toast = useToast();
  const canManage = has("Products.Update");
  const canCreate = has("Products.Create");

  const [filters, setFilters] = useState<Filters>(INITIAL);
  const [searchText, setSearchText] = useState("");

  // Debounce the search box; state is only set inside the timer callback.
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
      getProducts({
        search: filters.search || undefined,
        categoryId: filters.categoryId ? Number(filters.categoryId) : undefined,
        brandId: filters.brandId ? Number(filters.brandId) : undefined,
        isActive: filters.status === "" ? undefined : filters.status === "active",
        page: filters.page,
        pageSize: 20,
      }),
    JSON.stringify(filters),
  );

  const [toDeactivate, setToDeactivate] = useState<ProductListItem | null>(null);
  const [deactivating, setDeactivating] = useState(false);

  async function handleDeactivate() {
    if (!toDeactivate) return;
    setDeactivating(true);
    try {
      await deactivateProduct(toDeactivate.id);
      toast.success(t("common.deactivated"));
      setToDeactivate(null);
      list.reload();
    } catch {
      toast.error(t("common.deactivateFailed"));
    } finally {
      setDeactivating(false);
    }
  }

  const data = list.data;
  const categories = lookups.data?.categories ?? [];
  const brands = lookups.data?.brands ?? [];

  return (
    <div className="w-full">
      <PageHeader
        title={t("nav.products")}
        actions={
          canCreate ? (
            <Link to="/products/new">
              <Button>+ {t("prod.add")}</Button>
            </Link>
          ) : undefined
        }
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <SearchInput value={searchText} onChange={setSearchText} placeholder={t("prod.search")} className="sm:col-span-2 lg:col-span-1" />
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
            <option value="">{t("prod.allStatuses")}</option>
            <option value="active">{t("common.active")}</option>
            <option value="inactive">{t("common.inactive")}</option>
          </select>
        </div>
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("prod.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <>
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("common.name")}</th>
                  <th className={thClass}>SKU</th>
                  <th className={thClass}>{t("prod.category")}</th>
                  <th className={thClass}>{t("prod.brand")}</th>
                  {canManage && <th className={`${thClass} text-right`}>{t("prod.purchase")}</th>}
                  <th className={`${thClass} text-right`}>{t("prod.sale")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  {canManage && <th className={thClass}>{t("common.actions")}</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50">
                    <td className={tdClass}>
                      <p className="font-medium text-slate-900">{p.name}</p>
                      {p.barcode && <p className="text-xs text-slate-500">{p.barcode}</p>}
                    </td>
                    <td className={tdClass}>{p.sku}</td>
                    <td className={tdClass}>{p.categoryName}</td>
                    <td className={tdClass}>{p.brandName}</td>
                    {canManage && <td className={`${tdClass} text-right`}>{formatMoney(p.purchasePrice)}</td>}
                    <td className={`${tdClass} text-right font-medium`}>{formatMoney(p.salePrice)}</td>
                    <td className={tdClass}>
                      <StatusBadge
                        label={p.isActive ? t("common.active") : t("common.inactive")}
                        tone={p.isActive ? "green" : "gray"}
                      />
                    </td>
                    {canManage && (
                      <td className={tdClass}>
                        <div className="flex gap-2">
                          <Link to={`/products/${p.id}/edit`}>
                            <Button size="sm" variant="secondary">
                              {t("common.edit")}
                            </Button>
                          </Link>
                          {p.isActive && (
                            <Button size="sm" variant="ghost" className="!text-red-600" onClick={() => setToDeactivate(p)}>
                              {t("common.deactivate")}
                            </Button>
                          )}
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <ul className="divide-y divide-slate-100 md:hidden" data-testid="products-cards">
            {data.items.map((p) => (
              <li key={p.id} className="space-y-2 px-4 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-medium text-slate-900">{p.name}</p>
                    <p className="text-xs text-slate-500">
                      {p.sku} · {p.categoryName} · {p.brandName}
                    </p>
                    {p.barcode && <p className="text-xs text-slate-400">{p.barcode}</p>}
                  </div>
                  <StatusBadge label={p.isActive ? t("common.active") : t("common.inactive")} tone={p.isActive ? "green" : "gray"} />
                </div>
                <p className="text-sm text-slate-600">
                  {t("prod.sale")}: <span className="font-semibold text-slate-900">{formatMoney(p.salePrice)}</span>
                  {canManage && (
                    <>
                      {" · "}
                      {t("prod.purchase")}: {formatMoney(p.purchasePrice)}
                    </>
                  )}
                </p>
                {canManage && (
                  <div className="flex gap-2">
                    <Link to={`/products/${p.id}/edit`}>
                      <Button size="sm" variant="secondary" className="min-h-10">
                        {t("common.edit")}
                      </Button>
                    </Link>
                    {p.isActive && (
                      <Button size="sm" variant="ghost" className="min-h-10 !text-red-600" onClick={() => setToDeactivate(p)}>
                        {t("common.deactivate")}
                      </Button>
                    )}
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

      <ConfirmDialog
        open={toDeactivate !== null}
        title={t("common.deactivate")}
        message={t("prod.confirm")}
        confirmLabel={t("common.deactivate")}
        danger
        loading={deactivating}
        onConfirm={handleDeactivate}
        onCancel={() => setToDeactivate(null)}
      />
    </div>
  );
}
