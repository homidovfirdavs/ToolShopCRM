import { useSyncExternalStore } from "react";
import { applyPwaUpdate, dismissPwaUpdate, getPwaSnapshot, subscribePwa } from "../../pwa/pwaStore";
import { useI18n } from "../../i18n/useI18n";
import Button from "../ui/Button";

/** "New version available" toast. The update is applied only when the user taps Reload. */
export default function UpdatePrompt() {
  const { needRefresh } = useSyncExternalStore(subscribePwa, getPwaSnapshot, getPwaSnapshot);
  const { t } = useI18n();
  if (!needRefresh) return null;
  return (
    <div
      role="status"
      data-testid="update-prompt"
      className="fixed inset-x-3 bottom-[calc(env(safe-area-inset-bottom)+0.75rem)] z-[70] mx-auto max-w-md rounded-xl border border-slate-200 bg-white p-4 shadow-xl sm:left-auto sm:right-4 sm:mx-0"
    >
      <p className="text-sm font-semibold text-slate-900">{t("pwa.updateAvailable")}</p>
      <p className="mt-0.5 text-xs text-slate-500">{t("pwa.updateHint")}</p>
      <div className="mt-3 flex justify-end gap-2">
        <Button variant="ghost" onClick={dismissPwaUpdate}>
          {t("pwa.later")}
        </Button>
        <Button onClick={() => void applyPwaUpdate()}>{t("pwa.reload")}</Button>
      </div>
    </div>
  );
}
