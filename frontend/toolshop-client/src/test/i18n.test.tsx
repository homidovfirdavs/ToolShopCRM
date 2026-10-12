import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nProvider } from "../i18n/I18nProvider";
import { useI18n } from "../i18n/useI18n";
import { translations } from "../i18n/translations";

function Probe({ k }: { k: string }) {
  const { t, lang } = useI18n();
  return (
    <p>
      {lang}:{t(k)}
    </p>
  );
}

describe("translations", () => {
  const en = Object.keys(translations.en);

  it("every English key exists in Russian and Tajik", () => {
    const missingRu = en.filter((k) => !(k in translations.ru));
    const missingTj = en.filter((k) => !(k in translations.tj));
    expect({ missingRu, missingTj }).toEqual({ missingRu: [], missingTj: [] });
  });

  it("Russian and Tajik have no keys that English lacks", () => {
    const extraRu = Object.keys(translations.ru).filter((k) => !(k in translations.en));
    const extraTj = Object.keys(translations.tj).filter((k) => !(k in translations.en));
    expect({ extraRu, extraTj }).toEqual({ extraRu: [], extraTj: [] });
  });

  it("no translation is empty", () => {
    const empty: string[] = [];
    for (const lang of ["en", "ru", "tj"] as const) {
      for (const [k, v] of Object.entries(translations[lang])) {
        if (!v || !v.trim()) empty.push(`${lang}:${k}`);
      }
    }
    expect(empty).toEqual([]);
  });

  it("placeholders such as {amount} and {n} are kept in every language", () => {
    const bad: string[] = [];
    for (const k of en) {
      const ph = (translations.en[k].match(/\{[a-zA-Z]+\}/g) ?? []).sort().join(",");
      for (const lang of ["ru", "tj"] as const) {
        const other = (translations[lang][k]?.match(/\{[a-zA-Z]+\}/g) ?? []).sort().join(",");
        if (ph !== other) bad.push(`${lang}:${k}`);
      }
    }
    expect(bad).toEqual([]);
  });
});

describe("I18nProvider", () => {
  it("defaults to Tajik", () => {
    render(
      <I18nProvider>
        <Probe k="nav.dashboard" />
      </I18nProvider>,
    );
    expect(screen.getByText(`tj:${translations.tj["nav.dashboard"]}`)).toBeInTheDocument();
  });

  it("uses the stored language and falls back to the key for unknown keys", () => {
    localStorage.setItem("toolshop_lang", "ru");
    render(
      <I18nProvider>
        <Probe k="no.such.key" />
      </I18nProvider>,
    );
    expect(screen.getByText("ru:no.such.key")).toBeInTheDocument();
  });
});
