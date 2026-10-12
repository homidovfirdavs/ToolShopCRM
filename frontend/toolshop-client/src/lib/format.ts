const CURRENCY = "TJS";

/** 12350.5 -> "12 350.5 TJS" ; null/undefined -> "N/A" (never 0). */
export function formatMoney(value: number | null | undefined, currency: string = CURRENCY): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "N/A";
  const rounded = Math.round(value * 100) / 100;
  const hasFraction = Math.abs(rounded % 1) > 0;
  const text = new Intl.NumberFormat("en-US", {
    minimumFractionDigits: hasFraction ? 2 : 0,
    maximumFractionDigits: 2,
  })
    .format(rounded)
    .replace(/,/g, " ");
  return `${text} ${currency}`;
}

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "N/A";
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 3 })
    .format(value)
    .replace(/,/g, " ");
}

function pad(n: number): string {
  return n < 10 ? `0${n}` : String(n);
}

/** ISO string/Date -> "07.10.2026" */
export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return "—";
  const d = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return "—";
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`;
}

/** ISO string/Date -> "07.10.2026 14:35" */
export function formatDateTime(value: string | Date | null | undefined): string {
  if (!value) return "—";
  const d = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return "—";
  return `${formatDate(d)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
