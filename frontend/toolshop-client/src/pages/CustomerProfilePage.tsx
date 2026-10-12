import { Link, useParams } from "react-router-dom";
import { getCustomer } from "../api/customers";
import { getOrders } from "../api/orders";
import { getPayments, getCustomerOutstandingOrders } from "../api/payments";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate, formatDateTime, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import KpiCard from "../components/ui/KpiCard";
import Button from "../components/ui/Button";
import StatusBadge from "../components/ui/StatusBadge";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { tdClass, thClass } from "../components/ui/formStyles";

export default function CustomerProfilePage() {
  const { id } = useParams();
  const { t } = useI18n();
  const { hasRole } = usePermissions();
  const canEdit = hasRole("Admin", "Manager", "Cashier");

  const { data, loading, error, reload } = useAsyncData(async () => {
    const customerId = Number(id);
    const [customer, orders, payments, outstanding] = await Promise.all([
      getCustomer(customerId),
      getOrders({ customerId, pageSize: 100 }),
      getPayments({ customerId, pageSize: 100 }),
      getCustomerOutstandingOrders(customerId),
    ]);
    return { customer, orders: orders.items, payments: payments.items, outstanding };
  }, id ?? "");

  if (loading) return <LoadingSkeleton rows={8} />;
  if (error || !data) return <ErrorState message={t("cust.notFound")} onRetry={reload} />;

  const { customer, orders, payments, outstanding } = data;

  const completed = orders.filter((o) => o.status === "Completed");
  const totalPurchases = completed.reduce((s, o) => s + o.totalAmount, 0);
  const currentDebt = outstanding.reduce((s, o) => s + o.outstanding, 0);
  const totalPaid = totalPurchases - currentDebt;
  const lastPurchase =
    completed.length > 0
      ? completed.reduce((latest, o) => (o.orderDate > latest ? o.orderDate : latest), completed[0].orderDate)
      : null;

  return (
    <div className="w-full">
      <PageHeader
        title={customer.name}
        subtitle={customer.phone}
        actions={
          <>
            {canEdit && (
              <Link to={`/customers/${customer.id}/edit`}>
                <Button variant="secondary">{t("common.edit")}</Button>
              </Link>
            )}
            <Link to="/customers">
              <Button variant="ghost">{t("common.back")}</Button>
            </Link>
          </>
        }
      />

      <Card className="mb-4">
        <dl className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-slate-500">{t("common.status")}</dt>
            <dd>
              <StatusBadge
                label={customer.isActive ? t("common.active") : t("common.inactive")}
                tone={customer.isActive ? "green" : "gray"}
              />
            </dd>
          </div>
          <div>
            <dt className="text-slate-500">{t("cust.address")}</dt>
            <dd className="text-slate-900">{customer.address || "—"}</dd>
          </div>
          <div>
            <dt className="text-slate-500">{t("cust.notes")}</dt>
            <dd className="text-slate-900">{customer.notes || "—"}</dd>
          </div>
          <div>
            <dt className="text-slate-500">{t("common.created")}</dt>
            <dd className="text-slate-900">{formatDateTime(customer.createdAt)}</dd>
          </div>
        </dl>
      </Card>

      <div className="mb-4 grid grid-cols-2 gap-3 xl:grid-cols-4">
        <KpiCard label={t("cust.totalPurchases")} value={formatMoney(totalPurchases)} />
        <KpiCard label={t("cust.paid")} value={formatMoney(totalPaid)} tone="positive" />
        <KpiCard label={t("cust.debt")} value={formatMoney(currentDebt)} tone={currentDebt > 0 ? "negative" : "default"} />
        <KpiCard label={t("cust.debtOrders")} value={String(outstanding.length)} />
      </div>

      {outstanding.length > 0 && (
        <Card title={t("cust.outstanding")} className="mb-4">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr>
                  <th className={thClass}>{t("common.order")}</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={`${thClass} text-right`}>{t("cust.debt")}</th>
                  <th className={thClass}></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {outstanding.map((o) => (
                  <tr key={o.orderId}>
                    <td className={tdClass}>{o.orderNumber}</td>
                    <td className={tdClass}>{formatDate(o.orderDate)}</td>
                    <td className={`${tdClass} text-right font-semibold text-red-600`}>{formatMoney(o.outstanding)}</td>
                    <td className={tdClass}>
                      <Link to={`/orders/${o.orderId}`} className="text-blue-600 hover:underline">
                        {t("cust.acceptPayment")}
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <Card title={t("cust.orderHistory")} className="mb-4">
        {orders.length === 0 ? (
          <EmptyState message={t("cust.noOrders")} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr>
                  <th className={thClass}>{t("common.order")}</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={`${thClass} text-right`}>{t("common.amount")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {orders.map((o) => (
                  <tr key={o.id}>
                    <td className={tdClass}>
                      <Link to={`/orders/${o.id}`} className="text-blue-600 hover:underline">
                        {o.orderNumber}
                      </Link>
                    </td>
                    <td className={tdClass}>{formatDate(o.orderDate)}</td>
                    <td className={tdClass}>{o.status}</td>
                    <td className={`${tdClass} text-right`}>{formatMoney(o.totalAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card title={t("cust.paymentHistory")}>
        {payments.length === 0 ? (
          <EmptyState message={t("cust.noPayments")} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr>
                  <th className={thClass}>№</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={thClass}>{t("common.order")}</th>
                  <th className={`${thClass} text-right`}>{t("common.amount")}</th>
                  <th className={thClass}>{t("common.method")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {payments.map((p) => (
                  <tr key={p.id}>
                    <td className={tdClass}>{p.paymentNumber}</td>
                    <td className={tdClass}>{formatDateTime(p.createdAt)}</td>
                    <td className={tdClass}>{p.orderNumber}</td>
                    <td className={`${tdClass} text-right`}>{formatMoney(p.amount)}</td>
                    <td className={tdClass}>{p.method}</td>
                    <td className={tdClass}>{p.status}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {lastPurchase && (
          <p className="mt-3 text-xs text-slate-400">
            {t("cust.lastPurchase")}: {formatDate(lastPurchase)}
          </p>
        )}
      </Card>
    </div>
  );
}
