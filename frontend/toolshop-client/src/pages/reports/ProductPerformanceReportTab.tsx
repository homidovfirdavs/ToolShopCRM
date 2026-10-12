import { useEffect, useState } from "react";
import { getProductPerformance, type ProductPerformanceRow } from "../../api/reports";
import { DateRangeFields, Pager } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { defaultRange, formatMoney, formatPercent, styles } from "./reportUtils";

interface Filters {
  from: string;
  to: string;
  sortBy: string;
}

export default function ProductPerformanceReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const initial: Filters = { from: range.from, to: range.to, sortBy: "revenue" };

  const [draft, setDraft] = useState<Filters>(initial);
  const [applied, setApplied] = useState<Filters>(initial);
  const [page, setPage] = useState(1);

  const [rows, setRows] = useState<ProductPerformanceRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getProductPerformance({
          from: applied.from || undefined,
          to: applied.to || undefined,
          sortBy: applied.sortBy,
          page,
          pageSize: 20,
        });
        if (cancelled) return;
        setRows(result.items);
        setTotalPages(result.totalPages);
        setTotalItems(result.totalItems);
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
    const reset: Filters = { from: fresh.from, to: fresh.to, sortBy: "revenue" };
    setDraft(reset);
    setApplied(reset);
    setPage(1);
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
          <label style={styles.field}>
            {t("rep.sort")}
            <select style={styles.input} value={draft.sortBy} onChange={(e) => setDraft({ ...draft, sortBy: e.target.value })}>
              <option value="revenue">{t("rep.sort.revenue")}</option>
              <option value="profit">{t("rep.sort.profit")}</option>
              <option value="margin">{t("rep.sort.marginDesc")}</option>
              <option value="margin_asc">{t("rep.sort.marginAsc")}</option>
            </select>
          </label>
          <button style={styles.primaryButton} onClick={handleApply}>
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
            {t("rep.note.cogsProduct")}
          </div>
        )}
        {error && <p style={styles.error}>{t(error)}</p>}
        {loading ? (
          <p style={styles.muted}>{t("rep.loading")}</p>
        ) : (
          <>
            <div style={{ overflowX: "auto" }}>
              <table style={styles.table}>
                <thead>
                  <tr>
                    <th style={styles.th}>{t("rep.th.product")}</th>
                    <th style={styles.th}>SKU</th>
                    <th style={styles.thRight}>{t("rep.th.qty")}</th>
                    <th style={styles.thRight}>{t("rep.th.revenue")}</th>
                    <th style={styles.thRight}>COGS</th>
                    <th style={styles.thRight}>{t("rep.th.gross")}</th>
                    <th style={styles.thRight}>{t("rep.th.margin")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={7} style={styles.empty}>
                        {t("rep.noSales")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.productId}>
                      <td style={styles.td}>{r.productName}</td>
                      <td style={styles.td}>{r.sku}</td>
                      <td style={styles.tdRight}>{r.quantitySold}</td>
                      <td style={styles.tdRight}>{formatMoney(r.revenue)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.cogs)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.grossProfit)}</td>
                      <td style={styles.tdRight}>{formatPercent(r.grossMarginPercent)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager page={page} totalPages={totalPages} totalItems={totalItems} onPage={setPage} />
          </>
        )}
      </div>
    </div>
  );
}