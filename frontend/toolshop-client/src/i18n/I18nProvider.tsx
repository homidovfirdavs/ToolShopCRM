import { useCallback, useMemo, useState, type ReactNode } from "react";
import { I18nContext } from "./i18nContext";
import { translations, type Lang } from "./translations";

const STORAGE_KEY = "toolshop_lang";

function readInitialLang(): Lang {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    if (v === "tj" || v === "ru" || v === "en") return v;
  } catch {
    /* ignore */
  }
  return "tj";
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(readInitialLang);

  const setLang = useCallback((l: Lang) => {
    setLangState(l);
    try {
      localStorage.setItem(STORAGE_KEY, l);
    } catch {
      /* ignore */
    }
  }, []);

  const t = useCallback(
    (key: string) => translations[lang][key] ?? translations.en[key] ?? key,
    [lang],
  );

  const value = useMemo(() => ({ lang, setLang, t }), [lang, setLang, t]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}
