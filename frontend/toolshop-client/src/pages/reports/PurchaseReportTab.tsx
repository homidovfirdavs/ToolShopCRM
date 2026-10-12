import { useEffect, useState } from "react";
import { getPurchaseReport, exportPurchaseReport, type PurchaseReportRow } from "../../api/reports";
import { DateRangeFields, ExportButton, FilterBar, Pager } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { defaultRange, formatDate, formatDateTime, formatMoney, statusLabel, styles } from "./reportUtils";

interface Filters {
  from: string;
  to: string;
  status: string;
  search: string;
}

function buildParams(f: Filters) {
  return {
    from: f.from || undefined,
    to: f.to || undefined,
    status: f.status || undefined,
    search: f.search.trim() || undefined,
  };
}

export default function PurchaseReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const initial: Filters = { from: range.from, to: range.to, status: "", search: "" };

  const [draft, setDraft] = useState<Filters>(initial);
  const [applied, setApplied] = useState<Filters>(initial);
  const [page, setPage] = useState(1);

  const [rows, setRows] = useState<PurchaseReportRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getPurchaseReport({ ...buildParams(applied), page, pageSize: 20 });
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
    const reset: Filters = { from: fresh.from, to: fresh.to, status: "", search: "" };
    setDraft(reset);
    setApplied(reset);
    setPage(1);
  }

  return (
    <div>
      <div style={styles.card}>
        <FilterBar
          onApply={handleApply}
          onReset={handleReset}
          exportNode={
            <ExportButton onExport={() => exportPurchaseReport(buildParams(applied))} />
          }
          more={
            <>
          <label style={styles.field}>
            {t("rep.status")}
            <select style={styles.input} value={draft.status} onChange={(e) => setDraft({ ...draft, status: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              <option value="Draft">{statusLabel(t, "Draft")}</option>
              <option value="Confirmed">{statusLabel(t, "Confirmed")}</option>
              <option value="Cancelled">{statusLabel(t, "Cancelled")}</option>
            </select>
          </label>
            </>
          }
        >
          <DateRangeFields
            from={draft.from}
            to={draft.to}
            onFromChange={(v) => setDraft({ ...draft, from: v })}
            onToChange={(v) => setDraft({ ...draft, to: v })}
          />
          <label style={styles.field}>
            {t("rep.searchPurchase")}
            <input
              type="text"
              style={styles.input}
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleApply();
              }}
            />
          </label>
        </FilterBar>
      </div>

      <div style={styles.card}>
        {error && <p style={styles.error}>{t(error)}</p>}
        {loading ? (
          <p style={styles.muted}>{t("rep.loading")}</p>
        ) : (
          <>
            <div style={{ overflowX: "auto" }}>
              <table style={styles.table}>
                <thead>
                  <tr>
                    <th style={styles.th}>{t("rep.th.purchase")}</th>
                    <th style={styles.th}>{t("rep.th.date")}</th>
                    <th style={styles.th}>{t("rep.th.supplier")}</th>
                    <th style={styles.th}>{t("rep.status")}</th>
                    <th style={styles.thRight}>{t("rep.th.subtotal")}</th>
                    <th style={styles.thRight}>{t("rep.th.discount")}</th>
                    <th style={styles.thRight}>{t("rep.th.totalSum")}</th>
                    <th style={styles.th}>{t("rep.th.createdBy")}</th>
                    <th style={styles.th}>{t("rep.th.confirmed")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={9} style={styles.empty}>
                        {t("rep.noPurchases")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.purchaseId}>
                      <td style={styles.td}>{r.purchaseNumber}</td>
                      <td style={styles.td}>{formatDate(r.purchaseDate)}</td>
                      <td style={styles.td}>{r.supplierName}</td>
                      <td style={styles.td}>{statusLabel(t, r.status)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.subtotal)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.discountAmount)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.totalAmount)}</td>
                      <td style={styles.td}>{r.createdByUserName}</td>
                      <td style={styles.td}>{formatDateTime(r.confirmedAt)}</td>
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