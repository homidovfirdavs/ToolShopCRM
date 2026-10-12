import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getDebts, getWalkInExceptions } from "../api/payments";
import { useAuth } from "../context/AuthContext";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import SearchInput from "../components/ui/SearchInput";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { tdClass, thClass } from "../components/ui/formStyles";

function WalkInExceptions() {
  const { t } = useI18n();
  const res = useAsyncData(() => getWalkInExceptions(), "walk-in-exceptions");
  if (res.loading) return <p className="mt-2 text-xs">…</p>;
  if (res.error || !res.data) return <p className="mt-2 text-xs">{t("common.loadFailed")}</p>;
  return (
    <div className="mt-3">
      <p className="mb-2 text-xs">{t("debt.walkInReadOnly")}</p>
      <div className="overflow-x-auto rounded border border-amber-200 bg-white">
        <table className="w-full text-xs text-slate-800">
          <thead className="bg-amber-100/60">
            <tr>
              <th className="px-3 py-2 text-left">{t("debt.colOrder")}</th>
              <th className="px-3 py-2 text-left">{t("debt.colDate")}</th>
              <th className="px-3 py-2 text-right">{t("debt.colTotal")}</th>
              <th className="px-3 py-2 text-right">{t("debt.colPaid")}</th>
              <th className="px-3 py-2 text-right">{t("debt.colOutstanding")}</th>
            </tr>
          </thead>
          <tbody>
            {res.data.items.map((o) => (
              <tr key={o.orderId} className="border-t border-amber-100">
                <td className="px-3 py-2">{o.orderNumber}</td>
                <td className="px-3 py-2">{formatDate(o.orderDate)}</td>
                <td className="px-3 py-2 text-right">{formatMoney(o.total)}</td>
                <td className="px-3 py-2 text-right">{formatMoney(o.paid)}</td>
                <td className="px-3 py-2 text-right font-semibold">{formatMoney(o.outstanding)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default function DebtsPage() {
  const { t } = useI18n();
  const { user } = useAuth();
  const canSeeExceptions = !!(user?.roles.includes("Admin") || user?.roles.includes("Manager"));
  const [showWalkIn, setShowWalkIn] = useState(false);
  const [q, setQ] = useState({ search: "", page: 1 });
  const [searchText, setSearchText] = useState("");
  const [sortDesc, setSortDesc] = useState(true);

  useEffect(() => {
    const id = window.setTimeout(() => {
      setQ((prev) => (prev.search === searchText ? prev : { search: searchText, page: 1 }));
    }, 300);
    return () => window.clearTimeout(id);
  }, [searchText]);

  const list = useAsyncData(() => getDebts(q.search, q.page), `${q.search}|${q.page}`);
  const data = list.data;

  const items = data ? [...data.items].sort((a, b) => (sortDesc ? b.totalDebt - a.totalDebt : a.totalDebt - b.totalDebt)) : [];

  return (
    <div className="w-full">
      <PageHeader title={t("debt.title")} />

      {data && (data.walkInUnresolvedDebt ?? 0) > 0 && (
        <div className="mb-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <p className="font-semibold">{t("debt.walkInTitle")}</p>
          <p className="mt-1">
            {t("debt.walkInText")
              .replace("{amount}", formatMoney(data.walkInUnresolvedDebt ?? 0))
              .replace("{n}", String(data.walkInUnresolvedOrderCount ?? 0))}
          </p>
          {canSeeExceptions && (
            <>
              <button type="button" className="mt-2 text-xs font-medium underline" onClick={() => setShowWalkIn((v) => !v)}>
                {showWalkIn ? t("debt.walkInHide") : t("debt.walkInShow")}
              </button>
              {showWalkIn && <WalkInExceptions />}
            </>
          )}
        </div>
      )}

      <Card className="mb-4">
        <SearchInput value={searchText} onChange={setSearchText} placeholder={t("debt.search")} className="sm:max-w-md" />
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && items.length === 0 && <EmptyState message={t("debt.empty")} />}
        {!list.loading && !list.error && data && items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("debt.customer")}</th>
                  <th className={thClass}>{t("debt.phone")}</th>
                  <th className={`${thClass} text-right`}>{t("debt.orders")}</th>
                  <th
                    className={`${thClass} cursor-pointer select-none text-right`}
                    onClick={() => setSortDesc((v) => !v)}
                    title={t("debt.sortHint")}
                  >
                    {t("debt.total")} {sortDesc ? "▼" : "▲"}
                  </th>
                  <th className={thClass}>{t("debt.last")}</th>
                  <th className={thClass}></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {items.map((d) => (
                  <tr key={d.customerId} className="hover:bg-slate-50">
                    <td className={`${tdClass} font-medium text-slate-900`}>{d.customerName}
                      {d.isActive === false && (
                        <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-xs font-normal text-slate-500">{t("debt.inactive")}</span>
                      )}
                    </td>
                    <td className={tdClass}>{d.phone ?? "—"}</td>
                    <td className={`${tdClass} text-right`}>{d.outstandingOrders}</td>
                    <td className={`${tdClass} text-right font-semibold text-red-600`}>{formatMoney(d.totalDebt)}</td>
                    <td className={tdClass}>{formatDate(d.lastPurchase)}</td>
                    <td className={tdClass}>
                      <Link to={`/customers/${d.customerId}`}>
                        <Button size="sm" variant="secondary">
                          {t("debt.view")}
                        </Button>
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {data && <Pagination page={q.page} totalPages={data.totalPages} totalItems={data.totalItems} onChange={(page) => setQ((p) => ({ ...p, page }))} />}
    </div>
  );
}
