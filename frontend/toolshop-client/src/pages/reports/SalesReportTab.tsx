import { useEffect, useState } from "react";
import { getSalesReport, getSalesSummary, exportSalesReport, type SalesReportRow, type SalesReportSummary } from "../../api/reports";
import { DateRangeFields, ExportButton, FilterBar, Pager } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { defaultRange, formatDateTime, formatMoney, statusLabel, styles } from "./reportUtils";

interface Filters {
  from: string;
  to: string;
  status: string;
  paymentStatus: string;
  search: string;
}

function buildParams(f: Filters) {
  return {
    from: f.from || undefined,
    to: f.to || undefined,
    status: f.status || undefined,
    paymentStatus: f.paymentStatus || undefined,
    search: f.search.trim() || undefined,
  };
}

export default function SalesReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const emptyFilters: Filters = { from: range.from, to: range.to, status: "", paymentStatus: "", search: "" };

  const [draft, setDraft] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);
  const [page, setPage] = useState(1);

  const [rows, setRows] = useState<SalesReportRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<SalesReportSummary | null>(null);
  const [summaryError, setSummaryError] = useState(false);

  // Totals for ALL filtered orders - independent of the current page.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await getSalesSummary(buildParams(applied));
        if (cancelled) return;
        setSummary(result);
        setSummaryError(false);
      } catch (err) {
        if (cancelled) return;
        console.error(err);
        setSummary(null);
        setSummaryError(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [applied]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getSalesReport({ ...buildParams(applied), page, pageSize: 20 });
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
    const reset: Filters = { from: fresh.from, to: fresh.to, status: "", paymentStatus: "", search: "" };
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
            <ExportButton onExport={() => exportSalesReport(buildParams(applied))} />
          }
          more={
            <>
          <label style={styles.field}>
            {t("rep.orderStatus")}
            <select style={styles.input} value={draft.status} onChange={(e) => setDraft({ ...draft, status: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              <option value="Draft">{statusLabel(t, "Draft")}</option>
              <option value="Completed">{statusLabel(t, "Completed")}</option>
              <option value="Cancelled">{statusLabel(t, "Cancelled")}</option>
              <option value="Returned">{statusLabel(t, "Returned")}</option>
              <option value="PartiallyReturned">{statusLabel(t, "PartiallyReturned")}</option>
            </select>
          </label>
          <label style={styles.field}>
            {t("rep.paymentStatus")}
            <select
              style={styles.input}
              value={draft.paymentStatus}
              onChange={(e) => setDraft({ ...draft, paymentStatus: e.target.value })}
            >
              <option value="">{t("rep.all")}</option>
              <option value="Paid">{statusLabel(t, "Paid")}</option>
              <option value="Unpaid">{statusLabel(t, "Unpaid")}</option>
              <option value="PartiallyPaid">{statusLabel(t, "PartiallyPaid")}</option>
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
            {t("rep.searchOrder")}
            <input
              type="text"
              style={styles.input}
              value={draft.search}
              placeholder="ORD-..."
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
                    <th style={styles.th}>{t("rep.th.order")}</th>
                    <th style={styles.th}>{t("rep.th.date")}</th>
                    <th style={styles.th}>{t("rep.th.customer")}</th>
                    <th style={styles.th}>{t("rep.th.cashier")}</th>
                    <th style={styles.th}>{t("rep.status")}</th>
                    <th style={styles.thRight}>{t("rep.th.total")}</th>
                    <th style={styles.thRight}>{t("rep.th.discount")}</th>
                    <th style={styles.thRight}>{t("rep.th.paid")}</th>
                    <th style={styles.thRight}>{t("rep.th.debt")}</th>
                    <th style={styles.th}>{t("rep.th.payment")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={10} style={styles.empty}>
                        {t("rep.noData")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.orderId}>
                      <td style={styles.td}>{r.orderNumber}</td>
                      <td style={styles.td}>{formatDateTime(r.orderDate)}</td>
                      <td style={styles.td}>{r.customerName ?? t("rep.walkIn")}</td>
                      <td style={styles.td}>{r.cashierName}</td>
                      <td style={styles.td}>{statusLabel(t, r.status)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.totalAmount)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.discountAmount)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.totalPaid)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.outstandingBalance)}</td>
                      <td style={styles.td}>{statusLabel(t, r.paymentStatus)}</td>
                    </tr>
                  ))}
                </tbody>
                {summary && rows.length > 0 && (
                  <tfoot>
                    <tr style={{ background: "#f8fafc", fontWeight: 700 }}>
                      <td style={styles.td} colSpan={5}>
                        {t("rep.totalAllFiltered")} · {t("rep.totalOrders").replace("{n}", String(summary.orderCount))}
                      </td>
                      <td style={styles.tdRight}>{formatMoney(summary.totalAmount)}</td>
                      <td style={styles.tdRight}>{formatMoney(summary.discountAmount)}</td>
                      <td style={styles.tdRight}>{formatMoney(summary.totalPaid)}</td>
                      <td style={styles.tdRight}>{formatMoney(summary.outstandingBalance)}</td>
                      <td style={styles.td}></td>
                    </tr>
                    <tr style={{ background: "#f1f5f9", fontWeight: 700 }}>
                      <td style={styles.td} colSpan={5}>
                        {t("rep.totalCompleted")} · {t("rep.totalOrders").replace("{n}", String(summary.completedOrderCount))}
                      </td>
                      <td style={styles.tdRight}>{formatMoney(summary.completedTotalAmount)}</td>
                      <td style={styles.tdRight}></td>
                      <td style={styles.tdRight}>{formatMoney(summary.completedTotalPaid)}</td>
                      <td style={styles.tdRight}>{formatMoney(summary.completedOutstandingBalance)}</td>
                      <td style={styles.td}></td>
                    </tr>
                  </tfoot>
                )}
              </table>
            </div>
            {summaryError && <p style={styles.error}>{t("rep.totalLoadError")}</p>}
            {summary && rows.length > 0 && <p style={{ ...styles.muted, fontSize: 12, margin: "8px 0 0" }}>{t("rep.totalHint")}</p>}
            <Pager page={page} totalPages={totalPages} totalItems={totalItems} onPage={setPage} />
          </>
        )}
      </div>
    </div>
  );
}