import { useEffect, useState, type FormEvent } from "react";
import { QRCodeSVG } from "qrcode.react";
import { startMfaSetup, type MfaSetupData } from "../../api/mfa";
import { useI18n } from "../../i18n/useI18n";
import Button from "../ui/Button";
import FormField from "../ui/FormField";
import { inputClass } from "../ui/formStyles";

const digitsOnly = (v: string) => v.replace(/\D/g, "").slice(0, 6);

/** Step 2 of the login for users who already have an authenticator: 6-digit code or a recovery code. */
export function MfaVerifyPanel({
  busy,
  error,
  onSubmit,
  onBack,
}: {
  busy: boolean;
  error: string | null;
  onSubmit: (input: { code?: string; recoveryCode?: string }) => void;
  onBack: () => void;
}) {
  const { t } = useI18n();
  const [useRecovery, setUseRecovery] = useState(false);
  const [value, setValue] = useState("");

  function handle(e: FormEvent) {
    e.preventDefault();
    const v = value.trim();
    if (!v || busy) return;
    onSubmit(useRecovery ? { recoveryCode: v } : { code: v });
  }

  return (
    <form onSubmit={handle} className="space-y-4" data-testid="mfa-verify">
      <h2 className="text-lg font-semibold text-slate-900">{t("login.mfa.title")}</h2>
      <p className="text-sm text-slate-600">{useRecovery ? t("login.mfa.recoveryHint") : t("login.mfa.verifyHint")}</p>
      <FormField label={useRecovery ? t("login.mfa.recoveryCode") : t("login.mfa.code")}>
        <input
          className={inputClass}
          value={value}
          onChange={(e) => setValue(useRecovery ? e.target.value : digitsOnly(e.target.value))}
          inputMode={useRecovery ? "text" : "numeric"}
          autoComplete="one-time-code"
          maxLength={useRecovery ? 32 : 6}
          autoFocus
        />
      </FormField>
      {error && (
        <p className="text-sm text-red-600" role="alert">
          {error}
        </p>
      )}
      <Button type="submit" loading={busy} className="w-full !py-2.5">
        {t("login.mfa.verify")}
      </Button>
      <div className="flex items-center justify-between text-sm">
        <button
          type="button"
          className="text-blue-600 hover:underline"
          onClick={() => {
            setUseRecovery(!useRecovery);
            setValue("");
          }}
        >
          {useRecovery ? t("login.mfa.useCode") : t("login.mfa.useRecovery")}
        </button>
        <button type="button" className="text-slate-500 hover:underline" onClick={onBack}>
          {t("login.mfa.back")}
        </button>
      </div>
    </form>
  );
}

/** First-time setup: QR code (with the key as text), then the first code from the app. */
export function MfaSetupPanel({
  mfaToken,
  busy,
  error,
  onSubmit,
  onBack,
}: {
  mfaToken: string;
  busy: boolean;
  error: string | null;
  onSubmit: (code: string) => void;
  onBack: () => void;
}) {
  const { t } = useI18n();
  const [setup, setSetup] = useState<MfaSetupData | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [code, setCode] = useState("");

  useEffect(() => {
    let cancelled = false;
    startMfaSetup(mfaToken)
      .then((d) => {
        if (!cancelled) setSetup(d);
      })
      .catch(() => {
        if (!cancelled) setLoadFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, [mfaToken]);

  function handle(e: FormEvent) {
    e.preventDefault();
    if (code.length < 6 || busy) return;
    onSubmit(code);
  }

  return (
    <form onSubmit={handle} className="space-y-4" data-testid="mfa-setup">
      <h2 className="text-lg font-semibold text-slate-900">{t("login.mfa.setupTitle")}</h2>
      <p className="text-sm text-slate-600">{t("login.mfa.setupHint")}</p>

      {loadFailed && (
        <p className="text-sm text-red-600" role="alert">
          {t("login.mfa.setupFailed")}
        </p>
      )}

      {setup && (
        <>
          <div className="flex justify-center rounded-lg border border-slate-200 bg-white p-3">
            <QRCodeSVG value={setup.otpAuthUri} size={176} />
          </div>
          <div>
            <p className="text-xs text-slate-500">{t("login.mfa.manualKey")}</p>
            <p className="mt-1 break-all rounded bg-slate-100 px-2 py-1 font-mono text-sm" data-testid="mfa-shared-key">
              {setup.sharedKey}
            </p>
          </div>
          <FormField label={t("login.mfa.firstCode")}>
            <input
              className={inputClass}
              value={code}
              onChange={(e) => setCode(digitsOnly(e.target.value))}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
            />
          </FormField>
        </>
      )}

      {error && (
        <p className="text-sm text-red-600" role="alert">
          {error}
        </p>
      )}
      <Button type="submit" loading={busy} disabled={!setup || code.length < 6} className="w-full !py-2.5">
        {t("login.mfa.enable")}
      </Button>
      <button type="button" className="text-sm text-slate-500 hover:underline" onClick={onBack}>
        {t("login.mfa.back")}
      </button>
    </form>
  );
}

/** Recovery codes are shown once. The user has to confirm they were saved before the session starts. */
export function RecoveryCodesPanel({ codes, onContinue }: { codes: string[]; onContinue: () => void }) {
  const { t } = useI18n();
  const [saved, setSaved] = useState(false);
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(codes.join("\n"));
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <div className="space-y-4" data-testid="mfa-codes">
      <h2 className="text-lg font-semibold text-slate-900">{t("login.mfa.codesTitle")}</h2>
      <p className="text-sm text-slate-600">{t("login.mfa.codesHint")}</p>
      <ul className="grid grid-cols-2 gap-2 rounded-lg border border-slate-200 bg-slate-50 p-3 font-mono text-sm">
        {codes.map((c) => (
          <li key={c}>{c}</li>
        ))}
      </ul>
      <Button type="button" variant="secondary" size="sm" onClick={() => void copy()}>
        {copied ? t("login.mfa.copied") : t("login.mfa.copy")}
      </Button>
      <label className="flex items-start gap-2 text-sm text-slate-700">
        <input type="checkbox" className="mt-1" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
        <span>{t("login.mfa.savedConfirm")}</span>
      </label>
      <Button type="button" className="w-full !py-2.5" disabled={!saved} onClick={onContinue}>
        {t("login.mfa.continue")}
      </Button>
    </div>
  );
}
