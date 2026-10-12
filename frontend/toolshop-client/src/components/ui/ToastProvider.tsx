import { useCallback, useMemo, useRef, useState, type ReactNode } from "react";
import { ToastContext, type ToastKind } from "./toastContext";

interface ToastItem {
  id: number;
  message: string;
  kind: ToastKind;
}

const styles: Record<ToastKind, string> = {
  success: "bg-green-600",
  error: "bg-red-600",
  info: "bg-slate-800",
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const nextId = useRef(1);

  const show = useCallback((message: string, kind: ToastKind = "info") => {
    const id = nextId.current++;
    setItems((prev) => [...prev, { id, message, kind }]);
    window.setTimeout(() => setItems((prev) => prev.filter((i) => i.id !== id)), 4000);
  }, []);

  const value = useMemo(
    () => ({
      show,
      success: (m: string) => show(m, "success"),
      error: (m: string) => show(m, "error"),
    }),
    [show],
  );

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="fixed bottom-4 right-4 z-[60] flex flex-col gap-2" role="status" aria-live="polite">
        {items.map((i) => (
          <div key={i.id} className={`max-w-sm rounded-lg px-4 py-2 text-sm text-white shadow-lg ${styles[i.kind]}`}>
            {i.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
