import { LogOut, Menu } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { useI18n } from "../../i18n/useI18n";
import { LANGS } from "../../i18n/translations";
import InstallButton from "../pwa/InstallButton";

export default function TopHeader({ onMenu }: { onMenu: () => void }) {
  const { user, logout } = useAuth();
  const { lang, setLang, t } = useI18n();
  const navigate = useNavigate();

  async function handleLogout() {
    await logout();
    navigate("/login", { replace: true });
  }

  return (
    <header className="sticky top-0 z-30 flex min-h-16 items-center justify-between gap-2 border-b border-slate-200 bg-white px-4 pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))]">
      <button
        onClick={onMenu}
        aria-label={t("common.menu")}
        className="flex h-11 w-11 items-center justify-center rounded text-slate-600 hover:bg-slate-100 lg:hidden"
      >
        <Menu size={20} />
      </button>
      <div className="hidden lg:block" />

      <div className="flex items-center gap-1 sm:gap-3">
        <InstallButton />
        <div className="flex rounded-lg border border-slate-200 p-0.5" role="group" aria-label="Language">
          {LANGS.map((l) => (
            <button
              key={l.code}
              onClick={() => setLang(l.code)}
              aria-pressed={lang === l.code}
              className={`min-h-9 rounded-md px-2.5 text-xs font-semibold lg:min-h-0 lg:px-2 lg:py-1 ${
                lang === l.code ? "bg-blue-600 text-white" : "text-slate-600 hover:bg-slate-100"
              }`}
            >
              {l.label}
            </button>
          ))}
        </div>

        <div className="hidden text-right sm:block">
          <p className="text-sm font-medium text-slate-900">{user?.fullName || user?.username}</p>
          <p className="text-xs text-slate-500">{user?.roles.map((r) => t(`role.${r}`)).join(", ")}</p>
        </div>

        <button
          onClick={handleLogout}
          aria-label={t("common.logout")}
          className="flex min-h-11 items-center gap-1 rounded-lg px-2 py-1.5 text-sm text-slate-600 hover:bg-slate-100"
        >
          <LogOut size={16} />
          <span className="hidden sm:inline">{t("common.logout")}</span>
        </button>
      </div>
    </header>
  );
}
