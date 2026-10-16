import { useEffect, useState } from "react";
import { getReturnsReport, type ReturnsReport } from "../../api/reports";
import { DateRangeFields, FilterBar, Pager } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { defaultRange, formatDateTime, formatMoney, statusLabel, styles } from "./reportUtils";

/** Returns of the period, listed by the date of the RETURN (not of the original sale). Unknown cost is N/A, never 0. */
export default function ReturnsReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const [draft, setDraft] = useState({ from: range.from, to: range.to });
  const [applied, setApplied] = useState({ from: range.from, to: range.to });
  const [page, setPage] = useState(1);

  const [report, setReport] = useState<ReturnsReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getReturnsReport({
          from: applied.from || undefined,
          to: applied.to || undefined,
          page,
          pageSize: 20,
        });
        if (cancelled) return;
        setReport(result);
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
  }, [applied, page]);

  function handleApply() {
    setPage(1);
    setApplied({ ...draft });
  }

  function handleReset() {
    const fresh = defaultRange();
    setDraft(fresh);
    setApplied(fresh);
    setPage(1);
  }

  const money = (v: number | null | undefined) => (v === null || v === undefined ? "N/A" : formatMoney(v));
  const rows = report?.items ?? [];

  return (
    <div>
      <div style={styles.card}>
        <FilterBar onApply={handleApply} onReset={handleReset}>
          <DateRangeFields
            from={draft.from}
            to={draft.to}
            onFromChange={(v) => setDraft({ ...draft, from: v })}
            onToChange={(v) => setDraft({ ...draft, to: v })}
          />
        </FilterBar>
      </div>

      {report && (
        <div style={{ ...styles.card, display: "flex", flexWrap: "wrap", gap: 24 }} data-testid="returns-totals">
          <div>
            <div style={styles.muted}>{t("rep.ret.count")}</div>
            <strong>{report.returnsCount}</strong>
          </div>
          <div>
            <div style={styles.muted}>{t("rep.ret.goods")}</div>
            <strong>{formatMoney(report.goodsValue)}</strong>
          </div>
          <div>
            <div style={styles.muted}>{t("rep.ret.refunds")}</div>
            <strong>{formatMoney(report.refundsPaid)}</strong>
          </div>
          <div>
            <div style={styles.muted}>{t("rep.ret.costReversed")}</div>
            <strong>{money(report.costReversed)}</strong>
          </div>
          <div>
            <div style={styles.muted}>{t("rep.ret.writeOff")}</div>
            <strong>{money(report.writeOffCost)}</strong>
          </div>
        </div>
      )}

      <div style={styles.card}>
        <p style={{ ...styles.muted, fontSize: 12, marginTop: 0 }}>{t("rep.ret.note")}</p>
        {error && <p style={styles.error}>{t(error)}</p>}
        {loading ? (
          <p style={styles.muted}>{t("rep.loading")}</p>
        ) : (
          <>
            <div style={{ overflowX: "auto" }}>
              <table style={styles.table}>
                <thead>
                  <tr>
                    <th style={styles.th}>{t("rep.th.returnNo")}</th>
                    <th style={styles.th}>{t("rep.th.date")}</th>
                    <th style={styles.th}>{t("rep.th.order")}</th>
                    <th style={styles.th}>{t("rep.th.customer")}</th>
                    <th style={styles.thRight}>{t("rep.th.units")}</th>
                    <th style={styles.thRight}>{t("rep.th.restocked")}</th>
                    <th style={styles.thRight}>{t("rep.th.writtenOff")}</th>
                    <th style={styles.thRight}>{t("rep.th.goodsValue")}</th>
                    <th style={styles.thRight}>{t("rep.th.refund")}</th>
                    <th style={styles.th}>{t("rep.th.method")}</th>
                    <th style={styles.thRight}>{t("rep.th.costReversed")}</th>
                    <th style={styles.thRight}>{t("rep.th.writeOffCost")}</th>
                    <th style={styles.th}>{t("rep.th.returnedBy")}</th>
                    <th style={styles.th}>{t("rep.th.reason")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={14} style={styles.empty}>
                        {t("rep.noReturns")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.returnId}>
                      <td style={styles.td}>{r.returnNumber}</td>
                      <td style={styles.td}>{formatDateTime(r.returnedAt)}</td>
                      <td style={styles.td}>{r.orderNumber}</td>
                      <td style={styles.td}>{r.customerName ?? t("pos.walkIn")}</td>
                      <td style={styles.tdRight}>{r.returnedUnits}</td>
                      <td style={styles.tdRight}>{r.restockedUnits}</td>
                      <td style={styles.tdRight}>{r.writtenOffUnits}</td>
                      <td style={styles.tdRight}>{formatMoney(r.goodsValue)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.refundAmount)}</td>
                      <td style={styles.td}>{statusLabel(t, r.refundMethod)}</td>
                      <td style={styles.tdRight}>{money(r.costReversed)}</td>
                      <td style={styles.tdRight}>{money(r.writeOffCost)}</td>
                      <td style={styles.td}>{r.returnedByUserName}</td>
                      <td style={styles.td}>{r.reason}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager
              page={page}
              totalPages={report?.totalPages ?? 1}
              totalItems={report?.totalItems ?? 0}
              onPage={setPage}
            />
          </>
        )}
      </div>
    </div>
  );
}
