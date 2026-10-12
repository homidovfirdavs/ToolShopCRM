import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { getOrders, type OrderStatus } from "../api/orders";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDateTime, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import StatusBadge from "../components/ui/StatusBadge";
import SearchInput from "../components/ui/SearchInput";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const ORDER_TONE: Record<OrderStatus, "gray" | "green" | "red" | "yellow"> = {
  Draft: "gray",
  Completed: "green",
  Cancelled: "red",
  Returned: "yellow",
  PartiallyReturned: "yellow",
};

interface Filters {
  search: string;
  status: string;
  page: number;
}

export default function OrdersPage() {
  const { t } = useI18n();
  const { has } = usePermissions();
  const canSell = has("Sales.Create");

  const [params] = useSearchParams();
  const initialSearch = params.get("search") ?? "";
  const [filters, setFilters] = useState<Filters>({ search: initialSearch, status: "", page: 1 });
  const [searchText, setSearchText] = useState(initialSearch);

  useEffect(() => {
    const id = window.setTimeout(() => {
      setFilters((f) => (f.search === searchText ? f : { ...f, search: searchText, page: 1 }));
    }, 300);
    return () => window.clearTimeout(id);
  }, [searchText]);

  const list = useAsyncData(
    () =>
      getOrders({
        search: filters.search || undefined,
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
        title={t("ord.title")}
        actions={
          canSell ? (
            <Link to="/pos">
              <Button>+ {t("ord.newSale")}</Button>
            </Link>
          ) : undefined
        }
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <SearchInput className="sm:col-span-2" value={searchText} onChange={setSearchText} placeholder={t("ord.search")} />
          <select
            className={inputClass}
            value={filters.status}
            onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value, page: 1 }))}
          >
            <option value="">{t("ord.allStatuses")}</option>
            <option value="Draft">{t("ord.status.Draft")}</option>
            <option value="Completed">{t("ord.status.Completed")}</option>
            <option value="Cancelled">{t("ord.status.Cancelled")}</option>
            <option value="Returned">{t("ord.status.Returned")}</option>
          </select>
        </div>
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("ord.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <>
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("ord.number")}</th>
                  <th className={thClass}>{t("ord.date")}</th>
                  <th className={thClass}>{t("ord.customer")}</th>
                  <th className={thClass}>{t("ord.cashier")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={`${thClass} text-right`}>{t("ord.total")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((o) => (
                  <tr key={o.id} className="hover:bg-slate-50">
                    <td className={tdClass}>
                      <Link to={`/orders/${o.id}`} className="font-medium text-blue-600 hover:underline">
                        {o.orderNumber}
                      </Link>
                    </td>
                    <td className={tdClass}>{formatDateTime(o.orderDate)}</td>
                    <td className={tdClass}>{o.customerName === "Walk-in Customer" ? t("pos.walkIn") : o.customerName}</td>
                    <td className={tdClass}>{o.createdByUsername}</td>
                    <td className={tdClass}>
                      <StatusBadge label={t(`ord.status.${o.status}`)} tone={ORDER_TONE[o.status] ?? "gray"} />
                    </td>
                    <td className={`${tdClass} text-right font-medium`}>{formatMoney(o.totalAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {/* Phone: one tappable card per order instead of a wide table */}
          <ul className="divide-y divide-slate-100 md:hidden" data-testid="orders-cards">
            {data.items.map((o) => (
              <li key={o.id}>
                <Link to={`/orders/${o.id}`} className="block px-4 py-3 active:bg-slate-50">
                  <div className="flex items-center justify-between gap-3">
                    <span className="font-medium text-blue-600">{o.orderNumber}</span>
                    <StatusBadge label={t(`ord.status.${o.status}`)} tone={ORDER_TONE[o.status] ?? "gray"} />
                  </div>
                  <p className="mt-1 text-sm text-slate-700">{o.customerName === "Walk-in Customer" ? t("pos.walkIn") : o.customerName}</p>
                  <div className="mt-1 flex items-center justify-between gap-3 text-sm text-slate-500">
                    <span className="min-w-0 truncate">
                      {formatDateTime(o.orderDate)} · {o.createdByUsername}
                    </span>
                    <span className="shrink-0 font-semibold text-slate-900">{formatMoney(o.totalAmount)}</span>
                  </div>
                </Link>
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
          onChange={(page) => setFilters((f) => ({ ...f, page }))}
        />
      )}
    </div>
  );
}
