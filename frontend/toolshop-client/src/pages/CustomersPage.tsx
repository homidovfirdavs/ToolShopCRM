import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCustomers, getCustomer, updateCustomer, deactivateCustomer, type CustomerListItem } from "../api/customers";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { formatDate } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import ConfirmDialog from "../components/ui/ConfirmDialog";
import StatusBadge from "../components/ui/StatusBadge";
import Pagination from "../components/ui/Pagination";
import SearchInput from "../components/ui/SearchInput";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

interface Filters {
  search: string;
  status: string;
  page: number;
}

export default function CustomersPage() {
  const { t } = useI18n();
  const { hasRole } = usePermissions();
  const toast = useToast();
  const canDeactivate = hasRole("Admin", "Manager");
  const canCreate = hasRole("Admin", "Manager", "Cashier");

  const [filters, setFilters] = useState<Filters>({ search: "", status: "active", page: 1 });
  const [searchText, setSearchText] = useState("");

  useEffect(() => {
    const id = window.setTimeout(() => {
      setFilters((f) => (f.search === searchText ? f : { ...f, search: searchText, page: 1 }));
    }, 300);
    return () => window.clearTimeout(id);
  }, [searchText]);

  const list = useAsyncData(
    () =>
      getCustomers({
        search: filters.search || undefined,
        isActive: filters.status === "" ? undefined : filters.status === "active",
        page: filters.page,
        pageSize: 20,
      }),
    JSON.stringify(filters),
  );

  const [toDeactivate, setToDeactivate] = useState<CustomerListItem | null>(null);
  const [deactivating, setDeactivating] = useState(false);

  async function handleActivate(item: CustomerListItem) {
    try {
      const c = await getCustomer(item.id);
      await updateCustomer(item.id, {
        name: c.name,
        phone: c.phone,
        address: c.address ?? "",
        notes: c.notes ?? "",
        isActive: true,
      });
      toast.success(t("common.activated"));
      list.reload();
    } catch {
      toast.error(t("common.saveFailed"));
    }
  }

  async function handleDeactivate() {
    if (!toDeactivate) return;
    setDeactivating(true);
    try {
      await deactivateCustomer(toDeactivate.id);
      toast.success(t("common.deactivated"));
      setToDeactivate(null);
      list.reload();
    } catch {
      toast.error(t("common.deactivateFailed"));
    } finally {
      setDeactivating(false);
    }
  }

  const data = list.data;

  return (
    <div className="w-full">
      <PageHeader
        title={t("nav.customers")}
        actions={
          canCreate ? (
            <Link to="/customers/new">
              <Button>+ {t("cust.add")}</Button>
            </Link>
          ) : undefined
        }
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <SearchInput value={searchText} onChange={setSearchText} placeholder={t("cust.search")} className="sm:col-span-2" />
          <select
            className={inputClass}
            value={filters.status}
            onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value, page: 1 }))}
          >
            <option value="active">{t("common.active")}</option>
            <option value="inactive">{t("common.inactive")}</option>
            <option value="">{t("common.all")}</option>
          </select>
        </div>
      </Card>

      <Card className="overflow-hidden !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("cust.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <>
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("common.name")}</th>
                  <th className={thClass}>{t("cust.phone")}</th>
                  <th className={thClass}>{t("cust.address")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={thClass}>{t("common.created")}</th>
                  <th className={thClass}>{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((c) => (
                  <tr key={c.id} className="hover:bg-slate-50">
                    <td className={tdClass}>
                      <Link to={`/customers/${c.id}`} className="font-medium text-blue-600 hover:underline">
                        {c.name}
                      </Link>
                    </td>
                    <td className={tdClass}>{c.phone}</td>
                    <td className={tdClass}>{c.address || "—"}</td>
                    <td className={tdClass}>
                      <StatusBadge
                        label={c.isActive ? t("common.active") : t("common.inactive")}
                        tone={c.isActive ? "green" : "gray"}
                      />
                    </td>
                    <td className={tdClass}>{formatDate(c.createdAt)}</td>
                    <td className={tdClass}>
                      <div className="flex gap-2">
                        {canCreate && (
                          <Link to={`/customers/${c.id}/edit`}>
                            <Button size="sm" variant="secondary">
                              {t("common.edit")}
                            </Button>
                          </Link>
                        )}
                        {canDeactivate && c.isActive && (
                          <Button size="sm" variant="ghost" className="!text-red-600" onClick={() => setToDeactivate(c)}>
                            {t("common.deactivate")}
                          </Button>
                        )}
                        {canCreate && !c.isActive && (
                          <Button size="sm" variant="ghost" className="!text-green-700" onClick={() => handleActivate(c)}>
                            {t("common.activate")}
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <ul className="divide-y divide-slate-100 md:hidden" data-testid="customers-cards">
            {data.items.map((c) => (
              <li key={c.id} className="space-y-2 px-4 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <Link to={`/customers/${c.id}`} className="font-medium text-blue-600">
                      {c.name}
                    </Link>
                    <p className="text-sm text-slate-600">{c.phone}</p>
                    <p className="text-xs text-slate-500">{c.address || "—"}</p>
                  </div>
                  <StatusBadge label={c.isActive ? t("common.active") : t("common.inactive")} tone={c.isActive ? "green" : "gray"} />
                </div>
                <div className="flex flex-wrap gap-2">
                  {canCreate && (
                    <Link to={`/customers/${c.id}/edit`}>
                      <Button size="sm" variant="secondary" className="min-h-10">
                        {t("common.edit")}
                      </Button>
                    </Link>
                  )}
                  {canDeactivate && c.isActive && (
                    <Button size="sm" variant="ghost" className="min-h-10 !text-red-600" onClick={() => setToDeactivate(c)}>
                      {t("common.deactivate")}
                    </Button>
                  )}
                  {canCreate && !c.isActive && (
                    <Button size="sm" variant="ghost" className="min-h-10 !text-green-700" onClick={() => handleActivate(c)}>
                      {t("common.activate")}
                    </Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
          </>
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

      <ConfirmDialog
        open={toDeactivate !== null}
        title={t("common.deactivate")}
        message={t("cust.confirm")}
        confirmLabel={t("common.deactivate")}
        danger
        loading={deactivating}
        onConfirm={handleDeactivate}
        onCancel={() => setToDeactivate(null)}
      />
    </div>
  );
}
