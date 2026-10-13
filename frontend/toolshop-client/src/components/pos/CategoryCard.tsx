import { ChevronRight, type LucideIcon } from "lucide-react";
import type { CategoryTone } from "./categoryVisuals";

export interface CategoryCardProps {
  name: string;
  icon: LucideIcon;
  tone: CategoryTone;
  /** Real count from the API. When unknown, no number is shown (never estimated). */
  countLabel?: string;
  selected: boolean;
  onSelect: () => void;
}

export default function CategoryCard({ name, icon: Icon, tone, countLabel, selected, onSelect }: CategoryCardProps) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={selected}
      data-testid="pos-category-card"
      className={`group flex min-h-[84px] w-full min-w-0 items-center gap-3 rounded-2xl border bg-gradient-to-br p-3 text-left shadow-sm transition duration-150 hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 active:scale-[0.99] motion-reduce:transition-none motion-reduce:hover:translate-y-0 @[66rem]:p-4 ${tone.card} ${
        selected ? "shadow-md ring-2 ring-blue-500" : ""
      }`}
    >
      <span className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl @[66rem]:h-16 @[66rem]:w-16 ${tone.tile}`} aria-hidden="true">
        <Icon className="h-6 w-6 @[66rem]:h-8 @[66rem]:w-8" strokeWidth={1.75} />
      </span>
      <span className="min-w-0 flex-1">
        <span title={name} className="line-clamp-3 break-words text-[14px] font-bold @[66rem]:text-[15px] leading-snug text-slate-900">{name}</span>
        {countLabel && <span className="mt-0.5 block truncate text-sm text-slate-500">{countLabel}</span>}
      </span>
      <span
        aria-hidden="true"
        className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full border transition group-hover:translate-x-0.5 motion-reduce:transition-none ${
          selected ? "border-blue-600 bg-blue-600 text-white" : `border-slate-200 bg-white/80 ${tone.arrow}`
        }`}
      >
        <ChevronRight size={16} />
      </span>
    </button>
  );
}
