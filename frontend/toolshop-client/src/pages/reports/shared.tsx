import { useState, type ReactNode } from "react";
import { styles } from "./reportUtils";
import Pagination from "../../components/ui/Pagination";
import Button from "../../components/ui/Button";
import { useI18n } from "../../i18n/useI18n";

interface DateRangeFieldsProps {
  from: string;
  to: string;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
}

export function DateRangeFields({ from, to, onFromChange, onToChange }: DateRangeFieldsProps) {
  const { t } = useI18n();
  return (
    <>
      <label style={styles.field}>
        {t("rep.from")}
        <input type="date" style={styles.input} value={from} onChange={(e) => onFromChange(e.target.value)} />
      </label>
      <label style={styles.field}>
        {t("rep.to")}
        <input type="date" style={styles.input} value={to} onChange={(e) => onToChange(e.target.value)} />
      </label>
    </>
  );
}

interface PagerProps {
  page: number;
  totalPages: number;
  totalItems: number;
  onPage: (p: number) => void;
}

export function Pager({ page, totalPages, totalItems, onPage }: PagerProps) {
  return <Pagination page={page} totalPages={totalPages} totalItems={totalItems} onChange={onPage} />;
}

export function ExportButton({ onExport }: { onExport: () => Promise<void> }) {
  const { t } = useI18n();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleClick() {
    try {
      setBusy(true);
      setError(null);
      await onExport();
    } catch (err) {
      console.error(err);
      setError("rep.exportError");
    } finally {
      setBusy(false);
    }
  }

  return (
    <span>
      <button style={{ ...styles.button, opacity: busy ? 0.6 : 1 }} disabled={busy} onClick={handleClick}>
        {busy ? t("rep.exporting") : "⬇ CSV"}
      </button>
      {error && <span style={{ ...styles.error, marginLeft: 8 }}>{t(error)}</span>}
    </span>
  );
}


/** Compact filter bar: main filters always visible, the rest behind "More filters". */
export function FilterBar({
  children,
  more,
  onApply,
  onReset,
  exportNode,
}: {
  children: ReactNode;
  more?: ReactNode;
  onApply: () => void;
  onReset: () => void;
  exportNode?: ReactNode;
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  return (
    <div>
      <div style={styles.filterRow}>
        {children}
        {more && (
          <Button variant="secondary" size="sm" aria-expanded={open} onClick={() => setOpen((v) => !v)}>
            {open ? t("rep.lessFilters") : t("rep.moreFilters")}
          </Button>
        )}
        <Button size="sm" onClick={onApply}>
          {t("rep.apply")}
        </Button>
        <Button variant="secondary" size="sm" onClick={onReset}>
          {t("rep.reset")}
        </Button>
        {exportNode}
      </div>
      {more && open && <div style={{ ...styles.filterRow, marginTop: 12 }}>{more}</div>}
    </div>
  );
}
