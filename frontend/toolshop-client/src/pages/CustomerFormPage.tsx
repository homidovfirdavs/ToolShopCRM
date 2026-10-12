import { useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { getCustomer, createCustomer, updateCustomer } from "../api/customers";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorMessage, errorStatus } from "../lib/apiError";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import FormField from "../components/ui/FormField";
import { ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass } from "../components/ui/formStyles";

interface FormState {
  name: string;
  phone: string;
  address: string;
  notes: string;
  isActive: boolean;
}

const EMPTY: FormState = { name: "", phone: "", address: "", notes: "", isActive: true };

export default function CustomerFormPage() {
  const { id } = useParams();
  const isEdit = Boolean(id);
  const { t } = useI18n();

  const loaded = useAsyncData(async () => {
    if (!id) return EMPTY;
    const c = await getCustomer(Number(id));
    return {
      name: c.name,
      phone: c.phone,
      address: c.address ?? "",
      notes: c.notes ?? "",
      isActive: c.isActive,
    } as FormState;
  }, id ?? "new");

  if (loaded.loading) return <LoadingSkeleton rows={6} />;
  if (loaded.error || !loaded.data) return <ErrorState message={t("cust.notFound")} onRetry={loaded.reload} />;

  // Key forces a fresh form state whenever a different customer is loaded.
  return <CustomerForm key={id ?? "new"} id={id} isEdit={isEdit} initial={loaded.data} />;
}

function CustomerForm({ id, isEdit, initial }: { id?: string; isEdit: boolean; initial: FormState }) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const toast = useToast();

  const [form, setForm] = useState<FormState>(initial);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [generalError, setGeneralError] = useState<string | null>(null);

  function validate(): boolean {
    const e: Record<string, string> = {};
    if (!form.name.trim()) e.name = t("cust.nameRequired");
    if (!form.phone.trim()) e.phone = t("cust.phoneRequired");
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(ev: FormEvent) {
    ev.preventDefault();
    if (saving) return;
    setGeneralError(null);
    if (!validate()) return;

    setSaving(true);
    const payload = {
      name: form.name.trim(),
      phone: form.phone.trim(),
      address: form.address.trim(),
      notes: form.notes.trim(),
      isActive: form.isActive,
    };

    try {
      if (isEdit && id) await updateCustomer(Number(id), payload);
      else await createCustomer(payload);
      toast.success(t("common.saved"));
      navigate("/customers");
    } catch (err) {
      const status = errorStatus(err);
      if (status === 409) setGeneralError(errorMessage(err) ?? t("cust.phoneExists"));
      else if (status === 400) setGeneralError(errorMessage(err) ?? t("common.saveFailed"));
      else setGeneralError(t("common.somethingWrong"));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-xl">
      <PageHeader title={isEdit ? t("cust.edit") : t("cust.add")} />
      <Card>
        {generalError && (
          <p className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700" role="alert">
            {generalError}
          </p>
        )}
        <form onSubmit={handleSubmit} className="space-y-4" noValidate>
          <FormField label={t("common.name")} required error={errors.name}>
            <input className={inputClass} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </FormField>
          <FormField label={t("cust.phone")} required error={errors.phone}>
            <input
              className={inputClass}
              value={form.phone}
              placeholder="+992 900 00 00 00"
              onChange={(e) => setForm({ ...form, phone: e.target.value })}
            />
          </FormField>
          <FormField label={t("cust.address")}>
            <input className={inputClass} value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
          </FormField>
          <FormField label={t("cust.notes")}>
            <textarea
              className={inputClass}
              rows={3}
              value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
            />
          </FormField>
          {isEdit && (
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              />
              {t("common.active")}
            </label>
          )}
          <div className="flex gap-3 pt-2">
            <Button type="submit" loading={saving}>
              {t("common.save")}
            </Button>
            <Button variant="secondary" onClick={() => navigate("/customers")} disabled={saving}>
              {t("common.cancel")}
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
