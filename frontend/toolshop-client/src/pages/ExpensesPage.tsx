import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { MoreVertical } from "lucide-react";
import {
  getExpenses,
  getExpenseCategories,
  voidExpense,
  createExpenseCategory,
  updateExpenseCategory,
  deactivateExpenseCategory,
  type ExpenseListItem,
} from "../api/finance";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorMessage } from "../lib/apiError";
import { formatDate, formatMoney } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import FormField from "../components/ui/FormField";
import StatusBadge from "../components/ui/StatusBadge";
import SearchInput from "../components/ui/SearchInput";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

interface Filters {
  search: string;
  categoryId: string;
  status: string;
  fromDate: string;
  toDate: string;
  page: number;
}

export default function ExpensesPage() {
  const { t } = useI18n();
  const toast = useToast();
  const { has } = usePermissions();
  const canCreate = has("Expenses.Create");
  const canUpdate = has("Expenses.Update");
  const canVoid = has("Expenses.Void");

  const [filters, setFilters] = useState<Filters>({
    search: "",
    categoryId: "",
    status: "",
    fromDate: "",
    toDate: "",
    page: 1,
  });
  const [searchText, setSearchText] = useState("");

  useEffect(() => {
    const id = window.setTimeout(() => {
      setFilters((f) => (f.search === searchText ? f : { ...f, search: searchText, page: 1 }));
    }, 300);
    return () => window.clearTimeout(id);
  }, [searchText]);

  function patch(p: Partial<Filters>) {
    setFilters((f) => ({ ...f, ...p, page: p.page ?? 1 }));
  }

  const cats = useAsyncData(() => getExpenseCategories(true), "exp-cats");

  const list = useAsyncData(
    () =>
      getExpenses({
        search: filters.search || undefined,
        categoryId: filters.categoryId ? Number(filters.categoryId) : undefined,
        isVoided: filters.status === "" ? undefined : filters.status === "voided",
        fromDate: filters.fromDate || undefined,
        toDate: filters.toDate || undefined,
        page: filters.page,
        pageSize: 20,
      }),
    JSON.stringify(filters),
  );

  // ---------- row menu / void ----------
  const [menuId, setMenuId] = useState<number | null>(null);
  const [voiding, setVoiding] = useState<ExpenseListItem | null>(null);
  const [reason, setReason] = useState("");
  const [voidError, setVoidError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function confirmVoid() {
    if (!voiding || busy) return;
    if (!reason.trim()) return setVoidError(t("exp.reasonRequired"));
    setBusy(true);
    try {
      await voidExpense(voiding.id, reason.trim());
      toast.success(t("exp.voidedOk"));
      setVoiding(null);
      list.reload();
    } catch (err) {
      setVoidError(errorMessage(err) ?? t("exp.voidFailed"));
    } finally {
      setBusy(false);
    }
  }

  // ---------- categories drawer ----------
  const [catOpen, setCatOpen] = useState(false);
  const [newCat, setNewCat] = useState("");
  const [editCatId, setEditCatId] = useState<number | null>(null);
  const [editCatName, setEditCatName] = useState("");
  const [catError, setCatError] = useState<string | null>(null);
  const [catBusy, setCatBusy] = useState(false);

  async function runCat(action: () => Promise<unknown>, okMessage: string) {
    if (catBusy) return;
    setCatBusy(true);
    setCatError(null);
    try {
      await action();
      toast.success(okMessage);
      cats.reload();
    } catch (err) {
      setCatError(errorMessage(err) ?? t("exp.catFailed"));
    } finally {
      setCatBusy(false);
    }
  }

  async function addCategory() {
    if (!newCat.trim()) return setCatError(t("exp.catNameRequired"));
    await runCat(async () => {
      await createExpenseCategory(newCat.trim(), null);
      setNewCat("");
    }, t("exp.catAdded"));
  }

  async function saveCategoryName(id: number, description: string | null) {
    if (!editCatName.trim()) return setCatError(t("exp.catNameRequired"));
    await runCat(async () => {
      await updateExpenseCategory(id, editCatName.trim(), description);
      setEditCatId(null);
    }, t("exp.catUpdated"));
  }

  const data = list.data;
  const categories = cats.data ?? [];

  return (
    <div className="w-full">
      <PageHeader
        title={t("exp.title")}
        actions={
          <>
            {canCreate && (
              <Button variant="secondary" onClick={() => setCatOpen(true)}>
                {t("exp.manageCategories")}
              </Button>
            )}
            {canCreate && (
              <Link to="/expenses/new">
                <Button>+ {t("exp.new")}</Button>
              </Link>
            )}
          </>
        }
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-6">
          <SearchInput className="lg:col-span-2" value={searchText} onChange={setSearchText} placeholder={t("exp.search")} />
          <select className={inputClass} value={filters.categoryId} onChange={(e) => patch({ categoryId: e.target.value })}>
            <option value="">{t("exp.allCategories")}</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          <select className={inputClass} value={filters.status} onChange={(e) => patch({ status: e.target.value })}>
            <option value="">{t("exp.allStatuses")}</option>
            <option value="active">{t("common.active")}</option>
            <option value="voided">{t("exp.voided")}</option>
          </select>
          <div className="flex min-w-0 flex-col gap-2 min-[480px]:flex-row xl:col-span-2">
            <input
              type="date"
              aria-label={t("exp.from")}
              className={`${inputClass} min-w-0 flex-1`}
              value={filters.fromDate}
              onChange={(e) => patch({ fromDate: e.target.value })}
            />
            <input
              type="date"
              aria-label={t("exp.to")}
              className={`${inputClass} min-w-0 flex-1`}
              value={filters.toDate}
              onChange={(e) => patch({ toDate: e.target.value })}
            />
          </div>
        </div>
      </Card>

      <Card className="!overflow-visible !p-0">
        {list.loading && <LoadingSkeleton />}
        {list.error && <ErrorState message={t("common.loadFailed")} onRetry={list.reload} />}
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("exp.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("exp.number")}</th>
                  <th className={thClass}>{t("exp.date")}</th>
                  <th className={thClass}>{t("exp.category")}</th>
                  <th className={thClass}>{t("exp.description")}</th>
                  <th className={`${thClass} text-right`}>{t("common.amount")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={thClass}></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((e) => (
                  <tr key={e.id} className="hover:bg-slate-50">
                    <td className={`${tdClass} font-medium text-slate-900`}>{e.expenseNumber}</td>
                    <td className={tdClass}>{formatDate(e.expenseDate)}</td>
                    <td className={tdClass}>{e.categoryName}</td>
                    <td className={tdClass}>{e.description}</td>
                    <td className={`${tdClass} text-right font-medium`}>{formatMoney(e.amount)}</td>
                    <td className={tdClass}>
                      <StatusBadge
                        label={e.isVoided ? t("exp.voided") : t("common.active")}
                        tone={e.isVoided ? "red" : "green"}
                      />
                    </td>
                    <td className={`${tdClass} relative text-right`}>
                      {(canUpdate || (canVoid && !e.isVoided)) && (
                        <>
                          <button
                            type="button"
                            aria-label={t("common.actions")}
                            onClick={() => setMenuId(menuId === e.id ? null : e.id)}
                            className="rounded-lg p-1.5 text-slate-500 hover:bg-slate-100"
                          >
                            <MoreVertical size={18} />
                          </button>
                          {menuId === e.id && (
                            <>
                              <div className="fixed inset-0 z-10" onClick={() => setMenuId(null)} aria-hidden="true" />
                              <div className="absolute right-4 top-10 z-20 w-48 rounded-lg border border-slate-200 bg-white py-1 text-left shadow-lg">
                                {canUpdate && (
                                  <Link
                                    to={`/expenses/${e.id}/edit`}
                                    className="block px-3 py-2 text-sm text-slate-700 hover:bg-slate-50"
                                  >
                                    {t("exp.menuEdit")}
                                  </Link>
                                )}
                                {canVoid && !e.isVoided && (
                                  <button
                                    type="button"
                                    onClick={() => {
                                      setMenuId(null);
                                      setVoiding(e);
                                      setReason("");
                                      setVoidError(null);
                                    }}
                                    className="block w-full px-3 py-2 text-left text-sm text-red-600 hover:bg-red-50"
                                  >
                                    {t("exp.voidTitle")}
                                  </button>
                                )}
                              </div>
                            </>
                          )}
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
          onChange={(page) => patch({ page })}
        />
      )}

      {/* Void dialog */}
      <Modal
        open={voiding !== null}
        title={t("exp.voidTitle")}
        onClose={() => !busy && setVoiding(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setVoiding(null)} disabled={busy}>
              {t("common.cancel")}
            </Button>
            <Button variant="danger" onClick={confirmVoid} loading={busy}>
              {t("exp.voidTitle")}
            </Button>
          </>
        }
      >
        {voiding && (
          <div className="space-y-4">
            <p className="text-sm font-medium text-slate-900">
              {voiding.expenseNumber} · {formatMoney(voiding.amount)}
            </p>
            <p className="rounded-lg bg-amber-50 p-3 text-sm text-amber-800">{t("exp.voidConsequence")}</p>
            <FormField label={t("exp.reason")} required>
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

      {/* Categories drawer */}
      <Modal open={catOpen} side title={t("exp.manageCategories")} onClose={() => setCatOpen(false)}>
        <div className="space-y-4">
          <div className="flex gap-2">
            <input
              className={inputClass}
              placeholder={t("exp.catName")}
              value={newCat}
              onChange={(e) => setNewCat(e.target.value)}
            />
            <Button onClick={addCategory} loading={catBusy}>
              {t("common.add")}
            </Button>
          </div>
          {catError && (
            <p className="text-sm text-red-600" role="alert">
              {catError}
            </p>
          )}
          <ul className="divide-y divide-slate-100">
            {categories.map((c) => (
              <li key={c.id} className="flex items-center justify-between gap-2 py-2 text-sm">
                {editCatId === c.id ? (
                  <>
                    <input className={inputClass} value={editCatName} onChange={(e) => setEditCatName(e.target.value)} />
                    <Button size="sm" onClick={() => saveCategoryName(c.id, c.description)} disabled={catBusy}>
                      {t("common.save")}
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setEditCatId(null)}>
                      {t("common.cancel")}
                    </Button>
                  </>
                ) : (
                  <>
                    <div className="min-w-0 flex-1">
                      <span className="font-medium text-slate-900">{c.name}</span>{" "}
                      <StatusBadge
                        label={c.isActive ? t("common.active") : t("common.inactive")}
                        tone={c.isActive ? "green" : "gray"}
                      />
                    </div>
                    {c.isActive && (
                      <div className="flex gap-1">
                        <Button
                          size="sm"
                          variant="secondary"
                          onClick={() => {
                            setEditCatId(c.id);
                            setEditCatName(c.name);
                            setCatError(null);
                          }}
                        >
                          {t("common.edit")}
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          className="!text-red-600"
                          disabled={catBusy}
                          onClick={() => runCat(() => deactivateExpenseCategory(c.id), t("exp.catDeactivated"))}
                        >
                          {t("common.deactivate")}
                        </Button>
                      </div>
                    )}
                  </>
                )}
              </li>
            ))}
          </ul>
        </div>
      </Modal>
    </div>
  );
}
