import { useMemo, useState } from "react";
import { LayoutGrid, Search, ShoppingCart } from "lucide-react";
import type { Category } from "../../api/catalog";
import { useI18n } from "../../i18n/useI18n";
import { useCategoryNames } from "../../lib/categoryNames";
import { ErrorState } from "../ui/States";
import { inputClass } from "../ui/formStyles";
import CategoryCard from "./CategoryCard";
import { categoryIcon, categoryTone, TONES } from "./categoryVisuals";

interface Props {
  /** Categories exactly as the API returned them (the panel shows only the active ones). */
  categories: Category[] | null;
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  selectedId: number | null;
  onSelect: (id: number | null) => void;
}

export default function CategoryPanel({ categories, loading, error, onRetry, selectedId, onSelect }: Props) {
  const { t, lang } = useI18n();
  const catNames = useCategoryNames();
  const [search, setSearch] = useState("");
  const [open, setOpen] = useState(true);

  const active = useMemo(() => (categories ?? []).filter((c) => c.isActive), [categories]);
  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    // the search matches the name in any language (and the main name), whatever the UI language is
    return q
      ? active.filter((c) => [c.name, c.nameTj, c.nameRu, c.nameEn].some((n) => n?.toLowerCase().includes(q)))
      : active;
  }, [active, search]);

  function countLabel(n: number | undefined): string | undefined {
    if (typeof n !== "number") return undefined;
    // Russian needs three noun forms; EN has singular/plural; TJ has one form.
    const mod10 = n % 10;
    const mod100 = n % 100;
    let key = "pos.catCountMany";
    if (lang === "ru") {
      if (mod10 === 1 && mod100 !== 11) key = "pos.catCountOne";
      else if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) key = "pos.catCountFew";
    } else if (lang === "en" && n === 1) key = "pos.catCountOne";
    return `${n} ${t(key)}`;
  }

  function select(id: number | null) {
    onSelect(id);
    // On phones the product list is far below the cards; bring it into view.
    window.setTimeout(() => {
      const el = document.getElementById("pos-products");
      if (el && el.getBoundingClientRect().top > window.innerHeight * 0.6) el.scrollIntoView?.({ behavior: "smooth", block: "start" });
    }, 0);
  }

  return (
    <section
      aria-label={t("pos.catTitle")}
      className="@container min-w-0 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5"
    >
      <div className="flex flex-wrap items-center gap-3">
        <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-blue-600 text-white shadow-sm" aria-hidden="true">
          <ShoppingCart size={24} />
        </span>
        <div className="min-w-0 flex-1 basis-48">
          <h2 className="text-xl font-bold leading-tight text-slate-900">{t("pos.catTitle")}</h2>
          <p className="text-sm text-slate-500">{t("pos.catSubtitle")}</p>
        </div>
        <div className="flex w-full items-center gap-2 @[40rem]:w-auto @[40rem]:min-w-[20rem]">
          <div className="relative min-w-0 flex-1">
            <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("pos.catSearch")}
              aria-label={t("pos.catSearch")}
              className={`${inputClass} !rounded-xl pl-9`}
            />
          </div>
          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            aria-expanded={open}
            aria-label={open ? t("pos.catHide") : t("pos.catShow")}
            title={open ? t("pos.catHide") : t("pos.catShow")}
            className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 ${
              open ? "border-blue-200 bg-blue-50 text-blue-600" : "border-slate-200 bg-white text-slate-500 hover:bg-slate-50"
            }`}
          >
            <LayoutGrid size={18} />
          </button>
        </div>
      </div>

      {open && (
        <div className="mt-4">
          {loading && (
            <div className="grid grid-cols-1 gap-3 @[34rem]:grid-cols-2 @[52rem]:grid-cols-3 @[66rem]:grid-cols-4" aria-busy="true">
              {Array.from({ length: 4 }, (_, i) => (
                <div key={i} className="h-[84px] animate-pulse rounded-2xl bg-slate-100" />
              ))}
            </div>
          )}
          {!loading && error && <ErrorState message={t("common.loadFailed")} onRetry={onRetry} />}
          {!loading && !error && active.length === 0 && <p className="py-6 text-center text-sm text-slate-500">{t("pos.noCategories")}</p>}
          {!loading && !error && active.length > 0 && (
            <>
              <div className="grid grid-cols-1 gap-3 @[34rem]:grid-cols-2 @[52rem]:grid-cols-3 @[66rem]:grid-cols-4">
                {!search.trim() && (
                  <CategoryCard
                    name={t("pos.allCategoriesLong")}
                    icon={LayoutGrid}
                    tone={TONES[0]}
                    selected={selectedId === null}
                    onSelect={() => select(null)}
                  />
                )}
                {visible.map((c) => (
                  <CategoryCard
                    key={c.id}
                    name={catNames.of(c)}
                    icon={categoryIcon(`${c.name} ${c.nameTj ?? ""} ${c.nameRu ?? ""} ${c.nameEn ?? ""}`)}
                    tone={categoryTone(c.id)}
                    countLabel={countLabel(c.productCount)}
                    selected={selectedId === c.id}
                    onSelect={() => select(c.id)}
                  />
                ))}
              </div>
              {visible.length === 0 && <p className="py-6 text-center text-sm text-slate-500">{t("pos.catNoMatch")}</p>}
            </>
          )}
        </div>
      )}
    </section>
  );
}
