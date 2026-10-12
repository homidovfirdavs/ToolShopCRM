import { useState } from "react";
import { Link } from "react-router-dom";
import { getMovements, type Movement, type MovementType } from "../api/inventory";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDateTime, formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const TYPES: MovementType[] = [
  "StockIn",
  "StockOut",
  "AdjustmentIncrease",
  "AdjustmentDecrease",
  "Sale",
  "SaleReturn",
  "Purchase",
  "PurchaseReturn",
];

const INCREASING: MovementType[] = ["StockIn", "AdjustmentIncrease", "Purchase", "SaleReturn"];

function Quantity({ m }: { m: Movement }) {
  const up = INCREASING.includes(m.movementType);
  return (
    <span className={`font-semibold ${up ? "text-green-600" : "text-red-600"}`}>
      {up ? "+" : "−"}
      {formatNumber(m.quantity)}
    </span>
  );
}

export default function InventoryMovementsPage() {
  const { t } = useI18n();
  const [movementType, setMovementType] = useState("");
  const [page, setPage] = useState(1);

  const list = useAsyncData(
    () =>
      getMovements({
        movementType: movementType ? (movementType as MovementType) : undefined,
        page,
        pageSize: 20,
      }),
    `${movementType}|${page}`,
  );

  const data = list.data;

  return (
    <div className="w-full">
      <PageHeader
        title={t("mov.title")}
        actions={
          <Link to="/inventory">
            <Button variant="secondary">{t("common.back")}</Button>
          </Link>
        }
      />

      <Card className="mb-4">
        <select
          className={`${inputClass} sm:max-w-xs`}
          value={movementType}
          onChange={(e) => {
            setMovementType(e.target.value);
            setPage(1);
          }}
        >
          <option value="">{t("mov.allTypes")}</option>
          {TYPES.map((x) => (
            <option key={x} value={x}>
              {t(`mov.${x}`)}
            </option>
          ))}
        </select>
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("mov.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("common.date")}</th>
                  <th className={thClass}>{t("inv.product")}</th>
                  <th className={thClass}>SKU</th>
                  <th className={thClass}>{t("mov.type")}</th>
                  <th className={`${thClass} text-right`}>{t("inv.quantity")}</th>
                  <th className={thClass}>{t("inv.reason")}</th>
                  <th className={thClass}>{t("mov.user")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((m) => (
                  <tr key={m.id} className="hover:bg-slate-50">
                    <td className={tdClass}>{formatDateTime(m.createdAt)}</td>
                    <td className={`${tdClass} font-medium text-slate-900`}>{m.productName}</td>
                    <td className={tdClass}>{m.sku}</td>
                    <td className={tdClass}>{t(`mov.${m.movementType}`)}</td>
                    <td className={`${tdClass} text-right`}>
                      <Quantity m={m} />
                    </td>
                    <td className={tdClass}>{m.reason}</td>
                    <td className={tdClass}>{m.createdByUsername}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {data && <Pagination page={page} totalPages={data.totalPages} totalItems={data.totalItems} onChange={setPage} />}
    </div>
  );
}
