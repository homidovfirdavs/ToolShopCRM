import { useState } from "react";
import { Download } from "lucide-react";
import { useI18n } from "../../i18n/useI18n";
import { useInstallPrompt } from "../../pwa/useInstallPrompt";
import Button from "../ui/Button";
import Modal from "../ui/Modal";

/**
 * Install entry point.
 *  - Chrome/Edge on Windows, macOS and Android: opens the browser's install dialog.
 *  - iOS/iPadOS Safari (no install API): shows the "Add to Home Screen" steps.
 * Hidden when the app is already installed or the browser offers no install path.
 */
export default function InstallButton() {
  const { t } = useI18n();
  const { canPrompt, showIosHint, promptInstall } = useInstallPrompt();
  const [iosOpen, setIosOpen] = useState(false);

  if (!canPrompt && !showIosHint) return null;

  return (
    <>
      <button
        type="button"
        data-testid="install-button"
        aria-label={t("pwa.install")}
        onClick={() => (canPrompt ? void promptInstall() : setIosOpen(true))}
        className="flex min-h-11 items-center gap-1 rounded-lg px-2 py-1.5 text-sm text-slate-600 hover:bg-slate-100"
      >
        <Download size={16} aria-hidden="true" />
        <span className="hidden md:inline">{t("pwa.install")}</span>
      </button>
      <Modal
        open={iosOpen}
        title={t("pwa.iosTitle")}
        onClose={() => setIosOpen(false)}
        footer={<Button onClick={() => setIosOpen(false)}>{t("pwa.gotIt")}</Button>}
      >
        <ol className="list-decimal space-y-2 pl-5 text-sm text-slate-700">
          <li>{t("pwa.iosStep1")}</li>
          <li>{t("pwa.iosStep2")}</li>
          <li>{t("pwa.iosStep3")}</li>
        </ol>
      </Modal>
    </>
  );
}
