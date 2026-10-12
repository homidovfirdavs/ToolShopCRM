import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getPurchases, type PurchaseStatus } from "../api/purchases";
import { getSuppliers } from "../api/suppliers";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import StatusBadge from "../components/ui/StatusBadge";
import SearchInput from "../components/ui/SearchInput";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const TONE: Record<PurchaseStatus, "gray" | "green" | "red"> = {
  Draft: "gray",
  Confirmed: "green",
  Cancelled: "red",
};

interface Filters {
  search: string;
  supplierId: string;
  status: string;
  page: number;
}

export default function PurchasesPage() {
  const { t } = useI18n();
  const { hasRole } = usePermissions();
  const canCreate = hasRole("Admin", "Manager", "Warehouse");

  const [filters, setFilters] = useState<Filters>({ search: "", supplierId: "", status: "", page: 1 });
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

  const suppliers = useAsyncData(() => getSuppliers({ pageSize: 100 }), "suppliers");

  const list = useAsyncData(
    () =>
      getPurchases({
        search: filters.search || undefined,
        supplierId: filters.supplierId ? Number(filters.supplierId) : undefined,
        status: filters.status || undefined,
        page: filters.page,
        pageSize: 20,
      }),
    JSON.stringify(filters),
  );

  const data = list.data;

  return (
    <div className="w-full">
      <PageHeader
        title={t("pur.title")}
        actions={
          canCreate ? (
            <Link to="/purchases/new">
              <Button>+ {t("pur.new")}</Button>
            </Link>
          ) : undefined
        }
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <SearchInput value={searchText} onChange={setSearchText} placeholder={t("pur.search")} />
          <select className={inputClass} value={filters.supplierId} onChange={(e) => patch({ supplierId: e.target.value })}>
            <option value="">{t("pur.allSuppliers")}</option>
            {(suppliers.data?.items ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
          <select className={inputClass} value={filters.status} onChange={(e) => patch({ status: e.target.value })}>
            <option value="">{t("pur.allStatuses")}</option>
            <option value="Draft">{t("pur.status.Draft")}</option>
            <option value="Confirmed">{t("pur.status.Confirmed")}</option>
            <option value="Cancelled">{t("pur.status.Cancelled")}</option>
          </select>
        </div>
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("pur.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("pur.number")}</th>
                  <th className={thClass}>{t("pur.supplier")}</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={`${thClass} text-right`}>{t("pur.total")}</th>
                  <th className={thClass}>{t("pur.createdBy")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50">
                    <td className={tdClass}>
                      <Link to={`/purchases/${p.id}`} className="font-medium text-blue-600 hover:underline">
                        {p.purchaseNumber}
                      </Link>
                    </td>
                    <td className={tdClass}>{p.supplierName}</td>
                    <td className={tdClass}>{formatDate(p.purchaseDate)}</td>
                    <td className={tdClass}>
                      <StatusBadge label={t(`pur.status.${p.status}`)} tone={TONE[p.status]} />
                    </td>
                    <td className={`${tdClass} text-right font-medium`}>{formatMoney(p.totalAmount)}</td>
                    <td className={tdClass}>{p.createdByUsername}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
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
    </div>
  );
}
