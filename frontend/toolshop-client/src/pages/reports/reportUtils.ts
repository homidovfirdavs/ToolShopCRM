import type { CSSProperties } from "react";

// Centralized formatters (money = "12 350 TJS", null -> "N/A", date = dd.MM.yyyy)
export { formatMoney, formatDate, formatDateTime } from "../../lib/format";

export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined) return "N/A";
  return `${value.toFixed(2)}%`;
}

export function toIsoDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function defaultRange(): { from: string; to: string } {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 29);
  return { from: toIsoDate(from), to: toIsoDate(to) };
}

export const styles: Record<string, CSSProperties> = {
  card: { background: "#fff", borderRadius: 12, padding: 16, border: "1px solid #e2e8f0", boxShadow: "0 1px 2px rgba(15,23,42,0.05)", marginBottom: 16 },
  filterRow: { display: "flex", flexWrap: "wrap", gap: 12, alignItems: "flex-end" },
  field: { display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "#64748b" },
  input: { border: "1px solid #cbd5e1", borderRadius: 8, padding: "7px 10px", fontSize: 13, minWidth: 130, background: "#fff" },
  button: { border: "1px solid #cbd5e1", background: "#fff", color: "#334155", borderRadius: 8, padding: "7px 14px", fontSize: 13, cursor: "pointer" },
  primaryButton: { border: "1px solid #2563eb", background: "#2563eb", color: "#fff", borderRadius: 8, padding: "7px 14px", fontSize: 13, cursor: "pointer" },
  table: { width: "100%", borderCollapse: "collapse", fontSize: 13 },
  th: { textAlign: "left", padding: "10px 12px", borderBottom: "1px solid #e2e8f0", background: "#f8fafc", color: "#475569", fontWeight: 600, whiteSpace: "nowrap" },
  thRight: { textAlign: "right", padding: "10px 12px", borderBottom: "1px solid #e2e8f0", background: "#f8fafc", color: "#475569", fontWeight: 600, whiteSpace: "nowrap" },
  td: { padding: "10px 12px", borderBottom: "1px solid #f1f5f9" },
  tdRight: { padding: "10px 12px", borderBottom: "1px solid #f1f5f9", textAlign: "right" },
  empty: { padding: 24, textAlign: "center", color: "#94a3b8" },
  error: { color: "#b91c1c", fontSize: 13, margin: "8px 0" },
  muted: { color: "#94a3b8" },
  warning: { background: "#fffbeb", border: "1px solid #fde68a", color: "#92400e", borderRadius: 8, padding: "8px 12px", fontSize: 12, marginBottom: 12 },
};

/** Localized label for a status/method enum value (falls back to the raw value). */
export function statusLabel(t: (key: string) => string, value: string | null | undefined): string {
  if (!value) return "—";
  const key = `rep.st.${value}`;
  const label = t(key);
  return label === key ? value : label;
}
