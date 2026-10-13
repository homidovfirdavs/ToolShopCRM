import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import {
  Area, AreaChart, CartesianGrid, Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import {
  BarChart3, Calculator, ChevronRight, CircleAlert, ClipboardList, PackageX,
  TrendingUp, TriangleAlert, Users, Wallet,
} from "lucide-react";
import {
  getDashboardSummary, getSalesTrend, getSalesByCategory, getAttention,
  getLowStock, getFinancialSummaryShort, type DashboardSummary,
} from "../api/dashboard";
import { getPresetRange, toDateStr, type DateRange, type PresetKey } from "../lib/dateRange";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate, formatMoney, formatNumber } from "../lib/format";
import { useI18n } from "../i18n/useI18n";
import { useCategoryNames } from "../lib/categoryNames";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import KpiCard, { type KpiDelta } from "../components/ui/KpiCard";
import StatusBadge from "../components/ui/StatusBadge";
import DateRangeFilter from "../components/ui/DateRangeFilter";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";

const CATEGORY_COLORS = ["#2563eb", "#10b981", "#f59e0b", "#8b5cf6", "#ef4444", "#06b6d4", "#64748b"];

/** The period of the same length immediately before `range` (inclusive dates). */
function previousRange(range: DateRange): DateRange {
  const from = new Date(`${range.from}T00:00:00`);
  const to = new Date(`${range.to}T00:00:00`);
  const days = Math.round((to.getTime() - from.getTime()) / 86_400_000) + 1;
  const prevTo = new Date(from);
  prevTo.setDate(prevTo.getDate() - 1);
  const prevFrom = new Date(prevTo);
  prevFrom.setDate(prevFrom.getDate() - (days - 1));
  return { from: toDateStr(prevFrom), to: toDateStr(prevTo) };
}

/** null when it cannot be compared honestly (missing values or previous = 0). */
function pctChange(cur: number | null, prev: number | null | undefined): number | null {
  if (cur === null || prev === null || prev === undefined || prev === 0) return null;
  return ((cur - prev) / Math.abs(prev)) * 100;
}

async function loadAll(range: DateRange) {
  if (!range.from || !range.to) return null;
  const prev = previousRange(range);
  const [summary, trend, byCategory, attention, lowStock, fin, prevSummary] = await Promise.all([
    getDashboardSummary(range.from, range.to),
    getSalesTrend(range.from, range.to),
    getSalesByCategory(range.from, range.to),
    getAttention(range.from, range.to),
    getLowStock(10),
    getFinancialSummaryShort(range.from, range.to),
    // Comparison is optional: if it fails the cards simply show no change line.
    getDashboardSummary(prev.from, prev.to).catch((): DashboardSummary | null => null),
  ]);
  return { summary, trend, byCategory, attention, lowStock, fin, prevSummary };
}

const compactMoney = (v: number) =>
  Math.abs(v) >= 1000 ? `${new Intl.NumberFormat("en-US").format(v).replace(/,/g, " ")}` : String(v);

export default function DashboardPage() {
  const { t } = useI18n();
  const [preset, setPreset] = useState<PresetKey>("last30");
  const [range, setRange] = useState<DateRange>(getPresetRange("last30"));

  const { data, error, loading, reload } = useAsyncData(() => loadAll(range), `${range.from}|${range.to}`);
  const catNames = useCategoryNames();
  // Only the label text is translated; grouping, amounts and order come from the API unchanged.
  const byCategory = useMemo(
    () => (data?.byCategory ?? []).map((c) => ({ ...c, category: catNames.label(c.category) })),
    [data, catNames],
  );

  const money = (v: number | null) => formatMoney(v);
  // Backend value as is: Revenue − COGS − Operating Expenses, or null (N/A) when cost data is incomplete.
  const netProfit: number | null = data?.fin.netOperatingProfit ?? null;
  const periodLabel = `${formatDate(range.from)} → ${formatDate(range.to)}`;

  const delta = (cur: number | null, prev: number | null | undefined, upIsGood: boolean): KpiDelta => ({
    pct: pctChange(cur, prev),
    upIsGood,
    label: t("dash.vsPrev"),
  });

  return (
    <div className="w-full">
      <PageHeader title={t("dash.title")} subtitle={`${t("dash.subtitle")} · ${periodLabel}`} />
      <DateRangeFilter
        preset={preset}
        range={range}
        onChange={(p, r) => {
          setPreset(p);
          setRange(r);
        }}
      />

      {loading && <LoadingSkeleton rows={8} />}
      {error && <ErrorState message={t("dash.loadError")} onRetry={reload} />}

      {!loading && !error && data && (
        <>
          {!data.summary.hasCompleteCostData && (
            <p className="mb-4 flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
              <TriangleAlert size={18} className="mt-0.5 shrink-0" />
              <span>{t("dash.costNote")}</span>
            </p>
          )}

          {/* 5 KPIs, all from the API. Net Profit = Revenue − COGS (CostAtSale) − Operating Expenses, computed by the backend;
              it is null (shown as N/A, never 0) when the cost of any sold item is unknown. */}
          <div
            data-testid="dash-kpis"
            className="mb-5 grid grid-cols-1 gap-3 min-[640px]:grid-cols-2 min-[1024px]:grid-cols-6 min-[1500px]:grid-cols-5"
          >
            <div className="min-w-0 [&>*]:h-full min-[1024px]:col-span-2 min-[1500px]:col-span-1">
              <KpiCard
                accent="green"
                icon={<BarChart3 size={22} />}
                label={t("kpi.revenue")}
                value={money(data.summary.salesRevenue)}
                delta={delta(data.summary.salesRevenue, data.prevSummary?.salesRevenue, true)}
              />
            </div>
            <div className="min-w-0 [&>*]:h-full min-[1024px]:col-span-2 min-[1500px]:col-span-1">
              <KpiCard
                accent="teal"
                icon={<Wallet size={22} />}
                label={t("kpi.payments")}
                value={money(data.summary.paymentsReceived)}
                to="/payments"
                delta={delta(data.summary.paymentsReceived, data.prevSummary?.paymentsReceived, true)}
              />
            </div>
            <div className="min-w-0 [&>*]:h-full min-[1024px]:col-span-2 min-[1500px]:col-span-1">
              <KpiCard
                accent="red"
                icon={<Users size={22} />}
                label={t("kpi.debt")}
                value={money(data.summary.accountsReceivable)}
                hint={t("dash.debtAsOf")}
                to="/debts"
              />
            </div>
            <div className="min-w-0 [&>*]:h-full min-[1024px]:col-span-3 min-[1500px]:col-span-1">
              <KpiCard
                accent="amber"
                icon={<Calculator size={22} />}
                label={t("kpi.opex")}
                value={money(data.summary.operatingExpenses)}
                delta={delta(data.summary.operatingExpenses, data.prevSummary?.operatingExpenses, false)}
              />
            </div>
            <div className="min-w-0 [&>*]:h-full min-[640px]:col-span-2 min-[1024px]:col-span-3 min-[1500px]:col-span-1">
              <KpiCard
                accent="blue"
                icon={<TrendingUp size={22} />}
                label={t("kpi.netProfit")}
                value={money(netProfit)}
                tone={netProfit === null ? "warning" : netProfit < 0 ? "negative" : netProfit > 0 ? "positive" : "default"}
                hint={netProfit === null ? t("dash.warnNet") : undefined}
              />
            </div>
          </div>

          <div className="mb-5 grid grid-cols-1 gap-4 min-[1200px]:grid-cols-5">
            <Card title={t("dash.trend")} className="min-[1200px]:col-span-3 !rounded-2xl">
              {data.trend.length === 0 ? (
                <EmptyState message={t("dash.noData")} />
              ) : (
                <div className="h-[300px] min-[1200px]:h-[340px]">
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={data.trend} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
                      <defs>
                        <linearGradient id="gRev" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor="#2563eb" stopOpacity={0.22} />
                          <stop offset="100%" stopColor="#2563eb" stopOpacity={0.02} />
                        </linearGradient>
                        <linearGradient id="gGross" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor="#10b981" stopOpacity={0.2} />
                          <stop offset="100%" stopColor="#10b981" stopOpacity={0.02} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" vertical={false} />
                      <XAxis
                        dataKey="date"
                        tick={{ fontSize: 13, fill: "#475569" }}
                        tickLine={false}
                        axisLine={{ stroke: "#e2e8f0" }}
                        minTickGap={24}
                        tickFormatter={(v) => formatDate(String(v)).slice(0, 5)}
                      />
                      <YAxis
                        tick={{ fontSize: 13, fill: "#475569" }}
                        tickLine={false}
                        axisLine={false}
                        width={58}
                        tickFormatter={(v) => compactMoney(Number(v))}
                      />
                      <Tooltip
                        cursor={{ stroke: "#94a3b8", strokeDasharray: "4 4" }}
                        contentStyle={{ borderRadius: 12, border: "1px solid #e2e8f0", boxShadow: "0 8px 24px rgba(15,23,42,.12)", fontSize: 14 }}
                        labelFormatter={(v) => formatDate(String(v))}
                        formatter={(v) => formatMoney(Number(v))}
                      />
                      <Legend verticalAlign="top" align="center" iconType="circle" wrapperStyle={{ paddingBottom: 8 }} formatter={(v) => <span className="text-[15px] text-slate-700">{v}</span>} />
                      <Area
                        type="monotone"
                        dataKey="revenue"
                        name={t("dash.revenue")}
                        stroke="#2563eb"
                        strokeWidth={3}
                        fill="url(#gRev)"
                        dot={{ r: 3, strokeWidth: 2, fill: "#fff" }}
                        activeDot={{ r: 5 }}
                      />
                      {data.summary.hasCompleteCostData && (
                        <Area
                          type="monotone"
                          dataKey="grossProfit"
                          name={t("kpi.gross")}
                          stroke="#10b981"
                          strokeWidth={3}
                          fill="url(#gGross)"
                          dot={{ r: 3, strokeWidth: 2, fill: "#fff" }}
                          activeDot={{ r: 5 }}
                        />
                      )}
                    </AreaChart>
                  </ResponsiveContainer>
                </div>
              )}
              {!data.summary.hasCompleteCostData && data.trend.length > 0 && (
                <p className="mt-2 text-[13px] text-amber-800">{t("dash.warnGross")}</p>
              )}
            </Card>

            <Card title={t("dash.byCategory")} fill className="min-[1200px]:col-span-2 !rounded-2xl">
              {byCategory.length === 0 ? (
                <EmptyState message={t("dash.noData")} />
              ) : (
                <div className="flex flex-col items-center gap-4 min-[600px]:flex-row min-[1200px]:flex-col min-[1500px]:flex-row">
                  <div className="relative h-[230px] w-[230px] shrink-0 min-[1500px]:h-[250px] min-[1500px]:w-[250px]">
                    <ResponsiveContainer width="100%" height="100%">
                      <PieChart>
                        <Pie
                          data={byCategory}
                          dataKey="revenue"
                          nameKey="category"
                          innerRadius="62%"
                          outerRadius="100%"
                          paddingAngle={byCategory.length > 1 ? 2 : 0}
                          stroke="none"
                          isAnimationActive={false}
                        >
                          {byCategory.map((c, i) => (
                            <Cell key={c.category} fill={CATEGORY_COLORS[i % CATEGORY_COLORS.length]} />
                          ))}
                        </Pie>
                        <Tooltip formatter={(v) => formatMoney(Number(v))} />
                      </PieChart>
                    </ResponsiveContainer>
                    <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center text-center">
                      <span className="text-lg font-bold leading-tight text-slate-900">
                        {formatMoney(byCategory.reduce((s, c) => s + c.revenue, 0))}
                      </span>
                      <span className="text-[13px] text-slate-600">{t("dash.total")}</span>
                    </div>
                  </div>
                  <ul className="w-full min-w-0 flex-1 space-y-2.5">
                    {byCategory.map((c, i) => (
                      <li key={c.category} className="flex items-center gap-2 text-[15px]">
                        <span
                          className="h-2.5 w-2.5 shrink-0 rounded-full"
                          style={{ backgroundColor: CATEGORY_COLORS[i % CATEGORY_COLORS.length] }}
                        />
                        <span className="min-w-0 flex-1 truncate text-slate-700" title={c.category}>{c.category}</span>
                        <span className="shrink-0 font-semibold text-slate-900">{formatMoney(c.revenue)}</span>
                        <span className="w-14 shrink-0 text-right text-slate-600">{c.percentage.toFixed(1)}%</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </Card>
          </div>

          <div className="mb-5 grid grid-cols-1 gap-4 min-[1200px]:grid-cols-5">
            <Card title={<span className="flex items-center gap-2"><CircleAlert size={18} className="text-red-500" />{t("dash.attention")}</span>} className="min-[1200px]:col-span-2 !rounded-2xl">
              <div className="divide-y divide-slate-100">
                {data.attention.map((a) => {
                  const has = a.count > 0 || (a.amount !== null && a.amount > 0);
                  const nameKey = `att.${a.type}`;
                  const name = t(nameKey) === nameKey ? a.label : t(nameKey);
                  const desc = t(`${nameKey}.d`) === `${nameKey}.d` ? null : t(`${nameKey}.d`);
                  return (
                    <Link key={a.type} to={a.link} className="flex items-center gap-3 rounded-lg px-1 py-3 hover:bg-slate-50">
                      <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${a.type === "OutstandingDebt" ? "bg-red-50 text-red-600" : a.type === "OutOfStock" ? "bg-orange-50 text-orange-600" : a.type === "LowStock" ? "bg-amber-50 text-amber-600" : "bg-blue-50 text-blue-600"}`}>
                        {a.type === "OutstandingDebt" ? <Users size={20} /> : a.type === "DraftPurchases" ? <ClipboardList size={20} /> : <PackageX size={20} />}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block text-[15px] font-semibold text-slate-800 sm:truncate">{name}</span>
                        {desc && <span className="block text-[13px] text-slate-500 sm:truncate">{desc}</span>}
                      </span>
                      <span className={`shrink-0 rounded-lg px-2.5 py-1 text-[15px] font-semibold ${has ? (a.type === "OutstandingDebt" || a.type === "OutOfStock" ? "bg-red-50 text-red-600" : "bg-amber-50 text-amber-700") : "bg-slate-100 text-slate-500"}`}>
                        {a.amount !== null ? formatMoney(a.amount) : `${a.count} ${t("dash.pcs")}`}
                      </span>
                      <ChevronRight size={16} className="shrink-0 text-slate-400" />
                    </Link>
                  );
                })}
              </div>
            </Card>

            <Card
              title={t("dash.lowStockTitle")}
              className="min-[1200px]:col-span-3 !rounded-2xl"
              actions={<Link to="/inventory?status=LowStock" className="text-sm font-medium text-blue-600 hover:underline">{t("dash.viewAll")}</Link>}
            >
              {data.lowStock.length === 0 ? (
                <EmptyState message={t("dash.noLowStock")} />
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-[15px]">
                    <thead>
                      <tr className="border-b border-slate-100 text-left text-[13px] uppercase tracking-wide text-slate-600">
                        <th className="py-2 pr-2 font-medium">{t("dash.product")}</th>
                        <th className="py-2 pr-2 text-right font-medium">{t("dash.stock")}</th>
                        <th className="py-2 pr-2 text-right font-medium">{t("dash.min")}</th>
                        <th className="py-2 font-medium">{t("dash.status")}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-50">
                      {data.lowStock.slice(0, 6).map((p) => (
                        <tr key={p.productId}>
                          <td className="py-2.5 pr-2">
                            <span className="block font-medium text-slate-800">{p.productName}</span>
                            <span className="block text-[13px] text-slate-500">{p.sku}</span>
                          </td>
                          <td className={`py-2.5 pr-2 text-right font-semibold ${p.currentStock <= 0 ? "text-red-600" : "text-amber-600"}`}>
                            {formatNumber(p.currentStock)}
                          </td>
                          <td className="py-2.5 pr-2 text-right text-slate-600">{formatNumber(p.minimumStockLevel)}</td>
                          <td className="py-2.5">
                            <StatusBadge
                              label={p.status === "OutOfStock" ? t("dash.outOfStock") : t("dash.low")}
                              tone={p.status === "OutOfStock" ? "red" : "yellow"}
                            />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
