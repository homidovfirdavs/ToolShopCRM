import { WifiOff } from "lucide-react";
import { useI18n } from "../../i18n/useI18n";
import { useOnline } from "../../pwa/useOnline";

/** Shown while the browser is offline. Write actions are blocked in api/client.ts. */
export default function OfflineBanner() {
  const online = useOnline();
  const { t } = useI18n();
  if (online) return null;
  return (
    <div
      role="alert"
      data-testid="offline-banner"
      className="flex items-start gap-2 bg-amber-100 px-4 py-2 text-sm text-amber-900"
    >
      <WifiOff size={18} className="mt-0.5 shrink-0" aria-hidden="true" />
      <span>{t("offline.banner")}</span>
    </div>
  );
}
