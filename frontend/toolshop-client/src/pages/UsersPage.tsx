import { useState } from "react";
import {
  activateUser,
  createUser,
  deactivateUser,
  getUsers,
  resetUserMfa,
  resetUserPassword,
  updateUser,
  USER_ROLES,
  type ManagedUser,
  type UserRole,
} from "../api/users";
import { useAuth } from "../context/AuthContext";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorCode, errorMessage } from "../lib/apiError";
import { formatDate } from "../lib/format";
import { isStrongPassword } from "../lib/password";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import FormField from "../components/ui/FormField";
import ConfirmDialog from "../components/ui/ConfirmDialog";
import StatusBadge from "../components/ui/StatusBadge";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const KNOWN_ERRORS = [
  "USERNAME_TAKEN",
  "EMAIL_TAKEN",
  "CANNOT_DEACTIVATE_SELF",
  "CANNOT_CHANGE_OWN_ROLE",
  "LAST_ADMIN",
  "INVALID_ROLE",
  "CANNOT_RESET_OWN_MFA",
];

type Dialog =
  | { kind: "create" }
  | { kind: "edit"; user: ManagedUser }
  | { kind: "reset"; user: ManagedUser }
  | { kind: "deactivate"; user: ManagedUser }
  | { kind: "mfaReset"; user: ManagedUser }
  | null;

export default function UsersPage() {
  const { t } = useI18n();
  const { user: me } = useAuth();
  const toast = useToast();
  const list = useAsyncData(() => getUsers(), "users");

  const [dialog, setDialog] = useState<Dialog>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // form state (create / edit / reset)
  const [username, setUsername] = useState("");
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState<UserRole>("Cashier");

  function open(d: Dialog) {
    setFormError(null);
    setPassword("");
    if (d?.kind === "create") {
      setUsername("");
      setFullName("");
      setEmail("");
      setRole("Cashier");
    } else if (d?.kind === "edit") {
      setFullName(d.user.fullName);
      setEmail(d.user.email);
      setRole((USER_ROLES as string[]).includes(d.user.role) ? (d.user.role as UserRole) : "Cashier");
    }
    setDialog(d);
  }

  function close() {
    if (!busy) setDialog(null);
  }

  function messageFor(err: unknown): string {
    const code = errorCode(err);
    if (code && KNOWN_ERRORS.includes(code)) return t(`users.err.${code}`);
    return errorMessage(err) ?? t("common.saveFailed");
  }

  async function run(action: () => Promise<unknown>, success: string) {
    setBusy(true);
    setFormError(null);
    try {
      await action();
      toast.success(success);
      setDialog(null);
      list.reload();
    } catch (err) {
      setFormError(messageFor(err));
    } finally {
      setBusy(false);
    }
  }

  function submitCreate() {
    if (!username.trim() || !fullName.trim() || !email.trim() || !password) {
      setFormError(t("users.validation.required"));
      return;
    }
    if (!isStrongPassword(password)) {
      setFormError(t("users.validation.password"));
      return;
    }
    void run(
      () => createUser({ username: username.trim(), fullName: fullName.trim(), email: email.trim(), password, role }),
      t("users.created"),
    );
  }

  function submitEdit() {
    if (dialog?.kind !== "edit") return;
    if (!fullName.trim() || !email.trim()) {
      setFormError(t("users.validation.required"));
      return;
    }
    void run(() => updateUser(dialog.user.id, { fullName: fullName.trim(), email: email.trim(), role }), t("users.updated"));
  }

  function submitReset() {
    if (dialog?.kind !== "reset") return;
    if (!isStrongPassword(password)) {
      setFormError(t("users.validation.password"));
      return;
    }
    void run(() => resetUserPassword(dialog.user.id, password), t("users.passwordResetDone"));
  }

  function submitDeactivate() {
    if (dialog?.kind !== "deactivate") return;
    void run(() => deactivateUser(dialog.user.id), t("common.deactivated"));
  }

  function submitMfaReset() {
    if (dialog?.kind !== "mfaReset") return;
    void run(() => resetUserMfa(dialog.user.id), t("users.mfaResetDone"));
  }

  async function handleActivate(u: ManagedUser) {
    try {
      await activateUser(u.id);
      toast.success(t("common.activated"));
      list.reload();
    } catch (err) {
      toast.error(messageFor(err));
    }
  }

  const users = list.data;
  const roleLabel = (r: string) => (USER_ROLES as string[]).includes(r) ? t(`users.role.${r}`) : r;

  const roleSelect = (
    <select className={inputClass} value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
      {USER_ROLES.map((r) => (
        <option key={r} value={r}>
          {t(`users.role.${r}`)}
        </option>
      ))}
    </select>
  );

  return (
    <div className="w-full">
      <PageHeader title={t("nav.users")} actions={<Button onClick={() => open({ kind: "create" })}>+ {t("users.add")}</Button>} />

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && users && users.length === 0 && <EmptyState message={t("users.empty")} />}
        {!list.loading && !list.error && users && users.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("users.username")}</th>
                  <th className={thClass}>{t("users.fullName")}</th>
                  <th className={thClass}>{t("users.email")}</th>
                  <th className={thClass}>{t("users.role")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={thClass}>{t("users.mfa")}</th>
                  <th className={thClass}>{t("common.created")}</th>
                  <th className={thClass}>{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {users.map((u) => {
                  const isMe = me?.id === u.id;
                  return (
                    <tr key={u.id} className="hover:bg-slate-50" data-testid={`user-row-${u.username}`}>
                      <td className={`${tdClass} font-medium`}>
                        {u.username}
                        {isMe && <span className="ml-2 text-xs text-slate-400">({t("users.you")})</span>}
                      </td>
                      <td className={tdClass}>{u.fullName}</td>
                      <td className={tdClass}>{u.email}</td>
                      <td className={tdClass}>
                        <StatusBadge label={roleLabel(u.role)} tone="blue" />
                      </td>
                      <td className={tdClass}>
                        <div className="flex flex-wrap gap-1">
                          <StatusBadge
                            label={u.isActive ? t("common.active") : t("common.inactive")}
                            tone={u.isActive ? "green" : "gray"}
                          />
                          {u.isLockedOut && <StatusBadge label={t("users.lockedOut")} tone="red" />}
                        </div>
                      </td>
                      <td className={tdClass}>
                        <StatusBadge
                          label={u.mfaEnabled ? t("users.mfaOn") : t("users.mfaOff")}
                          tone={u.mfaEnabled ? "green" : "gray"}
                        />
                      </td>
                      <td className={tdClass}>{formatDate(u.createdAt)}</td>
                      <td className={tdClass}>
                        <div className="flex flex-wrap gap-2">
                          <Button size="sm" variant="secondary" onClick={() => open({ kind: "edit", user: u })}>
                            {t("common.edit")}
                          </Button>
                          <Button size="sm" variant="secondary" onClick={() => open({ kind: "reset", user: u })}>
                            {t("users.resetPassword")}
                          </Button>
                          {u.mfaEnabled && !isMe && (
                            <Button size="sm" variant="secondary" onClick={() => open({ kind: "mfaReset", user: u })}>
                              {t("users.mfaReset")}
                            </Button>
                          )}
                          {u.isActive && !isMe && (
                            <Button
                              size="sm"
                              variant="ghost"
                              className="!text-red-600"
                              onClick={() => open({ kind: "deactivate", user: u })}
                            >
                              {t("common.deactivate")}
                            </Button>
                          )}
                          {!u.isActive && (
                            <Button size="sm" variant="ghost" className="!text-green-700" onClick={() => handleActivate(u)}>
                              {t("common.activate")}
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* create */}
      <Modal
        open={dialog?.kind === "create"}
        title={t("users.createTitle")}
        onClose={close}
        footer={
          <>
            <Button variant="secondary" onClick={close} disabled={busy}>
              {t("common.cancel")}
            </Button>
            <Button onClick={submitCreate} loading={busy}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <FormField label={t("users.username")} required>
            <input className={inputClass} value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" />
          </FormField>
          <FormField label={t("users.fullName")} required>
            <input className={inputClass} value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </FormField>
          <FormField label={t("users.email")} required>
            <input className={inputClass} type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="off" />
          </FormField>
          <FormField label={t("users.role")} required>
            {roleSelect}
          </FormField>
          <FormField label={t("users.password")} required hint={t("users.passwordHint")}>
            <input
              className={inputClass}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
            />
          </FormField>
          {formError && <p role="alert" className="text-sm text-red-600">{formError}</p>}
        </div>
      </Modal>

      {/* edit */}
      <Modal
        open={dialog?.kind === "edit"}
        title={t("users.editTitle")}
        onClose={close}
        footer={
          <>
            <Button variant="secondary" onClick={close} disabled={busy}>
              {t("common.cancel")}
            </Button>
            <Button onClick={submitEdit} loading={busy}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <FormField label={t("users.fullName")} required>
            <input className={inputClass} value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </FormField>
          <FormField label={t("users.email")} required>
            <input className={inputClass} type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </FormField>
          <FormField label={t("users.role")} required hint={t("users.roleChangeNote")}>
            {roleSelect}
          </FormField>
          {formError && <p role="alert" className="text-sm text-red-600">{formError}</p>}
        </div>
      </Modal>

      {/* reset password */}
      <Modal
        open={dialog?.kind === "reset"}
        title={t("users.resetPassword")}
        onClose={close}
        footer={
          <>
            <Button variant="secondary" onClick={close} disabled={busy}>
              {t("common.cancel")}
            </Button>
            <Button onClick={submitReset} loading={busy}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            {dialog?.kind === "reset" ? dialog.user.username : ""} — {t("users.resetPasswordNote")}
          </p>
          <FormField label={t("users.newPassword")} required hint={t("users.passwordHint")}>
            <input
              className={inputClass}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
            />
          </FormField>
          {formError && <p role="alert" className="text-sm text-red-600">{formError}</p>}
        </div>
      </Modal>

      <ConfirmDialog
        open={dialog?.kind === "mfaReset"}
        title={t("users.mfaReset")}
        message={
          <>
            <p>{dialog?.kind === "mfaReset" ? dialog.user.username : ""}</p>
            <p className="mt-1">{t("users.mfaResetConfirm")}</p>
            {formError && <p role="alert" className="mt-2 text-red-600">{formError}</p>}
          </>
        }
        confirmLabel={t("users.mfaReset")}
        danger
        loading={busy}
        onConfirm={submitMfaReset}
        onCancel={close}
      />

      <ConfirmDialog
        open={dialog?.kind === "deactivate"}
        title={t("common.deactivate")}
        message={
          <>
            <p>{dialog?.kind === "deactivate" ? dialog.user.username : ""}</p>
            <p className="mt-1">{t("users.deactivateConfirm")}</p>
            {formError && <p role="alert" className="mt-2 text-red-600">{formError}</p>}
          </>
        }
        confirmLabel={t("common.deactivate")}
        danger
        loading={busy}
        onConfirm={submitDeactivate}
        onCancel={close}
      />
    </div>
  );
}
