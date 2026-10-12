import { NavLink } from "react-router-dom";
import { Wrench } from "lucide-react";
import { usePermissions } from "../../auth/usePermissions";
import { useI18n } from "../../i18n/useI18n";
import { navGroups } from "./navConfig";
import { APP_BUILD } from "../../lib/appInfo";

export default function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const { has } = usePermissions();
  const { t } = useI18n();

  return (
    <aside className="flex h-full w-full flex-col pb-[env(safe-area-inset-bottom)] border-r border-slate-800 bg-slate-900 text-slate-300 shadow-[2px_0_8px_rgba(15,23,42,0.18)]">
      <div className="flex h-14 shrink-0 items-center gap-2 px-5 text-white">
        <Wrench size={22} className="text-blue-400" />
        <span className="text-lg font-bold">{t("app.name")}</span>
      </div>
      <nav className="flex-1 overflow-y-auto px-3 pb-4 [scrollbar-color:#334155_transparent] [scrollbar-width:thin]" aria-label="Main">
        {navGroups.map((group) => {
          const visible = group.items.filter((i) => has(i.permission));
          if (visible.length === 0) return null;
          return (
            <div key={group.labelKey} className="mt-3">
              <p className="px-3 pb-0.5 text-xs font-semibold uppercase tracking-wider text-slate-400">
                {t(group.labelKey)}
              </p>
              {visible.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === "/inventory"}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    `flex min-h-11 items-center gap-3 rounded-lg px-3 py-1.5 text-[15px] transition lg:min-h-0 ${
                      isActive ? "bg-blue-600 text-white" : "hover:bg-slate-800 hover:text-white"
                    }`
                  }
                >
                  <item.icon size={18} />
                  {t(item.labelKey)}
                </NavLink>
              ))}
            </div>
          );
        })}
      </nav>
      <p data-testid="app-version" className="shrink-0 px-5 py-2 text-xs text-slate-400">
        build {APP_BUILD}
      </p>
    </aside>
  );
}
