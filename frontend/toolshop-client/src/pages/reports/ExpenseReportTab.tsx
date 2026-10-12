import { useEffect, useState } from "react";
import { getExpenseCategories, type ExpenseCategory } from "../../api/finance";
import { getExpenseReport, exportExpenseReport, type ExpenseReportRow } from "../../api/reports";
import { DateRangeFields, ExportButton, FilterBar, Pager } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { defaultRange, formatDate, formatMoney, statusLabel, styles } from "./reportUtils";

interface Filters {
  from: string;
  to: string;
  categoryId: string;
  voided: string;
  search: string;
}

function buildParams(f: Filters) {
  return {
    from: f.from || undefined,
    to: f.to || undefined,
    categoryId: f.categoryId ? Number(f.categoryId) : undefined,
    isVoided: f.voided === "" ? undefined : f.voided === "true",
    search: f.search.trim() || undefined,
  };
}

export default function ExpenseReportTab() {
  const { t } = useI18n();
  const range = defaultRange();
  const initial: Filters = { from: range.from, to: range.to, categoryId: "", voided: "", search: "" };

  const [draft, setDraft] = useState<Filters>(initial);
  const [applied, setApplied] = useState<Filters>(initial);
  const [page, setPage] = useState(1);

  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [rows, setRows] = useState<ExpenseReportRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getExpenseCategories(true).then(setCategories).catch(console.error);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getExpenseReport({ ...buildParams(applied), page, pageSize: 20 });
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
    const reset: Filters = { from: fresh.from, to: fresh.to, categoryId: "", voided: "", search: "" };
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
            <ExportButton onExport={() => exportExpenseReport(buildParams(applied))} />
          }
          more={
            <>
          <label style={styles.field}>
            {t("rep.category")}
            <select style={styles.input} value={draft.categoryId} onChange={(e) => setDraft({ ...draft, categoryId: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          <label style={styles.field}>
            {t("rep.status")}
            <select style={styles.input} value={draft.voided} onChange={(e) => setDraft({ ...draft, voided: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              <option value="false">{statusLabel(t, "Active")}</option>
              <option value="true">{statusLabel(t, "Voided")}</option>
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
            {t("rep.searchExpense")}
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
                    <th style={styles.th}>{t("rep.th.number")}</th>
                    <th style={styles.th}>{t("rep.th.date")}</th>
                    <th style={styles.th}>{t("rep.category")}</th>
                    <th style={styles.th}>{t("rep.th.description")}</th>
                    <th style={styles.thRight}>{t("rep.th.amount")}</th>
                    <th style={styles.th}>{t("rep.th.createdBy")}</th>
                    <th style={styles.th}>{t("rep.status")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={7} style={styles.empty}>
                        {t("rep.noExpenses")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.expenseId}>
                      <td style={styles.td}>{r.expenseNumber}</td>
                      <td style={styles.td}>{formatDate(r.expenseDate)}</td>
                      <td style={styles.td}>{r.categoryName}</td>
                      <td style={styles.td}>{r.description}</td>
                      <td style={styles.tdRight}>{formatMoney(r.amount)}</td>
                      <td style={styles.td}>{r.createdByUserName}</td>
                      <td style={{ ...styles.td, color: r.isVoided ? "#b91c1c" : "#15803d", fontWeight: 600 }}>
                        {statusLabel(t, r.isVoided ? "Voided" : "Active")}
                      </td>
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