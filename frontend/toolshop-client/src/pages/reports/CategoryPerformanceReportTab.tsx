import { useEffect, useState } from "react";
import { getCategoryPerformance, type CategoryPerformanceRow } from "../../api/reports";
import { DateRangeFields } from "./shared";
import { defaultRange, formatMoney, formatPercent, styles } from "./reportUtils";
import { useI18n } from "../../i18n/useI18n";

export default function CategoryPerformanceReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const [draft, setDraft] = useState(range);
  const [applied, setApplied] = useState(range);

  const [rows, setRows] = useState<CategoryPerformanceRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getCategoryPerformance(applied.from || undefined, applied.to || undefined);
        if (cancelled) return;
        setRows(result);
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

  const hasIncomplete = rows.some((r) => !r.hasCompleteCostData);

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

      <div style={styles.card}>
        {hasIncomplete && (
          <div style={styles.warning}>
            {t("rep.note.cogsCategory")}
          </div>
        )}
        {error && <p style={styles.error}>{t(error)}</p>}
        {loading ? (
          <p style={styles.muted}>{t("rep.loading")}</p>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table style={styles.table}>
              <thead>
                <tr>
                  <th style={styles.th}>{t("rep.category")}</th>
                  <th style={styles.thRight}>{t("rep.th.qtySold")}</th>
                  <th style={styles.thRight}>{t("rep.th.revenue")}</th>
                  <th style={styles.thRight}>COGS</th>
                  <th style={styles.thRight}>{t("rep.th.gross")}</th>
                  <th style={styles.thRight}>{t("rep.th.margin")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={6} style={styles.empty}>
                      {t("rep.noSales")}
                    </td>
                  </tr>
                )}
                {rows.map((r) => (
                  <tr key={r.category}>
                    <td style={styles.td}>{r.category}</td>
                    <td style={styles.tdRight}>{r.quantitySold}</td>
                    <td style={styles.tdRight}>{formatMoney(r.revenue)}</td>
                    <td style={styles.tdRight}>{formatMoney(r.cogs)}</td>
                    <td style={styles.tdRight}>{formatMoney(r.grossProfit)}</td>
                    <td style={styles.tdRight}>{formatPercent(r.marginPercent)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}