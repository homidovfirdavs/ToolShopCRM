import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Wrench } from "lucide-react";
import { useAuth } from "../context/AuthContext";
import type { MfaChallenge } from "../api/mfa";
import { errorStatus } from "../lib/apiError";
import { MfaSetupPanel, MfaVerifyPanel, RecoveryCodesPanel } from "../components/auth/MfaPanels";
import { useI18n } from "../i18n/useI18n";
import { LANGS } from "../i18n/translations";
import Button from "../components/ui/Button";
import FormField from "../components/ui/FormField";
import { inputClass } from "../components/ui/formStyles";

export default function LoginPage() {
  const { t, lang, setLang } = useI18n();
  const [usernameOrEmail, setUsernameOrEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [challenge, setChallenge] = useState<MfaChallenge | null>(null);
  const [mfaError, setMfaError] = useState<string | null>(null);
  const [recovery, setRecovery] = useState<{ codes: string[]; finish: () => void } | null>(null);

  const { login, verifyMfa, enableMfa } = useAuth();
  const navigate = useNavigate();

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (isLoading) return;
    setError(null);

    if (!usernameOrEmail.trim() || !password) {
      setError(t("login.errEmpty"));
      return;
    }

    setIsLoading(true);
    try {
      const next = await login(usernameOrEmail, password);
      if (next) {
        setChallenge(next);
        setMfaError(null);
        setPassword("");
        return;
      }
      navigate("/");
    } catch {
      setError(t("login.errInvalid"));
    } finally {
      setIsLoading(false);
    }
  }

  function mfaMessage(err: unknown): string {
    return errorStatus(err) === 429 ? t("login.mfa.errTooMany") : t("login.mfa.errCode");
  }

  function backToCredentials() {
    setChallenge(null);
    setRecovery(null);
    setMfaError(null);
  }

  async function handleVerify(input: { code?: string; recoveryCode?: string }) {
    if (!challenge || isLoading) return;
    setIsLoading(true);
    setMfaError(null);
    try {
      await verifyMfa(challenge.mfaToken, input);
      navigate("/");
    } catch (err) {
      setMfaError(mfaMessage(err));
    } finally {
      setIsLoading(false);
    }
  }

  async function handleEnable(code: string) {
    if (!challenge || isLoading) return;
    setIsLoading(true);
    setMfaError(null);
    try {
      const result = await enableMfa(challenge.mfaToken, code);
      setRecovery({ codes: result.recoveryCodes, finish: result.finish });
    } catch (err) {
      setMfaError(mfaMessage(err));
    } finally {
      setIsLoading(false);
    }
  }

  function finishRecovery() {
    recovery?.finish();
    navigate("/");
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-100 p-4">
      <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-8 shadow-md">
        <div className="mb-6 flex justify-end">
          <div className="flex rounded-lg border border-slate-200 p-0.5" role="group" aria-label="Language">
            {LANGS.map((l) => (
              <button
                key={l.code}
                type="button"
                onClick={() => setLang(l.code)}
                aria-pressed={lang === l.code}
                className={`rounded-md px-2 py-1 text-xs font-semibold ${
                  lang === l.code ? "bg-blue-600 text-white" : "text-slate-600 hover:bg-slate-100"
                }`}
              >
                {l.label}
              </button>
            ))}
          </div>
        </div>

        <div className="mb-6 flex flex-col items-center gap-2">
          <div className="rounded-xl bg-blue-600 p-3 text-white">
            <Wrench size={24} />
          </div>
          <h1 className="text-2xl font-bold text-slate-900">{t("login.title")}</h1>
        </div>

        {recovery ? (
          <RecoveryCodesPanel codes={recovery.codes} onContinue={finishRecovery} />
        ) : challenge?.setupRequired ? (
          <MfaSetupPanel
            mfaToken={challenge.mfaToken}
            busy={isLoading}
            error={mfaError}
            onSubmit={(c) => void handleEnable(c)}
            onBack={backToCredentials}
          />
        ) : challenge ? (
          <MfaVerifyPanel
            busy={isLoading}
            error={mfaError}
            onSubmit={(i) => void handleVerify(i)}
            onBack={backToCredentials}
          />
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            <FormField label={t("login.username")}>
              <input
                type="text"
                className={inputClass}
                value={usernameOrEmail}
                onChange={(e) => setUsernameOrEmail(e.target.value)}
                autoComplete="username"
                autoFocus
              />
            </FormField>

            <FormField label={t("login.password")}>
              <div className="relative">
                <input
                  type={showPassword ? "text" : "password"}
                  className={`${inputClass} pr-20`}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  autoComplete="current-password"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-xs font-medium text-slate-500 hover:text-slate-700"
                >
                  {showPassword ? t("login.hide") : t("login.show")}
                </button>
              </div>
            </FormField>

            {error && (
              <p className="text-sm text-red-600" role="alert">
                {error}
              </p>
            )}

            <Button type="submit" loading={isLoading} className="w-full !py-2.5">
              {isLoading ? t("login.submitting") : t("login.submit")}
            </Button>
          </form>
        )}
      </div>
    </div>
  );
}
