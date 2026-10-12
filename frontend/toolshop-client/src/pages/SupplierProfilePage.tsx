import { Link, useParams } from "react-router-dom";
import { getSupplier, getSupplierPurchases } from "../api/suppliers";
import type { PurchaseStatus } from "../api/purchases";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import StatusBadge from "../components/ui/StatusBadge";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { tdClass, thClass } from "../components/ui/formStyles";

interface RecentPurchase {
  id: number;
  purchaseNumber: string;
  purchaseDate: string;
  status: PurchaseStatus;
  totalAmount: number;
}

const TONE: Record<PurchaseStatus, "gray" | "green" | "red"> = {
  Draft: "gray",
  Confirmed: "green",
  Cancelled: "red",
};

export default function SupplierProfilePage() {
  const { id } = useParams();
  const { t } = useI18n();

  const { data, loading, error, reload } = useAsyncData(async () => {
    const supplierId = Number(id);
    const [supplier, purchases] = await Promise.all([
      getSupplier(supplierId),
      getSupplierPurchases(supplierId).catch(() => ({ items: [] })),
    ]);
    return { supplier, purchases: purchases.items as RecentPurchase[] };
  }, id ?? "");

  if (loading) return <LoadingSkeleton rows={6} />;
  if (error || !data) return <ErrorState message={t("sup.notFound")} onRetry={reload} />;

  const { supplier, purchases } = data;
  const details: [string, string | null][] = [
    [t("sup.contact"), supplier.contactPerson],
    [t("sup.phone"), supplier.phone],
    [t("sup.email"), supplier.email],
    [t("sup.address"), supplier.address],
    [t("sup.notes"), supplier.notes],
  ];

  return (
    <div className="mx-auto max-w-4xl">
      <PageHeader
        title={supplier.name}
        actions={
          <>
            <StatusBadge
              label={supplier.isActive ? t("common.active") : t("common.inactive")}
              tone={supplier.isActive ? "green" : "gray"}
            />
            <Link to="/suppliers">
              <Button variant="secondary">{t("common.back")}</Button>
            </Link>
          </>
        }
      />

      <Card className="mb-4">
        <dl className="grid grid-cols-1 gap-4 text-sm sm:grid-cols-2">
          {details.map(([label, value]) => (
            <div key={label}>
              <dt className="text-slate-500">{label}</dt>
              <dd className="font-medium text-slate-900">{value || "—"}</dd>
            </div>
          ))}
        </dl>
      </Card>

      <Card className="overflow-hidden !p-0">
        <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
          <h2 className="text-sm font-semibold text-slate-800">{t("sup.recent")}</h2>
          <Link to="/purchases" className="text-sm text-blue-600 hover:underline">
            {t("sup.viewAll")}
          </Link>
        </div>
        {purchases.length === 0 ? (
          <EmptyState message={t("sup.noPurchases")} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("pur.number")}</th>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={`${thClass} text-right`}>{t("pur.total")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {purchases.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50">
                    <td className={tdClass}>
                      <Link to={`/purchases/${p.id}`} className="font-medium text-blue-600 hover:underline">
                        {p.purchaseNumber}
                      </Link>
                    </td>
                    <td className={tdClass}>{formatDate(p.purchaseDate)}</td>
                    <td className={tdClass}>
                      <StatusBadge label={t(`pur.status.${p.status}`)} tone={TONE[p.status] ?? "gray"} />
                    </td>
                    <td className={`${tdClass} text-right`}>{formatMoney(p.totalAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
