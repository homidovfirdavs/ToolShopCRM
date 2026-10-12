import { useState } from "react";
import { Link } from "react-router-dom";
import { MoreVertical } from "lucide-react";
import { getPayments, voidPayment, type PaymentListItem, type PaymentStatus } from "../api/payments";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorMessage } from "../lib/apiError";
import { formatDateTime, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import FormField from "../components/ui/FormField";
import StatusBadge from "../components/ui/StatusBadge";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const TONE: Record<PaymentStatus, "green" | "red" | "yellow"> = {
  Completed: "green",
  Voided: "red",
  Refunded: "yellow",
};

interface Filters {
  method: string;
  status: string;
  page: number;
}

export default function PaymentsPage() {
  const { t } = useI18n();
  const toast = useToast();
  const { hasRole } = usePermissions();
  const canVoid = hasRole("Admin", "Manager");

  const [filters, setFilters] = useState<Filters>({ method: "", status: "", page: 1 });
  const list = useAsyncData(
    () =>
      getPayments({
        page: filters.page,
        pageSize: 20,
        method: filters.method || undefined,
        status: filters.status || undefined,
      }),
    JSON.stringify(filters),
  );

  const [menuId, setMenuId] = useState<number | null>(null);
  const [voiding, setVoiding] = useState<PaymentListItem | null>(null);
  const [reason, setReason] = useState("");
  const [voidError, setVoidError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  function startVoid(p: PaymentListItem) {
    setMenuId(null);
    setVoiding(p);
    setReason("");
    setVoidError(null);
  }

  async function confirmVoid() {
    if (!voiding || busy) return;
    if (!reason.trim()) return setVoidError(t("pay.reasonRequired"));
    setBusy(true);
    try {
      await voidPayment(voiding.id, reason.trim());
      toast.success(t("pay.voidedOk"));
      setVoiding(null);
      list.reload();
    } catch (err) {
      setVoidError(errorMessage(err) ?? t("pay.voidFailed"));
    } finally {
      setBusy(false);
    }
  }

  const data = list.data;
  const methods = ["Cash", "Card", "BankTransfer", "QR", "Other"];

  return (
    <div className="w-full">
      <PageHeader title={t("pay.title")} />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <select
            className={inputClass}
            value={filters.method}
            onChange={(e) => setFilters((f) => ({ ...f, method: e.target.value, page: 1 }))}
          >
            <option value="">{t("pay.allMethods")}</option>
            {methods.map((m) => (
              <option key={m} value={m}>
                {t(`pay.method.${m}`)}
              </option>
            ))}
          </select>
          <select
            className={inputClass}
            value={filters.status}
            onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value, page: 1 }))}
          >
            <option value="">{t("pay.allStatuses")}</option>
            {(["Completed", "Voided", "Refunded"] as PaymentStatus[]).map((s) => (
              <option key={s} value={s}>
                {t(`pay.status.${s}`)}
              </option>
            ))}
          </select>
        </div>
      </Card>

      <Card className="!overflow-visible !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("pay.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("pay.number")}</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={thClass}>{t("pay.order")}</th>
                  <th className={thClass}>{t("pay.customer")}</th>
                  <th className={`${thClass} text-right`}>{t("common.amount")}</th>
                  <th className={thClass}>{t("common.method")}</th>
                  <th className={thClass}>{t("pay.receivedBy")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={thClass}></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50">
                    <td className={`${tdClass} font-medium text-slate-900`}>{p.paymentNumber}</td>
                    <td className={tdClass}>{formatDateTime(p.createdAt)}</td>
                    <td className={tdClass}>{p.orderNumber}</td>
                    <td className={tdClass}>{!p.customerName || p.customerName === "Walk-in Customer" ? t("pos.walkIn") : p.customerName}</td>
                    <td className={`${tdClass} text-right font-medium`}>{formatMoney(p.amount)}</td>
                    <td className={tdClass}>{t(`pay.method.${p.method}`)}</td>
                    <td className={tdClass}>{p.receivedByUsername}</td>
                    <td className={tdClass}>
                      <StatusBadge label={t(`pay.status.${p.status}`)} tone={TONE[p.status] ?? "gray"} />
                    </td>
                    <td className={`${tdClass} relative text-right`}>
                      <button
                        type="button"
                        aria-label={t("pay.actions")}
                        onClick={() => setMenuId(menuId === p.id ? null : p.id)}
                        className="rounded-lg p-1.5 text-slate-500 hover:bg-slate-100"
                      >
                        <MoreVertical size={18} />
                      </button>
                      {menuId === p.id && (
                        <>
                          <div className="fixed inset-0 z-10" onClick={() => setMenuId(null)} aria-hidden="true" />
                          <div className="absolute right-4 top-10 z-20 w-52 rounded-lg border border-slate-200 bg-white py-1 text-left shadow-lg">
                            <Link
                              to={`/orders?search=${encodeURIComponent(p.orderNumber)}`}
                              className="block px-3 py-2 text-sm text-slate-700 hover:bg-slate-50"
                            >
                              {t("pay.viewOrder")}
                            </Link>
                            {canVoid && p.status === "Completed" && (
                              <button
                                type="button"
                                onClick={() => startVoid(p)}
                                className="block w-full px-3 py-2 text-left text-sm text-red-600 hover:bg-red-50"
                              >
                                {t("pay.void")}
                              </button>
                            )}
                          </div>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {data && (
        <Pagination
          page={filters.page}
          totalPages={data.totalPages}
          totalItems={data.totalItems}
          onChange={(page) => setFilters((f) => ({ ...f, page }))}
        />
      )}

      <Modal
        open={voiding !== null}
        title={t("pay.void")}
        onClose={() => !busy && setVoiding(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setVoiding(null)} disabled={busy}>
              {t("common.cancel")}
            </Button>
            <Button variant="danger" onClick={confirmVoid} loading={busy}>
              {t("pay.void")}
            </Button>
          </>
        }
      >
        {voiding && (
          <div className="space-y-4">
            <p className="text-sm text-slate-700">
              <span className="font-medium text-slate-900">
                {voiding.paymentNumber} · {formatMoney(voiding.amount)}
              </span>
            </p>
            <p className="rounded-lg bg-amber-50 p-3 text-sm text-amber-800">
              {t("pay.voidConsequence")} {formatMoney(voiding.amount)}. {t("pay.voidKeep")}
            </p>
            <FormField label={t("pay.reason")} required>
              <input className={inputClass} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
            </FormField>
            {voidError && (
              <p className="text-sm text-red-600" role="alert">
                {voidError}
              </p>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
}
