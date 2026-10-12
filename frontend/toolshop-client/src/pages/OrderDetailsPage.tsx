import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import type { OrderStatus } from "../api/orders";
import { getOrder, returnOrder } from "../api/orders";
import {
  getOrderPayments,
  createPayment,
  type OrderPaymentState,
  type PaymentMethod,
  type PaymentStatus,
} from "../api/payments";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorCode, errorMessage } from "../lib/apiError";
import { formatDateTime, formatMoney, formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import KpiCard from "../components/ui/KpiCard";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import FormField from "../components/ui/FormField";
import StatusBadge from "../components/ui/StatusBadge";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const ORDER_TONE: Record<OrderStatus, "gray" | "green" | "red" | "yellow"> = {
  Draft: "gray",
  Completed: "green",
  Cancelled: "red",
  Returned: "yellow",
  PartiallyReturned: "yellow",
};
const STATE_TONE: Record<OrderPaymentState, "red" | "yellow" | "green"> = {
  Unpaid: "red",
  PartiallyPaid: "yellow",
  Paid: "green",
  Refunded: "yellow",
};
const PAY_TONE: Record<PaymentStatus, "green" | "red" | "yellow"> = {
  Completed: "green",
  Voided: "red",
  Refunded: "yellow",
};

const METHODS: PaymentMethod[] = ["Cash", "Card", "BankTransfer", "QR", "Other"];

export default function OrderDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const { t } = useI18n();
  const toast = useToast();
  const { hasRole, has } = usePermissions();
  const canAddPayment = hasRole("Admin", "Manager", "Cashier");
  const canReturn = has("Sales.Return");

  const { data, loading, error, reload } = useAsyncData(async () => {
    const orderId = Number(id);
    const [order, summary] = await Promise.all([getOrder(orderId), getOrderPayments(orderId).catch(() => null)]);
    return { order, summary };
  }, id ?? "");

  const [modalOpen, setModalOpen] = useState(false);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [reference, setReference] = useState("");
  const [modalError, setModalError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [returnOpen, setReturnOpen] = useState(false);
  const [returnReason, setReturnReason] = useState("");
  const [returnError, setReturnError] = useState<string | null>(null);
  const [returning, setReturning] = useState(false);

  if (loading) return <LoadingSkeleton rows={8} />;
  if (error || !data) return <ErrorState message={t("ord.notFound")} onRetry={reload} />;

  const { order, summary } = data;
  const outstanding = summary?.outstandingBalance ?? 0;

  function openModal() {
    setAmount(outstanding > 0 ? String(outstanding) : "");
    setMethod("Cash");
    setReference("");
    setModalError(null);
    setModalOpen(true);
  }

  async function handleAddPayment(e: FormEvent) {
    e.preventDefault();
    if (saving) return;
    setModalError(null);
    const value = Number(amount);
    if (!value || value <= 0) return setModalError(t("ord.amountInvalid"));
    if (value > outstanding) return setModalError(t("ord.amountTooBig"));

    setSaving(true);
    try {
      await createPayment(order.id, value, method, reference.trim(), "");
      toast.success(t("ord.paymentSaved"));
      setModalOpen(false);
      reload();
    } catch (err) {
      setModalError(errorMessage(err) ?? t("ord.paymentFailed"));
    } finally {
      setSaving(false);
    }
  }

  function openReturn() {
    setReturnReason("");
    setReturnError(null);
    setReturnOpen(true);
  }

  async function handleReturn(e: FormEvent) {
    e.preventDefault();
    if (returning) return;
    const reason = returnReason.trim();
    if (reason.length < 3) return setReturnError(t("ord.returnReasonRequired"));

    setReturning(true);
    setReturnError(null);
    try {
      await returnOrder(order.id, reason);
      toast.success(t("ord.returnDone"));
      setReturnOpen(false);
      reload();
    } catch (err) {
      setReturnError(
        errorCode(err) === "ORDER_NOT_RETURNABLE" ? t("ord.err.ORDER_NOT_RETURNABLE") : (errorMessage(err) ?? t("ord.returnFailed")),
      );
    } finally {
      setReturning(false);
    }
  }

  return (
    <div className="w-full">
      <PageHeader
        title={`${t("ord.details")} ${order.orderNumber}`}
        subtitle={`${order.customerId ? order.customerName : t("pos.walkIn")} · ${formatDateTime(order.createdAt)}`}
        actions={
          <>
            <StatusBadge label={t(`ord.status.${order.status}`)} tone={ORDER_TONE[order.status] ?? "gray"} />
            {canReturn && order.status === "Completed" && (
              <Button variant="danger" onClick={openReturn}>
                {t("ord.return")}
              </Button>
            )}
            <Link to="/orders">
              <Button variant="secondary">{t("common.back")}</Button>
            </Link>
          </>
        }
      />

      <Card className="mb-4 overflow-hidden !p-0">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-slate-50">
              <tr>
                <th className={thClass}>{t("ord.product")}</th>
                <th className={thClass}>SKU</th>
                <th className={`${thClass} text-right`}>{t("ord.qty")}</th>
                <th className={`${thClass} text-right`}>{t("ord.price")}</th>
                <th className={`${thClass} text-right`}>{t("ord.lineTotal")}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {order.items.map((item) => (
                <tr key={item.id}>
                  <td className={`${tdClass} font-medium text-slate-900`}>{item.productName}</td>
                  <td className={tdClass}>{item.sku}</td>
                  <td className={`${tdClass} text-right`}>{formatNumber(item.quantity)}</td>
                  <td className={`${tdClass} text-right`}>{formatMoney(item.unitPrice)}</td>
                  <td className={`${tdClass} text-right`}>{formatMoney(item.unitPrice * item.quantity)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="ml-auto max-w-xs space-y-1 border-t border-slate-100 p-4 text-sm">
          <div className="flex justify-between">
            <span className="text-slate-600">{t("ord.subtotal")}</span>
            <span>{formatMoney(order.subtotal)}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-slate-600">{t("ord.discount")}</span>
            <span>{formatMoney(order.discountAmount)}</span>
          </div>
          <div className="flex justify-between border-t border-slate-100 pt-2 text-base font-bold text-slate-900">
            <span>{t("ord.total")}</span>
            <span>{formatMoney(order.totalAmount)}</span>
          </div>
        </div>
      </Card>

      {order.status === "Returned" && (
        <Card className="mb-4 border-amber-200 bg-amber-50" title={t("ord.status.Returned")}>
          <dl className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2" data-testid="return-info">
            <div>
              <dt className="text-slate-500">{t("ord.returnedAt")}</dt>
              <dd>{order.returnedAt ? formatDateTime(order.returnedAt) : "—"}</dd>
            </div>
            <div>
              <dt className="text-slate-500">{t("ord.returnedBy")}</dt>
              <dd>{order.returnedByUsername ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-slate-500">{t("ord.refundedAmount")}</dt>
              <dd className="font-semibold">{formatMoney(order.refundedAmount ?? 0)}</dd>
            </div>
            <div>
              <dt className="text-slate-500">{t("ord.returnReason")}</dt>
              <dd>{order.returnReason ?? "—"}</dd>
            </div>
          </dl>
        </Card>
      )}

      <h2 className="mb-2 text-base font-semibold text-slate-800">{t("ord.payments")}</h2>
      {summary && (
        <div className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <KpiCard label={t("ord.orderTotal")} value={formatMoney(summary.orderTotal)} />
          <KpiCard label={t("ord.paid")} value={formatMoney(summary.totalPaid)} tone="positive" />
          <KpiCard
            label={t("ord.outstanding")}
            value={formatMoney(summary.outstandingBalance)}
            tone={summary.outstandingBalance > 0 ? "negative" : "default"}
          />
          <KpiCard
            label={t("ord.payState")}
            value={
              <StatusBadge label={t(`pay.state.${summary.paymentState}`)} tone={STATE_TONE[summary.paymentState]} />
            }
          />
        </div>
      )}

      <Card className="overflow-hidden !p-0">
        {!summary || summary.payments.length === 0 ? (
          <EmptyState message={t("ord.noPayments")} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>№</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={`${thClass} text-right`}>{t("common.amount")}</th>
                  <th className={thClass}>{t("common.method")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {summary.payments.map((p) => (
                  <tr key={p.id}>
                    <td className={tdClass}>{p.paymentNumber}</td>
                    <td className={tdClass}>{formatDateTime(p.createdAt)}</td>
                    <td className={`${tdClass} text-right font-medium`}>{formatMoney(p.amount)}</td>
                    <td className={tdClass}>{t(`pay.method.${p.method}`)}</td>
                    <td className={tdClass}>
                      <StatusBadge label={t(`pay.status.${p.status}`)} tone={PAY_TONE[p.status] ?? "gray"} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {canAddPayment && outstanding > 0 && order.status === "Completed" && (
          <div className="border-t border-slate-100 p-4">
            <Button onClick={openModal}>+ {t("ord.addPayment")}</Button>
          </div>
        )}
      </Card>

      <Modal
        open={returnOpen}
        title={t("ord.returnTitle")}
        onClose={() => !returning && setReturnOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setReturnOpen(false)} disabled={returning}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" form="order-return-form" variant="danger" loading={returning}>
              {t("ord.return")}
            </Button>
          </>
        }
      >
        <form id="order-return-form" onSubmit={handleReturn} className="space-y-4">
          <p className="text-sm text-slate-600">{t("ord.returnNote")}</p>
          <p className="text-sm font-medium text-slate-800">
            {t("ord.refundedAmount")}: {formatMoney(summary?.totalPaid ?? 0)}
          </p>
          <FormField label={t("ord.returnReason")} required hint={t("ord.returnReasonHint")}>
            <textarea
              className={inputClass}
              rows={3}
              maxLength={500}
              value={returnReason}
              onChange={(e) => setReturnReason(e.target.value)}
              autoFocus
            />
          </FormField>
          {returnError && (
            <p className="text-sm text-red-600" role="alert">
              {returnError}
            </p>
          )}
        </form>
      </Modal>

      <Modal
        open={modalOpen}
        title={t("ord.addPayment")}
        onClose={() => !saving && setModalOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setModalOpen(false)} disabled={saving}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" form="order-payment-form" loading={saving}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <form id="order-payment-form" onSubmit={handleAddPayment} className="space-y-4">
          <FormField label={`${t("ord.amountUpTo")} ${formatMoney(outstanding)})`} required>
            <input
              type="number"
              min="0"
              step="0.01"
              className={inputClass}
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              autoFocus
            />
          </FormField>
          <FormField label={t("ord.method")}>
            <select className={inputClass} value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
              {METHODS.map((m) => (
                <option key={m} value={m}>
                  {t(`pay.method.${m}`)}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t("ord.reference")}>
            <input className={inputClass} value={reference} onChange={(e) => setReference(e.target.value)} />
          </FormField>
          {modalError && (
            <p className="text-sm text-red-600" role="alert">
              {modalError}
            </p>
          )}
        </form>
      </Modal>
    </div>
  );
}
