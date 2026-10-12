import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  getSuppliers,
  getSupplier,
  createSupplier,
  updateSupplier,
  deactivateSupplier,
  type SupplierFormValues,
  type SupplierListItem,
} from "../api/suppliers";
import { errorMessage } from "../lib/apiError";
import { usePermissions } from "../auth/usePermissions";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import Modal from "../components/ui/Modal";
import ConfirmDialog from "../components/ui/ConfirmDialog";
import FormField from "../components/ui/FormField";
import StatusBadge from "../components/ui/StatusBadge";
import SearchInput from "../components/ui/SearchInput";
import Pagination from "../components/ui/Pagination";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

const emptyForm: SupplierFormValues = {
  name: "",
  contactPerson: "",
  phone: "",
  email: "",
  address: "",
  notes: "",
};

interface Filters {
  search: string;
  status: string;
  page: number;
}

export default function SuppliersPage() {
  const { t } = useI18n();
  const toast = useToast();
  const { hasRole } = usePermissions();
  const canManage = hasRole("Admin", "Manager", "Warehouse");
  const canDeactivate = hasRole("Admin", "Manager");

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
      getSuppliers({
        search: filters.search || undefined,
        isActive: filters.status === "" ? undefined : filters.status === "active",
        page: filters.page,
        pageSize: 20,
      }),
    JSON.stringify(filters),
  );

  // ---- create / edit modal ----
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editingActive, setEditingActive] = useState(true);
  const [form, setForm] = useState<SupplierFormValues>(emptyForm);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  function openCreate() {
    setEditingId(null);
    setEditingActive(true);
    setForm(emptyForm);
    setFormError(null);
    setModalOpen(true);
  }

  async function openEdit(s: SupplierListItem) {
    try {
      const full = await getSupplier(s.id);
      setEditingId(full.id);
      setEditingActive(full.isActive);
      setForm({
        name: full.name,
        contactPerson: full.contactPerson ?? "",
        phone: full.phone ?? "",
        email: full.email ?? "",
        address: full.address ?? "",
        notes: full.notes ?? "",
      });
      setFormError(null);
      setModalOpen(true);
    } catch {
      toast.error(t("common.loadFailed"));
    }
  }

  function closeModal() {
    if (saving) return;
    setModalOpen(false);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (saving) return;
    setFormError(null);
    if (!form.name.trim()) return setFormError(t("sup.nameRequired"));

    setSaving(true);
    try {
      if (editingId) await updateSupplier(editingId, { ...form, isActive: editingActive });
      else await createSupplier(form);
      toast.success(t("common.saved"));
      setModalOpen(false);
      list.reload();
    } catch (err) {
      setFormError(errorMessage(err) ?? t("common.saveFailed"));
    } finally {
      setSaving(false);
    }
  }

  // ---- deactivate / activate ----
  const [toDeactivate, setToDeactivate] = useState<SupplierListItem | null>(null);
  const [busy, setBusy] = useState(false);

  async function confirmDeactivate() {
    if (!toDeactivate || busy) return;
    setBusy(true);
    try {
      await deactivateSupplier(toDeactivate.id);
      toast.success(t("common.deactivated"));
      setToDeactivate(null);
      list.reload();
    } catch {
      toast.error(t("common.deactivateFailed"));
    } finally {
      setBusy(false);
    }
  }

  async function activate(s: SupplierListItem) {
    if (busy) return;
    setBusy(true);
    try {
      const full = await getSupplier(s.id);
      await updateSupplier(s.id, {
        name: full.name,
        contactPerson: full.contactPerson ?? "",
        phone: full.phone ?? "",
        email: full.email ?? "",
        address: full.address ?? "",
        notes: full.notes ?? "",
        isActive: true,
      });
      toast.success(t("common.activated"));
      list.reload();
    } catch {
      toast.error(t("common.saveFailed"));
    } finally {
      setBusy(false);
    }
  }

  const data = list.data;

  return (
    <div className="w-full">
      <PageHeader
        title={t("sup.title")}
        actions={canManage ? <Button onClick={openCreate}>+ {t("sup.add")}</Button> : undefined}
      />

      <Card className="mb-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <SearchInput
            className="sm:col-span-2"
            value={searchText}
            onChange={setSearchText}
            placeholder={t("sup.search")}
          />
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
        {!list.loading && !list.error && data && data.items.length === 0 && <EmptyState message={t("sup.empty")} />}
        {!list.loading && !list.error && data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={thClass}>{t("sup.supplier")}</th>
                  <th className={thClass}>{t("sup.contact")}</th>
                  <th className={thClass}>{t("sup.phone")}</th>
                  <th className={thClass}>{t("sup.email")}</th>
                  <th className={thClass}>{t("common.status")}</th>
                  <th className={thClass}>{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map((s) => (
                  <tr key={s.id} className="hover:bg-slate-50">
                    <td className={tdClass}>
                      <Link to={`/suppliers/${s.id}`} className="font-medium text-blue-600 hover:underline">
                        {s.name}
                      </Link>
                    </td>
                    <td className={tdClass}>{s.contactPerson ?? "—"}</td>
                    <td className={tdClass}>{s.phone ?? "—"}</td>
                    <td className={tdClass}>{s.email ?? "—"}</td>
                    <td className={tdClass}>
                      <StatusBadge
                        label={s.isActive ? t("common.active") : t("common.inactive")}
                        tone={s.isActive ? "green" : "gray"}
                      />
                    </td>
                    <td className={tdClass}>
                      <div className="flex flex-wrap gap-2">
                        {canManage && (
                          <Button size="sm" variant="secondary" onClick={() => openEdit(s)}>
                            {t("common.edit")}
                          </Button>
                        )}
                        {canDeactivate && s.isActive && (
                          <Button size="sm" variant="ghost" className="!text-red-600" onClick={() => setToDeactivate(s)}>
                            {t("common.deactivate")}
                          </Button>
                        )}
                        {canDeactivate && !s.isActive && (
                          <Button size="sm" variant="ghost" className="!text-green-700" onClick={() => activate(s)}>
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
        open={modalOpen}
        title={editingId ? t("sup.edit") : t("sup.add")}
        onClose={closeModal}
        footer={
          <>
            <Button variant="secondary" onClick={closeModal} disabled={saving}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" form="supplier-form" loading={saving}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <form id="supplier-form" onSubmit={handleSubmit} className="space-y-4">
          <FormField label={t("common.name")} required>
            <input
              className={inputClass}
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              autoFocus
            />
          </FormField>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label={t("sup.contact")}>
              <input
                className={inputClass}
                value={form.contactPerson}
                onChange={(e) => setForm({ ...form, contactPerson: e.target.value })}
              />
            </FormField>
            <FormField label={t("sup.phone")}>
              <input
                className={inputClass}
                value={form.phone}
                onChange={(e) => setForm({ ...form, phone: e.target.value })}
              />
            </FormField>
          </div>
          <FormField label={t("sup.email")}>
            <input
              className={inputClass}
              value={form.email}
              onChange={(e) => setForm({ ...form, email: e.target.value })}
            />
          </FormField>
          <FormField label={t("sup.address")}>
            <input
              className={inputClass}
              value={form.address}
              onChange={(e) => setForm({ ...form, address: e.target.value })}
            />
          </FormField>
          <FormField label={t("sup.notes")}>
            <textarea
              className={inputClass}
              rows={2}
              value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
            />
          </FormField>
          {editingId && canDeactivate && (
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input type="checkbox" checked={editingActive} onChange={(e) => setEditingActive(e.target.checked)} />
              {t("common.active")}
            </label>
          )}
          {formError && (
            <p className="text-sm text-red-600" role="alert">
              {formError}
            </p>
          )}
        </form>
      </Modal>

      <ConfirmDialog
        open={toDeactivate !== null}
        title={t("sup.deactivateTitle")}
        message={
          <>
            <p className="font-medium text-slate-900">{toDeactivate?.name}</p>
            <p className="mt-1">{t("sup.deactivateMsg")}</p>
          </>
        }
        confirmLabel={t("common.deactivate")}
        danger
        loading={busy}
        onConfirm={confirmDeactivate}
        onCancel={() => setToDeactivate(null)}
      />
    </div>
  );
}
