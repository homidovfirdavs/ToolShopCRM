import { useState, type FormEvent } from "react";
import { usePermissions } from "../../auth/usePermissions";
import { useI18n } from "../../i18n/useI18n";
import { useAsyncData } from "../../lib/useAsyncData";
import { errorStatus } from "../../lib/apiError";
import PageHeader from "../ui/PageHeader";
import Card from "../ui/Card";
import Button from "../ui/Button";
import Modal from "../ui/Modal";
import ConfirmDialog from "../ui/ConfirmDialog";
import FormField from "../ui/FormField";
import StatusBadge from "../ui/StatusBadge";
import { EmptyState, ErrorState, LoadingSkeleton } from "../ui/States";
import { useToast } from "../ui/useToast";
import { inputClass, tdClass, thClass } from "../ui/formStyles";

export interface CatalogItem {
  id: number;
  name: string;
  description: string | null;
  isActive: boolean;
  /** Present for categories only (real count from the API). */
  productCount?: number;
  totalProductCount?: number;
}

interface Props {
  /** i18n prefix: "cat" or "brand" */
  prefix: "cat" | "brand";
  titleKey: string;
  load: () => Promise<CatalogItem[]>;
  create: (name: string, description: string) => Promise<unknown>;
  update: (id: number, name: string, description: string, isActive: boolean) => Promise<unknown>;
  deactivate: (id: number) => Promise<unknown>;
}

// Tighter cell padding on phones so the whole row (incl. actions) fits without sideways scrolling.
const CELL_PAD = "!px-1.5 sm:!px-4";
const TH_SMALL = "!text-[11px] !tracking-normal sm:!text-[13px] sm:!tracking-wide";

export default function SimpleCatalogPage({ prefix, titleKey, load, create, update, deactivate }: Props) {
  const { t } = useI18n();
  const { has } = usePermissions();
  const toast = useToast();
  const canManage = has("Products.Update");

  const { data, error, loading, reload } = useAsyncData(load, "list");
  const showCount = !!data && data.some((i) => i.productCount !== undefined);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<CatalogItem | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [isActive, setIsActive] = useState(true);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [toDeactivate, setToDeactivate] = useState<CatalogItem | null>(null);
  const [deactivating, setDeactivating] = useState(false);

  function openCreate() {
    setEditing(null);
    setName("");
    setDescription("");
    setIsActive(true);
    setFormError(null);
    setModalOpen(true);
  }

  function openEdit(item: CatalogItem) {
    setEditing(item);
    setName(item.name);
    setDescription(item.description ?? "");
    setIsActive(item.isActive);
    setFormError(null);
    setModalOpen(true);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (saving) return;
    if (!name.trim()) {
      setFormError(t("common.nameRequired"));
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      if (editing) {
        await update(editing.id, name.trim(), description.trim(), isActive);
      } else {
        await create(name.trim(), description.trim());
      }
      toast.success(t("common.saved"));
      setModalOpen(false);
      reload();
    } catch (err) {
      setFormError(errorStatus(err) === 409 ? t(`${prefix}.exists`) : t("common.saveFailed"));
    } finally {
      setSaving(false);
    }
  }

  async function handleActivate(item: CatalogItem) {
    try {
      await update(item.id, item.name, item.description ?? "", true);
      toast.success(t("common.activated"));
      reload();
    } catch {
      toast.error(t("common.saveFailed"));
    }
  }

  async function handleDeactivate() {
    if (!toDeactivate) return;
    setDeactivating(true);
    try {
      await deactivate(toDeactivate.id);
      toast.success(t("common.deactivated"));
      setToDeactivate(null);
      reload();
    } catch {
      toast.error(t("common.deactivateFailed"));
    } finally {
      setDeactivating(false);
    }
  }

  return (
    <div className="mx-auto max-w-4xl">
      <PageHeader
        title={t(titleKey)}
        actions={canManage ? <Button onClick={openCreate}>+ {t(`${prefix}.add`)}</Button> : undefined}
      />

      <Card className="overflow-hidden !p-0">
        {loading && <LoadingSkeleton />}
        {error && <ErrorState message={t("common.loadFailed")} onRetry={reload} />}
        {!loading && !error && data && data.length === 0 && <EmptyState message={t(`${prefix}.empty`)} />}
        {!loading && !error && data && data.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead className="bg-slate-50">
                <tr>
                  <th className={`${thClass} ${CELL_PAD} ${TH_SMALL}`}>{t("common.name")}</th>
                  <th className={`${thClass} ${CELL_PAD} ${TH_SMALL} hidden sm:table-cell`}>{t("common.description")}</th>
                  {showCount && <th className={`${thClass} ${CELL_PAD} ${TH_SMALL} text-right`}>{t("cat.productCount")}</th>}
                  <th className={`${thClass} ${CELL_PAD} ${TH_SMALL} hidden sm:table-cell`}>{t("common.status")}</th>
                  {canManage && <th className={`${thClass} ${CELL_PAD} ${TH_SMALL}`}>{t("common.actions")}</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.map((item) => (
                  <tr key={item.id} className="hover:bg-slate-50">
                    <td className={`${tdClass} ${CELL_PAD} font-medium text-slate-900`}>
                      {item.name}
                      <div className="mt-1 sm:hidden">
                        <StatusBadge
                          label={item.isActive ? t("common.active") : t("common.inactive")}
                          tone={item.isActive ? "green" : "gray"}
                        />
                      </div>
                    </td>
                    <td className={`${tdClass} ${CELL_PAD} hidden sm:table-cell`}>{item.description || "—"}</td>
                    {showCount && <td className={`${tdClass} ${CELL_PAD} text-right`}>
                        {item.productCount ?? "—"}
                        {item.totalProductCount !== undefined && item.productCount !== undefined && item.totalProductCount > item.productCount && (
                          <span className="ml-1 text-xs text-slate-400" title={t("cat.productCountTotal")}>/ {item.totalProductCount}</span>
                        )}
                      </td>}
                    <td className={`${tdClass} ${CELL_PAD} hidden sm:table-cell`}>
                      <StatusBadge
                        label={item.isActive ? t("common.active") : t("common.inactive")}
                        tone={item.isActive ? "green" : "gray"}
                      />
                    </td>
                    {canManage && (
                      <td className={`${tdClass} ${CELL_PAD}`}>
                        <div className="flex flex-col items-start gap-1 sm:flex-row sm:items-center sm:gap-2">
                          <Button size="sm" variant="secondary" onClick={() => openEdit(item)}>
                            {t("common.edit")}
                          </Button>
                          {item.isActive ? (
                            <Button size="sm" variant="ghost" className="!text-red-600" onClick={() => setToDeactivate(item)}>
                              {t("common.deactivate")}
                            </Button>
                          ) : (
                            <Button size="sm" variant="ghost" className="!text-green-700" onClick={() => handleActivate(item)}>
                              {t("common.activate")}
                            </Button>
                          )}
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={modalOpen}
        title={editing ? t(`${prefix}.edit`) : t(`${prefix}.add`)}
        onClose={() => !saving && setModalOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setModalOpen(false)} disabled={saving}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" form="catalog-form" loading={saving}>
              {t("common.save")}
            </Button>
          </>
        }
      >
        <form id="catalog-form" onSubmit={handleSubmit} className="space-y-4">
          <FormField label={t("common.name")} required>
            <input className={inputClass} value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </FormField>
          <FormField label={`${t("common.description")} (${t("common.optional")})`}>
            <input className={inputClass} value={description} onChange={(e) => setDescription(e.target.value)} />
          </FormField>
          {editing && (
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              {t("common.active")}
            </label>
          )}
          {formError && <p className="text-sm text-red-600">{formError}</p>}
        </form>
      </Modal>

      <ConfirmDialog
        open={toDeactivate !== null}
        title={t("common.deactivate")}
        message={t(`${prefix}.confirm`)}
        confirmLabel={t("common.deactivate")}
        danger
        loading={deactivating}
        onConfirm={handleDeactivate}
        onCancel={() => setToDeactivate(null)}
      />
    </div>
  );
}
