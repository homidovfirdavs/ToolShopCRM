import { useEffect, useState } from "react";
import { getPaymentReport, exportPaymentReport, type PaymentReportRow } from "../../api/reports";
import { DateRangeFields, ExportButton, FilterBar, Pager } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { defaultRange, formatDateTime, formatMoney, statusLabel, styles } from "./reportUtils";

interface Filters {
  from: string;
  to: string;
  method: string;
  status: string;
  search: string;
}

function buildParams(f: Filters) {
  return {
    from: f.from || undefined,
    to: f.to || undefined,
    method: f.method || undefined,
    status: f.status || undefined,
    search: f.search.trim() || undefined,
  };
}

function statusColor(status: string) {
  if (status === "Completed") return "#15803d";
  if (status === "Voided") return "#b91c1c";
  return "#c2410c";
}

export default function PaymentReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const initial: Filters = { from: range.from, to: range.to, method: "", status: "", search: "" };

  const [draft, setDraft] = useState<Filters>(initial);
  const [applied, setApplied] = useState<Filters>(initial);
  const [page, setPage] = useState(1);

  const [rows, setRows] = useState<PaymentReportRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getPaymentReport({ ...buildParams(applied), page, pageSize: 20 });
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
    const reset: Filters = { from: fresh.from, to: fresh.to, method: "", status: "", search: "" };
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
            <ExportButton onExport={() => exportPaymentReport(buildParams(applied))} />
          }
          more={
            <>
          <label style={styles.field}>
            {t("rep.paymentMethod")}
            <select style={styles.input} value={draft.method} onChange={(e) => setDraft({ ...draft, method: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              <option value="Cash">{statusLabel(t, "Cash")}</option>
              <option value="Card">{statusLabel(t, "Card")}</option>
              <option value="BankTransfer">{statusLabel(t, "BankTransfer")}</option>
              <option value="QR">{statusLabel(t, "QR")}</option>
              <option value="Other">{statusLabel(t, "Other")}</option>
            </select>
          </label>
          <label style={styles.field}>
            {t("rep.status")}
            <select style={styles.input} value={draft.status} onChange={(e) => setDraft({ ...draft, status: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              <option value="Completed">{statusLabel(t, "Completed")}</option>
              <option value="Voided">{statusLabel(t, "Voided")}</option>
              <option value="Refunded">{statusLabel(t, "Refunded")}</option>
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
            {t("rep.searchPayment")}
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
                    <th style={styles.th}>{t("rep.th.payNo")}</th>
                    <th style={styles.th}>{t("rep.th.date")}</th>
                    <th style={styles.th}>{t("rep.th.order")}</th>
                    <th style={styles.th}>{t("rep.th.customer")}</th>
                    <th style={styles.thRight}>{t("rep.th.amount")}</th>
                    <th style={styles.th}>{t("rep.th.method")}</th>
                    <th style={styles.th}>{t("rep.status")}</th>
                    <th style={styles.th}>{t("rep.th.receivedBy")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={8} style={styles.empty}>
                        {t("rep.noPayments")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.paymentId}>
                      <td style={styles.td}>{r.paymentNumber}</td>
                      <td style={styles.td}>{formatDateTime(r.createdAt)}</td>
                      <td style={styles.td}>{r.orderNumber}</td>
                      <td style={styles.td}>{r.customerName}</td>
                      <td style={styles.tdRight}>{formatMoney(r.amount)}</td>
                      <td style={styles.td}>{statusLabel(t, r.method)}</td>
                      <td style={{ ...styles.td, color: statusColor(r.status), fontWeight: 600 }}>{statusLabel(t, r.status)}</td>
                      <td style={styles.td}>{r.receivedByUserName}</td>
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