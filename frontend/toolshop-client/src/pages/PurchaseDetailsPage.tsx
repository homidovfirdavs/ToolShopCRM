import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getPurchase, confirmPurchase, cancelPurchase, type PurchaseStatus } from "../api/purchases";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorMessage } from "../lib/apiError";
import { formatDate, formatDateTime, formatMoney, formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import ConfirmDialog from "../components/ui/ConfirmDialog";
import StatusBadge from "../components/ui/StatusBadge";
import { ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { tdClass, thClass } from "../components/ui/formStyles";

const TONE: Record<PurchaseStatus, "gray" | "green" | "red"> = {
  Draft: "gray",
  Confirmed: "green",
  Cancelled: "red",
};

export default function PurchaseDetailsPage() {
  const { id } = useParams();
  const { t } = useI18n();
  const toast = useToast();
  const { hasRole } = usePermissions();
  const canConfirm = hasRole("Admin", "Manager");
  const canCancel = hasRole("Admin", "Manager", "Warehouse");

  const { data: purchase, loading, error, reload } = useAsyncData(() => getPurchase(Number(id)), id ?? "");

  const [dialog, setDialog] = useState<"confirm" | "cancel" | null>(null);
  const [busy, setBusy] = useState(false);

  async function run() {
    if (!purchase || !dialog || busy) return;
    setBusy(true);
    try {
      if (dialog === "confirm") {
        await confirmPurchase(purchase.id);
        toast.success(t("pur.confirmed"));
      } else {
        await cancelPurchase(purchase.id);
        toast.success(t("pur.cancelled"));
      }
      setDialog(null);
      reload();
    } catch (err) {
      toast.error(errorMessage(err) ?? t(dialog === "confirm" ? "pur.confirmFailed" : "pur.cancelFailed"));
      setDialog(null);
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <LoadingSkeleton rows={8} />;
  if (error || !purchase) return <ErrorState message={t("pur.notFound")} onRetry={reload} />;

  const isDraft = purchase.status === "Draft";

  return (
    <div className="mx-auto max-w-4xl">
      <PageHeader
        title={purchase.purchaseNumber}
        subtitle={purchase.supplierName}
        actions={
          <>
            <StatusBadge label={t(`pur.status.${purchase.status}`)} tone={TONE[purchase.status]} />
            <Link to="/purchases">
              <Button variant="secondary">{t("common.back")}</Button>
            </Link>
          </>
        }
      />

      <Card className="mb-4">
        <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
          <div>
            <dt className="text-slate-500">{t("pur.date")}</dt>
            <dd className="font-medium text-slate-900">{formatDate(purchase.purchaseDate)}</dd>
          </div>
          <div>
            <dt className="text-slate-500">{t("pur.createdBy")}</dt>
            <dd className="font-medium text-slate-900">{purchase.createdByUsername}</dd>
          </div>
          {purchase.confirmedByUsername && (
            <>
              <div>
                <dt className="text-slate-500">{t("pur.confirmedBy")}</dt>
                <dd className="font-medium text-slate-900">{purchase.confirmedByUsername}</dd>
              </div>
              <div>
                <dt className="text-slate-500">{t("pur.confirmedAt")}</dt>
                <dd className="font-medium text-slate-900">
                  {purchase.confirmedAt ? formatDateTime(purchase.confirmedAt) : "—"}
                </dd>
              </div>
            </>
          )}
          {purchase.notes && (
            <div className="col-span-2 sm:col-span-4">
              <dt className="text-slate-500">{t("pur.notes")}</dt>
              <dd className="text-slate-900">{purchase.notes}</dd>
            </div>
          )}
        </dl>
      </Card>

      <Card className="mb-4 overflow-hidden !p-0">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-slate-50">
              <tr>
                <th className={thClass}>{t("inv.product")}</th>
                <th className={thClass}>SKU</th>
                <th className={`${thClass} text-right`}>{t("pur.qty")}</th>
                <th className={`${thClass} text-right`}>{t("pur.unitCost")}</th>
                <th className={`${thClass} text-right`}>{t("pur.lineTotal")}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {purchase.items.map((i) => (
                <tr key={i.id}>
                  <td className={`${tdClass} font-medium text-slate-900`}>{i.productName}</td>
                  <td className={tdClass}>{i.sku}</td>
                  <td className={`${tdClass} text-right`}>{formatNumber(i.quantity)}</td>
                  <td className={`${tdClass} text-right`}>{formatMoney(i.unitCost)}</td>
                  <td className={`${tdClass} text-right`}>{formatMoney(i.lineTotal)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <Card>
        <div className="ml-auto max-w-xs space-y-2 text-sm">
          <div className="flex justify-between">
            <span className="text-slate-600">{t("pur.subtotal")}</span>
            <span>{formatMoney(purchase.subtotal)}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-slate-600">{t("pur.discount")}</span>
            <span>{formatMoney(purchase.discountAmount)}</span>
          </div>
          <div className="flex justify-between border-t border-slate-100 pt-2 text-base font-bold text-slate-900">
            <span>{t("pur.total")}</span>
            <span>{formatMoney(purchase.totalAmount)}</span>
          </div>
        </div>

        {isDraft && (canConfirm || canCancel) && (
          <div className="mt-5 flex flex-wrap gap-3 border-t border-slate-100 pt-4">
            {canConfirm && <Button onClick={() => setDialog("confirm")}>{t("pur.confirm")}</Button>}
            {canCancel && (
              <Button variant="danger" onClick={() => setDialog("cancel")}>
                {t("pur.cancel")}
              </Button>
            )}
          </div>
        )}
      </Card>

      <ConfirmDialog
        open={dialog !== null}
        title={dialog === "confirm" ? t("pur.confirm") : t("pur.cancel")}
        message={dialog === "confirm" ? t("pur.confirmMsg") : t("pur.cancelMsg")}
        confirmLabel={dialog === "confirm" ? t("common.confirm") : t("pur.cancel")}
        danger={dialog === "cancel"}
        loading={busy}
        onConfirm={run}
        onCancel={() => setDialog(null)}
      />
    </div>
  );
}
