import { useEffect, useState } from "react";
import { getCustomerDebtReport, exportCustomerDebtReport, type CustomerDebtReportRow } from "../../api/reports";
import { ExportButton, Pager } from "./shared";
import { formatDate, formatMoney, styles } from "./reportUtils";
import { useI18n } from "../../i18n/useI18n";

export default function CustomerDebtReportTab() {
  const { t } = useI18n();
  const [draftSearch, setDraftSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [page, setPage] = useState(1);

  const [rows, setRows] = useState<CustomerDebtReportRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getCustomerDebtReport({
          search: appliedSearch.trim() || undefined,
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
  }, [appliedSearch, page]);

  function handleApply() {
    setPage(1);
    setAppliedSearch(draftSearch);
  }

  function handleReset() {
    setDraftSearch("");
    setAppliedSearch("");
    setPage(1);
  }

  return (
    <div>
      <div style={styles.card}>
        <div style={styles.filterRow}>
          <label style={styles.field}>
            {t("rep.searchCustomer")}
            <input
              type="text"
              style={{ ...styles.input, minWidth: 220 }}
              value={draftSearch}
              onChange={(e) => setDraftSearch(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleApply();
              }}
            />
          </label>
          <button style={styles.primaryButton} onClick={handleApply}>
            {t("rep.apply")}
          </button>
          <button style={styles.button} onClick={handleReset}>
            {t("rep.reset")}
          </button>
          <ExportButton onExport={() => exportCustomerDebtReport({ search: appliedSearch.trim() || undefined })} />
        </div>
      </div>

      <div style={styles.card}>
        <div style={styles.warning}>
          {t("rep.note.debt")}
        </div>
        {error && <p style={styles.error}>{t(error)}</p>}
        {loading ? (
          <p style={styles.muted}>{t("rep.loading")}</p>
        ) : (
          <>
            <div style={{ overflowX: "auto" }}>
              <table style={styles.table}>
                <thead>
                  <tr>
                    <th style={styles.th}>{t("rep.th.customer")}</th>
                    <th style={styles.th}>{t("rep.th.phone")}</th>
                    <th style={styles.thRight}>{t("rep.th.debtOrders")}</th>
                    <th style={styles.thRight}>{t("rep.th.totalDebt")}</th>
                    <th style={styles.th}>{t("rep.th.lastPurchase")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={5} style={styles.empty}>
                        {t("rep.noDebtors")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.customerId}>
                      <td style={styles.td}>{r.customerName}</td>
                      <td style={styles.td}>{r.phone}</td>
                      <td style={styles.tdRight}>{r.outstandingOrderCount}</td>
                      <td style={styles.tdRight}>{formatMoney(r.totalDebt)}</td>
                      <td style={styles.td}>{formatDate(r.lastPurchaseDate)}</td>
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