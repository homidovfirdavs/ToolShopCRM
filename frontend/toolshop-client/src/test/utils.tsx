/* eslint-disable react-refresh/only-export-components -- test helpers, not app components */
import type { ReactElement } from "react";
import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { I18nProvider } from "../i18n/I18nProvider";
import { ToastProvider } from "../components/ui/ToastProvider";
import { translations, type Lang } from "../i18n/translations";

/** Renders inside router + i18n + toast providers. Language defaults to English so assertions use stable text. */
export function renderApp(ui: ReactElement, opts: { lang?: Lang; route?: string } = {}) {
  localStorage.setItem("toolshop_lang", opts.lang ?? "en");
  return render(
    <MemoryRouter initialEntries={[opts.route ?? "/"]}>
      <I18nProvider>
        <ToastProvider>{ui}</ToastProvider>
      </I18nProvider>
    </MemoryRouter>,
  );
}

/** English text for a translation key (so tests do not hard-code UI strings). */
export function T(key: string, lang: Lang = "en"): string {
  return translations[lang][key] ?? key;
}
