import { PRESET_KEYS, getPresetRange, type DateRange, type PresetKey } from "../../lib/dateRange";
import { useI18n } from "../../i18n/useI18n";

export default function DateRangeFilter({
  preset,
  range,
  onChange,
}: {
  preset: PresetKey;
  range: DateRange;
  onChange: (preset: PresetKey, range: DateRange) => void;
}) {
  const { t } = useI18n();
  return (
    <div className="mb-4 min-w-0 rounded-xl border border-slate-200 bg-white p-3 shadow-sm">
      <div className="flex flex-wrap gap-2">
        {PRESET_KEYS.map((k) => (
          <button
            key={k}
            onClick={() => onChange(k, k === "custom" ? range : getPresetRange(k))}
            aria-pressed={preset === k}
            className={`min-h-11 rounded-lg px-3.5 py-1.5 text-sm font-medium transition sm:min-h-0 ${
              preset === k ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-700 hover:bg-slate-200"
            }`}
          >
            {t(`preset.${k}`)}
          </button>
        ))}
      </div>
      {preset === "custom" && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <input
            type="date"
            aria-label="From"
            className="min-w-0 flex-1 rounded-lg border border-slate-300 px-2 py-1.5 text-sm sm:flex-none"
            value={range.from}
            onChange={(e) => onChange("custom", { ...range, from: e.target.value })}
          />
          <span className="text-slate-400">—</span>
          <input
            type="date"
            aria-label="To"
            className="min-w-0 flex-1 rounded-lg border border-slate-300 px-2 py-1.5 text-sm sm:flex-none"
            value={range.to}
            onChange={(e) => onChange("custom", { ...range, to: e.target.value })}
          />
        </div>
      )}
    </div>
  );
}
