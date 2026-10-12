import { useI18n } from "../../i18n/useI18n";
import Button from "./Button";

export function EmptyState({ message }: { message?: string }) {
  const { t } = useI18n();
  return <div className="px-4 py-10 text-center text-sm text-slate-500">{message ?? t("common.noData")}</div>;
}

export function ErrorState({ message, onRetry }: { message?: string; onRetry?: () => void }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col items-center gap-3 px-4 py-10 text-center">
      <p className="text-sm text-red-600">{message ?? t("common.error")}</p>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          {t("common.retry")}
        </Button>
      )}
    </div>
  );
}

export function LoadingSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-3 p-4" aria-busy="true">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="h-5 animate-pulse rounded bg-slate-100" />
      ))}
    </div>
  );
}
