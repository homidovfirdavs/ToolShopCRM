import { useCallback, useEffect, useMemo, useSyncExternalStore } from "react";
import { getCategories } from "../api/catalog";
import { useI18n } from "../i18n/useI18n";
import type { Lang } from "../i18n/translations";

/** The parts of a category that matter for display. Translations are optional and display-only. */
export interface CategoryNameParts {
  name: string;
  nameTj?: string | null;
  nameRu?: string | null;
  nameEn?: string | null;
}

/** Name in the selected language; an empty/missing translation falls back to the main name. */
export function localizedCategoryName(c: CategoryNameParts, lang: Lang): string {
  const v = lang === "tj" ? c.nameTj : lang === "ru" ? c.nameRu : c.nameEn;
  return v?.trim() || c.name;
}

// ---- small shared store: some API rows carry only the category's main name (Products, Inventory,
// Reports, Dashboard). The main name is unique, so those rows are translated by looking it up here.
// Ids, filters and report grouping are never touched: this only changes the text that is shown.

let byName = new Map<string, CategoryNameParts>();
let version = 0;
let loaded = false;
let inFlight: Promise<void> | null = null;
const listeners = new Set<() => void>();

function emit() {
  version += 1;
  listeners.forEach((l) => l());
}

/** (Re)loads the category names. Called when a category is created or edited. Never throws. */
export function refreshCategoryNames(): Promise<void> {
  if (inFlight) return inFlight;
  inFlight = (async () => {
    try {
      const list = await getCategories();
      byName = new Map(list.map((c) => [c.name, c]));
      loaded = true;
      emit();
    } catch {
      /* translations are cosmetic: on failure the main name is shown */
    } finally {
      inFlight = null;
    }
  })();
  return inFlight;
}

/** Test helper. */
export function resetCategoryNames() {
  byName = new Map();
  loaded = false;
  inFlight = null;
  emit();
}

function subscribe(l: () => void) {
  listeners.add(l);
  return () => {
    listeners.delete(l);
  };
}
const getVersion = () => version;

/**
 * Category display names in the current language. Re-renders immediately when the language changes
 * (no reload) and when category translations are edited.
 */
export function useCategoryNames() {
  const { lang } = useI18n();
  useSyncExternalStore(subscribe, getVersion, getVersion);

  useEffect(() => {
    if (!loaded) void refreshCategoryNames();
  }, []);

  /** For a row that only has the category's main name. Unknown name → shown as is. */
  const label = useCallback(
    (name: string | null | undefined): string => {
      if (!name) return "";
      const c = byName.get(name);
      return c ? localizedCategoryName(c, lang) : name;
    },
    // `version` makes the callback change when names load, so memoised children update.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [lang, version],
  );

  /** For a full category object from the API. */
  const of = useCallback((c: CategoryNameParts): string => localizedCategoryName(c, lang), [lang]);

  return useMemo(() => ({ label, of, lang }), [label, of, lang]);
}
