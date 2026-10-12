import type { ReactNode } from "react";

export default function Card({
  children,
  className = "",
  title,
  actions,
  fill = false,
}: {
  children: ReactNode;
  className?: string;
  title?: ReactNode;
  actions?: ReactNode;
  /** Stretch to the row height and centre the content vertically (dashboard widgets next to a taller card). */
  fill?: boolean;
}) {
  return (
    <section className={`min-w-0 rounded-xl border border-slate-200 bg-white shadow-sm ${fill ? "flex flex-col" : ""} ${className}`}>
      {(title || actions) && (
        <header className="flex items-center justify-between gap-2 border-b border-slate-100 px-4 py-3">
          <h2 className="text-base font-semibold text-slate-800">{title}</h2>
          {actions}
        </header>
      )}
      <div className={fill ? "flex flex-1 flex-col justify-center p-4" : "p-4"}>{children}</div>
    </section>
  );
}
