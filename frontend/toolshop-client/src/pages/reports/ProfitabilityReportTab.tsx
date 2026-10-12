import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { getProfitabilityReport, type ProfitabilityReport } from "../../api/reports";
import { DateRangeFields } from "./shared";
import { defaultRange, formatMoney, formatPercent, styles } from "./reportUtils";
import { useI18n } from "../../i18n/useI18n";

function Kpi({ label, value, hint }: { label: string; value: ReactNode; hint?: string }) {
  return (
    <div style={{ border: "1px solid #e5e7eb", borderRadius: 8, padding: 12, background: "#fff" }}>
      <div style={{ fontSize: 12, color: "#666", marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 18, fontWeight: 700 }}>{value}</div>
      {hint && <div style={{ fontSize: 11, color: "#888", marginTop: 4 }}>{hint}</div>}
    </div>
  );
}

export default function ProfitabilityReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const [draft, setDraft] = useState(range);
  const [applied, setApplied] = useState(range);

  const [data, setData] = useState<ProfitabilityReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getProfitabilityReport(applied.from || undefined, applied.to || undefined);
        if (cancelled) return;
        setData(result);
        setError(null);
      } catch (err) {
        if (cancelled) return;
        console.error(err);
        setError("rep.loadError");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [applied]);

  function handleReset() {
    const fresh = defaultRange();
    setDraft(fresh);
    setApplied(fresh);
  }

  return (
    <div>
      <div style={styles.card}>
        <div style={styles.filterRow}>
          <DateRangeFields
            from={draft.from}
            to={draft.to}
            onFromChange={(v) => setDraft({ ...draft, from: v })}
            onToChange={(v) => setDraft({ ...draft, to: v })}
          />
          <button style={styles.primaryButton} onClick={() => setApplied({ ...draft })}>
            {t("rep.apply")}
          </button>
          <button style={styles.button} onClick={handleReset}>
            {t("rep.reset")}
          </button>
        </div>
      </div>

      {error && <p style={styles.error}>{t(error)}</p>}
      {loading && <p style={styles.muted}>{t("rep.loading")}</p>}

      {!loading && data && (
        <div style={styles.card}>
          <p style={{ ...styles.muted, marginTop: 0, fontSize: 12 }}>
            {t("rep.period")}: {data.dateFrom} — {data.dateTo}
          </p>

          {!data.hasCompleteCostData && (
            <div style={styles.warning}>
              {t("rep.note.cogsAll")}
            </div>
          )}

          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 12 }}>
            <Kpi label={t("rep.kpi.revenue")} value={`${formatMoney(data.revenue)}`} />
            <Kpi label="COGS" value={data.cogs === null ? "N/A" : `${formatMoney(data.cogs)}`} />
            <Kpi label={t("rep.kpi.grossProfit")} value={data.grossProfit === null ? "N/A" : `${formatMoney(data.grossProfit)}`} />
            <Kpi label={t("rep.kpi.grossMargin")} value={formatPercent(data.grossMarginPercent)} />
            <Kpi label={t("rep.kpi.opex")} value={`${formatMoney(data.operatingExpenses)}`} />
            <Kpi
              label={t("rep.kpi.netProfit")}
              value={data.netOperatingProfit === null ? "N/A" : `${formatMoney(data.netOperatingProfit)}`}
            />
            <Kpi label={t("rep.kpi.netMargin")} value={formatPercent(data.netOperatingMarginPercent)} />
            <Kpi label={t("rep.kpi.paymentsReceived")} value={`${formatMoney(data.paymentsReceived)}`} />
            <Kpi label={t("rep.kpi.ar")} value={`${formatMoney(data.accountsReceivable)}`} hint={t("rep.kpi.arHint")} />
            <Kpi label={t("rep.kpi.purchaseSpend")} value={`${formatMoney(data.purchaseSpend)}`} />
            <Kpi label={t("rep.kpi.completedOrders")} value={data.completedOrdersCount} />
            <Kpi label={t("rep.kpi.avgOrder")} value={`${formatMoney(data.averageOrderValue)}`} />
          </div>
        </div>
      )}
    </div>
  );
}