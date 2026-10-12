export type PresetKey = "today" | "yesterday" | "last7" | "last30" | "thisMonth" | "lastMonth" | "custom";

export interface DateRange {
  from: string;
  to: string;
}

export const PRESET_KEYS: PresetKey[] = ["today", "yesterday", "last7", "last30", "thisMonth", "lastMonth", "custom"];

export function toDateStr(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function getPresetRange(preset: PresetKey): DateRange {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  switch (preset) {
    case "yesterday": {
      const y = new Date(today);
      y.setDate(y.getDate() - 1);
      return { from: toDateStr(y), to: toDateStr(y) };
    }
    case "last7": {
      const f = new Date(today);
      f.setDate(f.getDate() - 6);
      return { from: toDateStr(f), to: toDateStr(today) };
    }
    case "last30": {
      const f = new Date(today);
      f.setDate(f.getDate() - 29);
      return { from: toDateStr(f), to: toDateStr(today) };
    }
    case "thisMonth":
      return { from: toDateStr(new Date(today.getFullYear(), today.getMonth(), 1)), to: toDateStr(today) };
    case "lastMonth":
      return {
        from: toDateStr(new Date(today.getFullYear(), today.getMonth() - 1, 1)),
        to: toDateStr(new Date(today.getFullYear(), today.getMonth(), 0)),
      };
    default:
      return { from: toDateStr(today), to: toDateStr(today) };
  }
}
