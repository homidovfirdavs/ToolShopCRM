import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { createPurchase, type PurchaseItemInput } from "../api/purchases";
import { getSuppliers } from "../api/suppliers";
import { getProducts, type ProductListItem } from "../api/catalog";
import { useI18n } from "../i18n/useI18n";
import { useAsyncData } from "../lib/useAsyncData";
import { toDateStr } from "../lib/dateRange";
import { formatMoney, formatNumber } from "../lib/format";
import PageHeader from "../components/ui/PageHeader";
import Card from "../components/ui/Card";
import Button from "../components/ui/Button";
import FormField from "../components/ui/FormField";
import { useToast } from "../components/ui/useToast";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";

interface DraftItem extends PurchaseItemInput {
  productName: string;
  sku: string;
}

export default function PurchaseFormPage() {
  const { t } = useI18n();
  const toast = useToast();
  const navigate = useNavigate();

  const suppliers = useAsyncData(() => getSuppliers({ isActive: true, pageSize: 100 }), "active-suppliers");

  const [supplierId, setSupplierId] = useState("");
  const [purchaseDate, setPurchaseDate] = useState(toDateStr(new Date())); // F2: the shop-local day, not the UTC day
  const [notes, setNotes] = useState("");
  const [discountAmount, setDiscountAmount] = useState("0");
  const [items, setItems] = useState<DraftItem[]>([]);

  const [productSearch, setProductSearch] = useState("");
  const [productResults, setProductResults] = useState<ProductListItem[]>([]);
  const [quantity, setQuantity] = useState("1");
  const [unitCost, setUnitCost] = useState("");
  const [selectedProduct, setSelectedProduct] = useState<ProductListItem | null>(null);

  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);

  function onProductSearch(value: string) {
    setProductSearch(value);
    setSelectedProduct(null);
    window.clearTimeout(timer.current);
    if (!value.trim()) {
      setProductResults([]);
      return;
    }
    timer.current = window.setTimeout(() => {
      getProducts({ search: value, isActive: true, pageSize: 10 })
        .then((r) => setProductResults(r.items))
        .catch(() => setProductResults([]));
    }, 300);
  }

  function selectProduct(p: ProductListItem) {
    setSelectedProduct(p);
    setProductSearch(p.name);
    setProductResults([]);
    setUnitCost(p.purchasePrice != null ? String(p.purchasePrice) : "");
  }

  function addItem() {
    if (!selectedProduct) return setError(t("pur.errProduct"));
    const qty = Number(quantity);
    const cost = Number(unitCost);
    if (!qty || qty <= 0) return setError(t("pur.errQty"));
    if (isNaN(cost) || cost < 0) return setError(t("pur.errCost"));
    setError(null);

    const p = selectedProduct;
    setItems((prev) => {
      const idx = prev.findIndex((i) => i.productId === p.id);
      if (idx >= 0) {
        const updated = [...prev];
        updated[idx] = { ...updated[idx], quantity: updated[idx].quantity + qty, unitCost: cost };
        return updated;
      }
      return [...prev, { productId: p.id, productName: p.name, sku: p.sku, quantity: qty, unitCost: cost }];
    });

    setSelectedProduct(null);
    setProductSearch("");
    setQuantity("1");
    setUnitCost("");
  }

  function removeItem(productId: number) {
    setItems((prev) => prev.filter((i) => i.productId !== productId));
  }

  const subtotal = items.reduce((sum, i) => sum + i.quantity * i.unitCost, 0);
  const discount = Number(discountAmount) || 0;
  const total = Math.max(0, subtotal - discount);

  async function handleSave(e: FormEvent) {
    e.preventDefault();
    if (saving) return;
    setError(null);

    if (!supplierId) return setError(t("pur.errSupplier"));
    if (items.length === 0) return setError(t("pur.errItems"));
    if (discount < 0 || discount > subtotal) return setError(t("pur.errDiscount"));

    setSaving(true);
    try {
      await createPurchase({
        supplierId: Number(supplierId),
        purchaseDate: new Date(purchaseDate).toISOString(),
        notes: notes.trim(),
        discountAmount: discount,
        items: items.map((i) => ({ productId: i.productId, quantity: i.quantity, unitCost: i.unitCost })),
      });
      toast.success(t("pur.draftSaved"));
      navigate("/purchases");
    } catch {
      setError(t("pur.saveFailed"));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-4xl">
      <PageHeader
        title={t("pur.new.title")}
        actions={
          <Link to="/purchases">
            <Button variant="secondary">{t("common.back")}</Button>
          </Link>
        }
      />

      <form onSubmit={handleSave} className="space-y-4">
        <Card>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <FormField label={t("pur.supplier")} required>
              <select className={inputClass} value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
                <option value="">{t("pur.selectSupplier")}</option>
                {(suppliers.data?.items ?? []).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t("pur.date")} required>
              <input type="date" className={inputClass} value={purchaseDate} onChange={(e) => setPurchaseDate(e.target.value)} />
            </FormField>
            <FormField label={t("pur.notes")} className="sm:col-span-2">
              <input className={inputClass} value={notes} onChange={(e) => setNotes(e.target.value)} />
            </FormField>
          </div>
        </Card>

        <Card>
          <h2 className="mb-3 text-sm font-semibold text-slate-800">{t("pur.addProduct")}</h2>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-[1fr_6rem_8rem_auto]">
            <div className="relative col-span-2 sm:col-span-1">
              <input
                className={inputClass}
                placeholder={t("pur.searchProduct")}
                value={productSearch}
                onChange={(e) => onProductSearch(e.target.value)}
              />
              {productResults.length > 0 && (
                <ul className="absolute z-10 mt-1 max-h-56 w-full overflow-auto rounded-lg border border-slate-200 bg-white shadow-lg">
                  {productResults.map((p) => (
                    <li key={p.id}>
                      <button
                        type="button"
                        onClick={() => selectProduct(p)}
                        className="block w-full px-3 py-2 text-left text-sm hover:bg-slate-50"
                      >
                        {p.name} <span className="text-slate-400">— {p.sku}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <input
              type="number"
              min="0"
              className={inputClass}
              placeholder={t("pur.qty")}
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
            />
            <input
              type="number"
              min="0"
              step="0.01"
              className={inputClass}
              placeholder={t("pur.unitCost")}
              value={unitCost}
              onChange={(e) => setUnitCost(e.target.value)}
            />
            <Button variant="secondary" onClick={addItem}>
              + {t("pur.add")}
            </Button>
          </div>

          {items.length > 0 && (
            <div className="mt-4 overflow-x-auto">
              <table className="w-full">
                <thead className="bg-slate-50">
                  <tr>
                    <th className={thClass}>{t("inv.product")}</th>
                    <th className={thClass}>SKU</th>
                    <th className={`${thClass} text-right`}>{t("pur.qty")}</th>
                    <th className={`${thClass} text-right`}>{t("pur.unitCost")}</th>
                    <th className={`${thClass} text-right`}>{t("pur.lineTotal")}</th>
                    <th className={thClass}></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {items.map((i) => (
                    <tr key={i.productId}>
                      <td className={`${tdClass} font-medium text-slate-900`}>{i.productName}</td>
                      <td className={tdClass}>{i.sku}</td>
                      <td className={`${tdClass} text-right`}>{formatNumber(i.quantity)}</td>
                      <td className={`${tdClass} text-right`}>{formatMoney(i.unitCost)}</td>
                      <td className={`${tdClass} text-right`}>{formatMoney(i.quantity * i.unitCost)}</td>
                      <td className={tdClass}>
                        <Button size="sm" variant="ghost" className="!text-red-600" onClick={() => removeItem(i.productId)}>
                          {t("pur.remove")}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>

        <Card>
          <div className="ml-auto max-w-xs space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-slate-600">{t("pur.subtotal")}</span>
              <span>{formatMoney(subtotal)}</span>
            </div>
            <div className="flex items-center justify-between gap-3">
              <span className="text-slate-600">{t("pur.discount")}</span>
              <input
                type="number"
                min="0"
                step="0.01"
                className={`${inputClass} !w-28 text-right`}
                value={discountAmount}
                onChange={(e) => setDiscountAmount(e.target.value)}
              />
            </div>
            <div className="flex justify-between border-t border-slate-100 pt-2 text-base font-bold text-slate-900">
              <span>{t("pur.total")}</span>
              <span>{formatMoney(total)}</span>
            </div>
          </div>
        </Card>

        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}

        <div className="flex gap-3">
          <Button type="submit" loading={saving}>
            {t("pur.saveDraft")}
          </Button>
          <Button variant="secondary" onClick={() => navigate("/purchases")} disabled={saving}>
            {t("common.cancel")}
          </Button>
        </div>
      </form>
    </div>
  );
}
