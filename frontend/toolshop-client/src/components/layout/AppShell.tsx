import { useEffect, useState } from "react";
import { Outlet } from "react-router-dom";
import Sidebar from "./Sidebar";
import TopHeader from "./TopHeader";
import OfflineBanner from "../pwa/OfflineBanner";
import UpdatePrompt from "../pwa/UpdatePrompt";
import { useStackedTables } from "../../lib/useStackedTables";

export default function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  useStackedTables(); // phone: table rows become cards (see lib/useStackedTables.ts)

  // Close the drawer with Escape.
  useEffect(() => {
    if (!mobileOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setMobileOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [mobileOpen]);

  // Stop the page behind the open drawer from scrolling (touch devices).
  useEffect(() => {
    if (!mobileOpen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prev;
    };
  }, [mobileOpen]);

  return (
    <div className="min-h-dvh bg-slate-50">
      {/* Desktop sidebar: fixed 240px column */}
      <div className="fixed inset-y-0 left-0 z-40 hidden w-60 lg:block">
        <Sidebar />
      </div>

      {/* Mobile / tablet drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true">
          <div className="absolute inset-0 bg-slate-900/50" onClick={() => setMobileOpen(false)} aria-hidden="true" />
          <div className="relative h-full w-72 max-w-[85vw] overscroll-contain">
            <Sidebar onNavigate={() => setMobileOpen(false)} />
          </div>
        </div>
      )}

      {/* Content column: takes only the space left of the sidebar */}
      <div className="flex min-h-dvh min-w-0 flex-col lg:pl-60">
        <TopHeader onMenu={() => setMobileOpen(true)} />
        <OfflineBanner />
        <main className="w-full min-w-0 flex-1 p-4 sm:p-5 lg:px-8 lg:py-6">
          <div className="mx-auto w-full min-w-0 max-w-[1760px]">
            <Outlet />
          </div>
        </main>
      </div>
      <UpdatePrompt />
    </div>
  );
}
