import { useState } from "react";
import type { ComponentType } from "react";
import { Navigate } from "react-router-dom";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import PageHeader from "../components/ui/PageHeader";
import SalesReportTab from "./reports/SalesReportTab";
import InventoryReportTab from "./reports/InventoryReportTab";
import CustomerDebtReportTab from "./reports/CustomerDebtReportTab";
import PurchaseReportTab from "./reports/PurchaseReportTab";
import ExpenseReportTab from "./reports/ExpenseReportTab";
import PaymentReportTab from "./reports/PaymentReportTab";
import ProductPerformanceReportTab from "./reports/ProductPerformanceReportTab";
import CategoryPerformanceReportTab from "./reports/CategoryPerformanceReportTab";
import ProfitabilityReportTab from "./reports/ProfitabilityReportTab";

interface TabDef {
  key: string;
  labelKey: string;
  component: ComponentType;
}

const TABS: TabDef[] = [
  { key: "sales", labelKey: "rep.sales", component: SalesReportTab },
  { key: "inventory", labelKey: "rep.inventory", component: InventoryReportTab },
  { key: "customer-debt", labelKey: "rep.debt", component: CustomerDebtReportTab },
  { key: "purchases", labelKey: "rep.purchases", component: PurchaseReportTab },
  { key: "expenses", labelKey: "rep.expenses", component: ExpenseReportTab },
  { key: "payments", labelKey: "rep.payments", component: PaymentReportTab },
  { key: "product-performance", labelKey: "rep.product", component: ProductPerformanceReportTab },
  { key: "category-performance", labelKey: "rep.category", component: CategoryPerformanceReportTab },
  { key: "profitability", labelKey: "rep.profit", component: ProfitabilityReportTab },
];

export default function ReportsPage() {
  const { has } = usePermissions();
  const { t } = useI18n();
  const [activeKey, setActiveKey] = useState<string>(TABS[0].key);

  if (!has("Reports.View")) return <Navigate to="/" replace />;

  const active = TABS.find((x) => x.key === activeKey) ?? TABS[0];
  const ActiveComponent = active.component;

  return (
    <div className="w-full">
      <PageHeader title={t("nav.reports")} subtitle={t("rep.subtitle")} />

      <div className="mb-4 flex flex-wrap gap-2" role="tablist">
        {TABS.map((tab) => {
          const isActive = tab.key === active.key;
          return (
            <button
              key={tab.key}
              role="tab"
              aria-selected={isActive}
              onClick={() => setActiveKey(tab.key)}
              className={`rounded-full px-4 py-1.5 text-sm font-medium transition ${
                isActive ? "bg-blue-600 text-white" : "bg-white text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50"
              }`}
            >
              {t(tab.labelKey)}
            </button>
          );
        })}
      </div>

      <ActiveComponent key={active.key} />
    </div>
  );
}
