import { useState } from "react";
import {
  CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import { getFinanceSummary, getFinanceDaily, getExpensesByCategory } from "../api/finance";
import { getPresetRange, type DateRange, type PresetKey } from "../lib/dateRange";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate, formatMoney } from "../lib/format";
import { useI18n } from "../i18n/useI18n";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import KpiCard from "../components/ui/KpiCard";
import DateRangeFilter from "../components/ui/DateRangeFilter";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";

async function loadAll(range: DateRange) {
  if (!range.from || !range.to) return null;
  const [summary, daily, breakdown] = await Promise.all([
    getFinanceSummary(range.from, range.to),
    getFinanceDaily(range.from, range.to),
    getExpensesByCategory(range.from, range.to),
  ]);
  return { summary, daily, breakdown };
}

export default function FinancePage() {
  const { t } = useI18n();
  const [preset, setPreset] = useState<PresetKey>("last30");
  const [range, setRange] = useState<DateRange>(getPresetRange("last30"));

  const { data, error, loading, reload } = useAsyncData(() => loadAll(range), `${range.from}|${range.to}`);

  return (
    <div className="w-full">
      <PageHeader title={t("fin.title")} subtitle={t("fin.subtitle")} />
      <DateRangeFilter
        preset={preset}
        range={range}
        onChange={(p, r) => {
          setPreset(p);
          setRange(r);
        }}
      />

      {loading && <LoadingSkeleton rows={8} />}
      {error && <ErrorState message={t("fin.loadError")} onRetry={reload} />}

      {!loading && !error && data && (() => {
        const { summary, daily, breakdown } = data;
        const costUnknown = summary.unknownCostItemsCount > 0;
        const chartData = daily.map((d) => ({
          date: d.date.slice(0, 10),
          revenue: d.revenue,
          grossProfit: d.grossProfit,
          expenses: d.expenses,
        }));
        const gross = costUnknown ? null : summary.grossProfit;
        const net = costUnknown ? null : summary.netOperatingProfit;

        return (
          <>
            {costUnknown && (
              <p className="mb-4 rounded-lg bg-amber-50 p-3 text-sm text-amber-800">
                {summary.unknownCostItemsCount} {t("fin.costNote")}
              </p>
            )}

            <div className="mb-6 grid grid-cols-1 gap-3 min-[480px]:grid-cols-2 min-[900px]:grid-cols-3">
              <KpiCard label={t("kpi.revenue")} value={formatMoney(summary.revenue)} />
              <KpiCard label={t("kpi.gross")} value={formatMoney(gross)} tone={gross === null ? "warning" : "positive"} />
              <KpiCard label={t("kpi.opex")} value={formatMoney(summary.operatingExpenses)} tone="negative" />
              <KpiCard
                label={t("kpi.net")}
                value={formatMoney(net)}
                tone={net === null ? "warning" : net >= 0 ? "positive" : "negative"}
              />
              <KpiCard label={t("kpi.debt")} value={formatMoney(summary.accountsReceivable)} tone="warning" to="/debts" />
              <KpiCard label={t("kpi.payments")} value={formatMoney(summary.paymentsReceived)} to="/payments" />
            </div>

            <Card title={t("fin.trend")} className="mb-6">
              {costUnknown && <p className="mb-2 text-[13px] text-slate-500">{t("fin.trendNote")}</p>}
              {chartData.length === 0 ? (
                <EmptyState message={t("dash.noData")} />
              ) : (
                <ResponsiveContainer width="100%" height={340}>
                  <LineChart data={chartData}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="date" tick={{ fontSize: 13 }} tickFormatter={(v) => formatDate(String(v)).slice(0, 5)} />
                    <YAxis tick={{ fontSize: 13 }} />
                    <Tooltip labelFormatter={(v) => formatDate(String(v))} formatter={(v) => formatMoney(Number(v))} />
                    <Legend />
                    <Line type="monotone" dataKey="revenue" name={t("dash.revenue")} stroke="#2563eb" strokeWidth={2} dot={false} />
                    {!costUnknown && (
                      <Line type="monotone" dataKey="grossProfit" name={t("kpi.gross")} stroke="#16a34a" strokeWidth={2} dot={false} />
                    )}
                    <Line type="monotone" dataKey="expenses" name={t("dash.expenses")} stroke="#dc2626" strokeWidth={2} dot={false} />
                  </LineChart>
                </ResponsiveContainer>
              )}
            </Card>

            <Card title={t("fin.byCategory")}>
              {breakdown.length === 0 ? (
                <EmptyState message={t("fin.noExpenses")} />
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <tbody className="divide-y divide-slate-100">
                      {breakdown.map((e) => (
                        <tr key={e.category}>
                          <td className="py-2">{e.category}</td>
                          <td className="py-2 text-right font-medium">{formatMoney(e.amount)}</td>
                          <td className="py-2 text-right text-slate-500">{e.percentage}%</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </>
        );
      })()}
    </div>
  );
}
