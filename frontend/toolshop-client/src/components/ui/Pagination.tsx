import { useI18n } from "../../i18n/useI18n";
import Button from "./Button";

export default function Pagination({
  page,
  totalPages,
  totalItems,
  onChange,
}: {
  page: number;
  totalPages: number;
  totalItems?: number;
  onChange: (page: number) => void;
}) {
  const { t } = useI18n();
  if (totalPages <= 1) return null;
  return (
    <div className="mt-4 flex flex-wrap items-center justify-between gap-2 text-sm text-slate-600">
      <span>
        {t("common.page")} {page} {t("common.of")} {totalPages}
        {totalItems !== undefined && ` (${totalItems} ${t("common.items")})`}
      </span>
      <div className="flex gap-2">
        <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)}>
          {t("common.previous")}
        </Button>
        <Button variant="secondary" size="sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
          {t("common.next")}
        </Button>
      </div>
    </div>
  );
}
