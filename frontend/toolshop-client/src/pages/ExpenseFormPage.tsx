import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  getExpenseCategories,
  createExpense,
  updateExpense,
  getExpense,
  type Expense,
  type ExpenseCategory,
} from "../api/finance";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { toDateStr } from "../lib/dateRange";
import { errorMessage } from "../lib/apiError";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import FormField from "../components/ui/FormField";
import { ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass } from "../components/ui/formStyles";

export default function ExpenseFormPage() {
  const { id } = useParams();
  const { t } = useI18n();

  const { data, loading, error, reload } = useAsyncData(async () => {
    const [categories, expense] = await Promise.all([
      getExpenseCategories(true),
      id ? getExpense(Number(id)) : Promise.resolve(null),
    ]);
    return { categories, expense };
  }, id ?? "new");

  if (loading) return <LoadingSkeleton rows={6} />;
  if (error || !data) return <ErrorState message={t("exp.notFound")} onRetry={reload} />;

  return <ExpenseForm key={id ?? "new"} categories={data.categories} expense={data.expense} />;
}

function ExpenseForm({ categories, expense }: { categories: ExpenseCategory[]; expense: Expense | null }) {
  const { t } = useI18n();
  const toast = useToast();
  const navigate = useNavigate();
  const isEdit = expense !== null;
  const readOnly = expense?.isVoided ?? false;

  const [categoryId, setCategoryId] = useState(expense ? String(expense.categoryId) : "");
  const [amount, setAmount] = useState(expense ? String(expense.amount) : "");
  const [expenseDate, setExpenseDate] = useState(
    expense ? expense.expenseDate.slice(0, 10) : toDateStr(new Date()),
  );
  const [description, setDescription] = useState(expense?.description ?? "");
  const [referenceNumber, setReferenceNumber] = useState(expense?.referenceNumber ?? "");
  const [notes, setNotes] = useState(expense?.notes ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSave(e: FormEvent) {
    e.preventDefault();
    if (saving || readOnly) return;
    setError(null);

    if (!categoryId) return setError(t("exp.errCategory"));
    const amountNum = Number(amount);
    if (!amountNum || amountNum <= 0) return setError(t("exp.errAmount"));
    if (!description.trim()) return setError(t("exp.errDescription"));

    setSaving(true);
    try {
      const ref = referenceNumber.trim() || null;
      const note = notes.trim() || null;
      if (expense) {
        await updateExpense(expense.id, Number(categoryId), amountNum, expenseDate, description.trim(), ref, note);
      } else {
        await createExpense(Number(categoryId), amountNum, expenseDate, description.trim(), ref, note);
      }
      toast.success(t("exp.saved"));
      navigate("/expenses");
    } catch (err) {
      setError(errorMessage(err) ?? t("common.saveFailed"));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <PageHeader
        title={isEdit ? t("exp.formEdit") : t("exp.formNew")}
        subtitle={expense?.expenseNumber}
        actions={
          <Link to="/expenses">
            <Button variant="secondary">{t("common.back")}</Button>
          </Link>
        }
      />

      {readOnly && <p className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{t("exp.readonly")}</p>}

      <Card>
        <form onSubmit={handleSave} className="space-y-4">
          <FormField label={t("exp.category")} required>
            <select
              className={inputClass}
              value={categoryId}
              disabled={readOnly}
              onChange={(e) => setCategoryId(e.target.value)}
            >
              <option value="">{t("exp.selectCategory")}</option>
              {categories
                .filter((c) => c.isActive || String(c.id) === categoryId)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                    {!c.isActive ? ` ${t("exp.inactiveSuffix")}` : ""}
                  </option>
                ))}
            </select>
          </FormField>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label={t("common.amount")} required>
              <input
                type="number"
                min="0"
                step="0.01"
                className={inputClass}
                value={amount}
                disabled={readOnly}
                onChange={(e) => setAmount(e.target.value)}
              />
            </FormField>
            <FormField label={t("exp.date")} required>
              <input
                type="date"
                className={inputClass}
                value={expenseDate}
                disabled={readOnly}
                onChange={(e) => setExpenseDate(e.target.value)}
              />
            </FormField>
          </div>

          <FormField label={t("exp.description")} required>
            <input
              className={inputClass}
              value={description}
              disabled={readOnly}
              onChange={(e) => setDescription(e.target.value)}
            />
          </FormField>

          <FormField label={t("exp.reference")}>
            <input
              className={inputClass}
              value={referenceNumber}
              disabled={readOnly}
              onChange={(e) => setReferenceNumber(e.target.value)}
            />
          </FormField>

          <FormField label={t("exp.notes")}>
            <textarea
              className={inputClass}
              rows={3}
              value={notes}
              disabled={readOnly}
              onChange={(e) => setNotes(e.target.value)}
            />
          </FormField>

          {error && (
            <p className="text-sm text-red-600" role="alert">
              {error}
            </p>
          )}

          {!readOnly && (
            <div className="flex gap-3">
              <Button type="submit" loading={saving}>
                {t("common.save")}
              </Button>
              <Button variant="secondary" onClick={() => navigate("/expenses")} disabled={saving}>
                {t("common.cancel")}
              </Button>
            </div>
          )}
        </form>
      </Card>
    </div>
  );
}
