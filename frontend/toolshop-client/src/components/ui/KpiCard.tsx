import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { ArrowDown, ArrowUp } from "lucide-react";

type Tone = "default" | "positive" | "negative" | "warning";
export type KpiAccent = "blue" | "green" | "amber" | "violet" | "red" | "teal";

const tones: Record<Tone, string> = {
  default: "text-slate-900",
  positive: "text-green-600",
  negative: "text-red-600",
  warning: "text-amber-600",
};

const accents: Record<KpiAccent, string> = {
  blue: "bg-blue-50 text-blue-600",
  green: "bg-emerald-50 text-emerald-600",
  amber: "bg-amber-50 text-amber-600",
  violet: "bg-violet-50 text-violet-600",
  red: "bg-red-50 text-red-600",
  teal: "bg-teal-50 text-teal-600",
};

/** Change vs the previous period. pct === null means "not comparable": nothing is shown. */
export interface KpiDelta {
  pct: number | null;
  /** true when an increase is good (revenue); false when an increase is bad (expenses). */
  upIsGood: boolean;
  label: string;
}

function DeltaLine({ delta }: { delta: KpiDelta }) {
  if (delta.pct === null) return null;
  const up = delta.pct >= 0;
  const good = up === delta.upIsGood;
  const Arrow = up ? ArrowUp : ArrowDown;
  return (
    <div className="mt-1.5 flex flex-wrap items-center gap-x-1.5 text-[13px] text-slate-600">
      <span className={`inline-flex items-center gap-0.5 font-semibold ${good ? "text-emerald-600" : "text-red-600"}`}>
        <Arrow size={12} strokeWidth={3} />
        {Math.abs(delta.pct).toFixed(1)}%
      </span>
      <span>{delta.label}</span>
    </div>
  );
}

export default function KpiCard({
  label,
  value,
  hint,
  tone = "default",
  icon,
  accent,
  delta,
  to,
}: {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: Tone;
  icon?: ReactNode;
  /** When set, the icon is drawn as a coloured tile on the left (dashboard style). */
  accent?: KpiAccent;
  delta?: KpiDelta;
  to?: string;
}) {
  const body = accent ? (
    <div className="@container flex h-full min-w-0 items-start gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm transition hover:shadow-md">
      <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${accents[accent]}`}>{icon}</div>
      <div className="min-w-0 flex-1">
        <p className="break-words text-xs font-semibold uppercase leading-tight tracking-wide text-slate-600">{label}</p>
        <p className={`mt-1 break-words text-2xl font-bold leading-tight @[17rem]:text-[1.75rem] ${tones[tone]}`}>{value}</p>
        {delta && <DeltaLine delta={delta} />}
        {hint && <div className="mt-1 text-[13px] text-slate-600">{hint}</div>}
      </div>
    </div>
  ) : (
    <div className="@container h-full min-w-0 rounded-xl border border-slate-200 bg-white p-4 shadow-sm transition hover:shadow-md sm:p-5">
      <div className="flex items-center justify-between gap-2 text-slate-500">
        <span className="min-w-0 break-words text-[13px] font-medium uppercase tracking-wide text-slate-600">{label}</span>
        {icon}
      </div>
      <div className={`mt-2 break-words text-2xl font-bold @[15rem]:text-3xl ${tones[tone]}`}>{value}</div>
      {hint && <div className="mt-1 text-[13px] text-slate-600">{hint}</div>}
    </div>
  );
  return to ? (
    <Link to={to} className="block min-w-0">
      {body}
    </Link>
  ) : (
    body
  );
}
