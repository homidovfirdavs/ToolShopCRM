import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  getCategories,
  getBrands,
  getProduct,
  createProduct,
  updateProduct,
  type Brand,
  type Category,
  type Product,
} from "../api/catalog";
import { getSingleStock, type StockItem } from "../api/inventory";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { errorMessage, errorStatus } from "../lib/apiError";
import { formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import FormField from "../components/ui/FormField";
import StatusBadge from "../components/ui/StatusBadge";
import { ErrorState, LoadingSkeleton } from "../components/ui/States";
import { useToast } from "../components/ui/useToast";
import { inputClass } from "../components/ui/formStyles";

export default function ProductFormPage() {
  const { id } = useParams();
  const { t } = useI18n();

  const { data, loading, error, reload } = useAsyncData(async () => {
    const [categories, brands, product, stock] = await Promise.all([
      getCategories(),
      getBrands(),
      id ? getProduct(Number(id)) : Promise.resolve(null),
      id ? getSingleStock(Number(id)).catch(() => null) : Promise.resolve(null),
    ]);
    return { categories, brands, product, stock };
  }, id ?? "new");

  if (loading) return <LoadingSkeleton rows={8} />;
  if (error || !data) return <ErrorState message={t("pf.notFound")} onRetry={reload} />;

  return (
    <ProductForm
      key={id ?? "new"}
      categories={data.categories}
      brands={data.brands}
      product={data.product}
      stock={data.stock}
    />
  );
}

function ProductForm({
  categories,
  brands,
  product,
  stock,
}: {
  categories: Category[];
  brands: Brand[];
  product: Product | null;
  stock: StockItem | null;
}) {
  const { t } = useI18n();
  const toast = useToast();
  const navigate = useNavigate();
  const isEdit = product !== null;

  const [form, setForm] = useState({
    name: product?.name ?? "",
    sku: product?.sku ?? "",
    barcode: product?.barcode ?? "",
    categoryId: product ? String(product.categoryId) : "",
    brandId: product ? String(product.brandId) : "",
    purchasePrice: product ? String(product.purchasePrice) : "",
    salePrice: product ? String(product.salePrice) : "",
    minimumStockLevel: product ? String(product.minimumStockLevel) : "0",
    warrantyMonths: product ? String(product.warrantyMonths) : "0",
    description: product?.description ?? "",
    imageUrl: product?.imageUrl ?? "",
    isActive: product?.isActive ?? true,
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  function set<K extends keyof typeof form>(key: K, value: (typeof form)[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function validate(): boolean {
    const e: Record<string, string> = {};
    if (!form.name.trim()) e.name = t("pf.errName");
    if (!form.sku.trim()) e.sku = t("pf.errSku");
    if (!form.categoryId) e.categoryId = t("pf.errCategory");
    if (!form.brandId) e.brandId = t("pf.errBrand");
    if (form.purchasePrice === "" || Number(form.purchasePrice) < 0) e.purchasePrice = t("pf.errPurchase");
    if (form.salePrice === "" || Number(form.salePrice) < 0) e.salePrice = t("pf.errSale");
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (saving) return;
    setGeneralError(null);
    if (!validate()) return;

    setSaving(true);
    const payload = {
      name: form.name.trim(),
      sku: form.sku.trim(),
      barcode: form.barcode.trim(),
      categoryId: Number(form.categoryId),
      brandId: Number(form.brandId),
      purchasePrice: Number(form.purchasePrice),
      salePrice: Number(form.salePrice),
      minimumStockLevel: Number(form.minimumStockLevel || 0),
      warrantyMonths: Number(form.warrantyMonths || 0),
      description: form.description.trim(),
      imageUrl: form.imageUrl.trim(),
      isActive: form.isActive,
    };

    try {
      if (product) await updateProduct(product.id, payload);
      else await createProduct(payload);
      toast.success(t("pf.saved"));
      navigate("/products");
    } catch (err) {
      const status = errorStatus(err);
      if (status === 409) setGeneralError(errorMessage(err) ?? t("pf.exists"));
      else if (status === 400) setGeneralError(errorMessage(err) ?? t("pf.checkForm"));
      else setGeneralError(t("common.somethingWrong"));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-3xl">
      <PageHeader
        title={isEdit ? t("pf.edit") : t("pf.new")}
        subtitle={product?.sku}
        actions={
          <Link to="/products">
            <Button variant="secondary">{t("common.back")}</Button>
          </Link>
        }
      />

      <Card>
        <form onSubmit={handleSubmit} className="space-y-4">
          {generalError && (
            <p className="rounded-lg bg-red-50 p-3 text-sm text-red-700" role="alert">
              {generalError}
            </p>
          )}

          <FormField label={t("pf.name")} required error={errors.name}>
            <input className={inputClass} value={form.name} onChange={(e) => set("name", e.target.value)} autoFocus />
          </FormField>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label="SKU" required error={errors.sku}>
              <input className={inputClass} value={form.sku} onChange={(e) => set("sku", e.target.value)} />
            </FormField>
            <FormField label={t("pf.barcode")}>
              <input className={inputClass} value={form.barcode} onChange={(e) => set("barcode", e.target.value)} />
            </FormField>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label={t("prod.category")} required error={errors.categoryId}>
              <select className={inputClass} value={form.categoryId} onChange={(e) => set("categoryId", e.target.value)}>
                <option value="">{t("pf.selectCategory")}</option>
                {categories
                  .filter((c) => c.isActive || String(c.id) === form.categoryId)
                  .map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
              </select>
            </FormField>
            <FormField label={t("prod.brand")} required error={errors.brandId}>
              <select className={inputClass} value={form.brandId} onChange={(e) => set("brandId", e.target.value)}>
                <option value="">{t("pf.selectBrand")}</option>
                {brands
                  .filter((b) => b.isActive || String(b.id) === form.brandId)
                  .map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
              </select>
            </FormField>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label={t("prod.purchase")} required error={errors.purchasePrice}>
              <input
                type="number"
                min="0"
                step="0.01"
                className={inputClass}
                value={form.purchasePrice}
                onChange={(e) => set("purchasePrice", e.target.value)}
              />
            </FormField>
            <FormField label={t("prod.sale")} required error={errors.salePrice}>
              <input
                type="number"
                min="0"
                step="0.01"
                className={inputClass}
                value={form.salePrice}
                onChange={(e) => set("salePrice", e.target.value)}
              />
            </FormField>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label={t("pf.minStock")}>
              <input
                type="number"
                min="0"
                className={inputClass}
                value={form.minimumStockLevel}
                onChange={(e) => set("minimumStockLevel", e.target.value)}
              />
            </FormField>
            <FormField label={t("pf.warranty")}>
              <input
                type="number"
                min="0"
                className={inputClass}
                value={form.warrantyMonths}
                onChange={(e) => set("warrantyMonths", e.target.value)}
              />
            </FormField>
          </div>

          <FormField label={t("pf.description")}>
            <textarea
              className={inputClass}
              rows={3}
              value={form.description}
              onChange={(e) => set("description", e.target.value)}
            />
          </FormField>

          <FormField label={t("pf.imageUrl")}>
            <input className={inputClass} value={form.imageUrl} onChange={(e) => set("imageUrl", e.target.value)} />
          </FormField>

          {isEdit && (
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input type="checkbox" checked={form.isActive} onChange={(e) => set("isActive", e.target.checked)} />
              {t("common.active")}
            </label>
          )}

          {isEdit && stock && (
            <div className="rounded-lg bg-slate-50 p-3 text-sm">
              <p className="mb-1 font-semibold text-slate-800">{t("pf.inventory")}</p>
              <p className="flex flex-wrap items-center gap-2 text-slate-600">
                {t("pf.currentStock")}: <strong>{formatNumber(stock.currentStock)}</strong> · {t("inv.minimum")}:{" "}
                {formatNumber(stock.minimumStockLevel)} ·{" "}
                <StatusBadge
                  label={t(`inv.status.${stock.status}`)}
                  tone={stock.status === "InStock" ? "green" : stock.status === "LowStock" ? "yellow" : "red"}
                />
              </p>
              <Link to="/inventory/movements" className="mt-1 inline-block text-blue-600 hover:underline">
                {t("pf.viewMovements")}
              </Link>
            </div>
          )}

          <div className="flex gap-3 pt-2">
            <Button type="submit" loading={saving}>
              {t("common.save")}
            </Button>
            <Button variant="secondary" onClick={() => navigate("/products")} disabled={saving}>
              {t("common.cancel")}
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
