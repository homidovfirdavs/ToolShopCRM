import { useEffect, useState } from "react";
import { getBrands, getCategories, type Category } from "../../api/catalog";
import { getInventoryReport, exportInventoryReport, type InventoryReportRow } from "../../api/reports";
import { ExportButton, Pager, FilterBar } from "./shared";
import { useI18n } from "../../i18n/useI18n";
import { useCategoryNames } from "../../lib/categoryNames";
import { formatMoney, statusLabel, styles } from "./reportUtils";

interface Filters {
  search: string;
  categoryId: string;
  brandId: string;
  stockStatus: string;
}

const EMPTY_FILTERS: Filters = { search: "", categoryId: "", brandId: "", stockStatus: "" };

function buildParams(f: Filters) {
  return {
    search: f.search.trim() || undefined,
    categoryId: f.categoryId ? Number(f.categoryId) : undefined,
    brandId: f.brandId ? Number(f.brandId) : undefined,
    stockStatus: f.stockStatus || undefined,
  };
}

function statusStyle(status: string) {
  if (status === "OutOfStock") return { color: "#b91c1c", fontWeight: 600 };
  if (status === "LowStock") return { color: "#c2410c", fontWeight: 600 };
  return { color: "#15803d", fontWeight: 600 };
}

export default function InventoryReportTab() {
  const { t } = useI18n();
  const catNames = useCategoryNames();
  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [page, setPage] = useState(1);

  const [categories, setCategories] = useState<Category[]>([]);
  const [brands, setBrands] = useState<{ id: number; name: string }[]>([]);

  const [rows, setRows] = useState<InventoryReportRow[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getCategories().then(setCategories).catch(console.error);
    getBrands().then(setBrands).catch(console.error);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const result = await getInventoryReport({ ...buildParams(applied), page, pageSize: 20 });
        if (cancelled) return;
        setRows(result.items);
        setTotalPages(result.totalPages);
        setTotalItems(result.totalItems);
        setError(null);
      } catch (err) {
        if (cancelled) return;
        console.error(err);
        setError("rep.loadError");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [applied, page]);

  function handleApply() {
    setPage(1);
    setApplied({ ...draft });
  }

  function handleReset() {
    setDraft(EMPTY_FILTERS);
    setApplied(EMPTY_FILTERS);
    setPage(1);
  }

  return (
    <div>
      <div style={styles.card}>
        <FilterBar
          onApply={handleApply}
          onReset={handleReset}
          exportNode={
            <ExportButton onExport={() => exportInventoryReport(buildParams(applied))} />
          }
          more={
            <>
          <label style={styles.field}>
            {t("rep.category")}
            <select style={styles.input} value={draft.categoryId} onChange={(e) => setDraft({ ...draft, categoryId: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {catNames.of(c)}
                </option>
              ))}
            </select>
          </label>
          <label style={styles.field}>
            {t("rep.brand")}
            <select style={styles.input} value={draft.brandId} onChange={(e) => setDraft({ ...draft, brandId: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              {brands.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
          </label>
          <label style={styles.field}>
            {t("rep.stockStatus")}
            <select style={styles.input} value={draft.stockStatus} onChange={(e) => setDraft({ ...draft, stockStatus: e.target.value })}>
              <option value="">{t("rep.all")}</option>
              <option value="Normal">{statusLabel(t, "Normal")}</option>
              <option value="LowStock">{statusLabel(t, "LowStock")}</option>
              <option value="OutOfStock">{statusLabel(t, "OutOfStock")}</option>
            </select>
          </label>
            </>
          }
        >
          <label style={styles.field}>
            {t("rep.searchProduct")}
            <input
              type="text"
              style={styles.input}
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleApply();
              }}
            />
          </label>
        </FilterBar>
      </div>

      <div style={styles.card}>
        {error && <p style={styles.error}>{t(error)}</p>}
        {loading ? (
          <p style={styles.muted}>{t("rep.loading")}</p>
        ) : (
          <>
            <div style={{ overflowX: "auto" }}>
              <table style={styles.table}>
                <thead>
                  <tr>
                    <th style={styles.th}>{t("rep.th.product")}</th>
                    <th style={styles.th}>SKU</th>
                    <th style={styles.th}>{t("rep.category")}</th>
                    <th style={styles.th}>{t("rep.brand")}</th>
                    <th style={styles.thRight}>{t("rep.th.stock")}</th>
                    <th style={styles.thRight}>{t("rep.th.min")}</th>
                    <th style={styles.th}>{t("rep.status")}</th>
                    <th style={styles.thRight}>{t("rep.th.buyPrice")}</th>
                    <th style={styles.thRight}>{t("rep.th.sellPrice")}</th>
                    <th style={styles.thRight}>{t("rep.th.stockValue")}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={10} style={styles.empty}>
                        {t("rep.noProducts")}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => (
                    <tr key={r.productId}>
                      <td style={styles.td}>{r.productName}</td>
                      <td style={styles.td}>{r.sku}</td>
                      <td style={styles.td}>{catNames.label(r.categoryName)}</td>
                      <td style={styles.td}>{r.brandName}</td>
                      <td style={styles.tdRight}>{r.currentStock}</td>
                      <td style={styles.tdRight}>{r.minimumStockLevel}</td>
                      <td style={{ ...styles.td, ...statusStyle(r.stockStatus) }}>{statusLabel(t, r.stockStatus)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.purchasePrice)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.salePrice)}</td>
                      <td style={styles.tdRight}>{formatMoney(r.stockValue)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager page={page} totalPages={totalPages} totalItems={totalItems} onPage={setPage} />
          </>
        )}
      </div>
    </div>
  );
}