import { useEffect, useRef, useState, type FormEvent } from "react";
import { AlertTriangle, CameraOff, CheckCircle2, Loader2, Minus, Plus, X, XCircle } from "lucide-react";
import { useI18n } from "../../i18n/useI18n";
import Button from "../ui/Button";
import { inputClass } from "../ui/formStyles";
import { normalizeBarcode, ScanDeduper } from "../../lib/barcode";

/** What the POS tells the scanner after a code was handled. */
export interface ScanFeedback {
  kind: "ok" | "warn" | "error";
  title: string;
  lines: string[];
  /** Set when a product was put in the cart: enables the −1 / +1 buttons for it. */
  productId?: number;
}

type CameraState = "starting" | "ready" | "denied" | "none" | "insecure" | "error";

interface Props {
  open: boolean;
  /** Close without going to the cart. */
  onClose: () => void;
  /** "Done": close and show the cart. */
  onDone: () => void;
  /** Look the code up and add the product to the cart (the POS owns the cart and all stock checks). */
  onCode: (code: string) => Promise<ScanFeedback>;
  /** Take one unit back out of the cart. */
  onRemoveOne: (productId: number) => void;
  cartCount: number;
  cartTotal: string;
}

function errorKind(e: unknown): CameraState {
  const name = (e as { name?: string } | null)?.name ?? "";
  if (name === "NotAllowedError" || name === "SecurityError" || name === "PermissionDeniedError") return "denied";
  if (name === "NotFoundError" || name === "DevicesNotFoundError" || name === "OverconstrainedError") return "none";
  return "error";
}

const FEEDBACK_STYLE = {
  ok: { box: "border-emerald-300 bg-emerald-50 text-emerald-900", Icon: CheckCircle2, icon: "text-emerald-600" },
  warn: { box: "border-amber-300 bg-amber-50 text-amber-900", Icon: AlertTriangle, icon: "text-amber-600" },
  error: { box: "border-red-300 bg-red-50 text-red-900", Icon: XCircle, icon: "text-red-600" },
} as const;

function cameraSupported(): boolean {
  return typeof window !== "undefined" && window.isSecureContext && !!navigator.mediaDevices?.getUserMedia;
}

/** Mounted only while open, so every opening starts from a clean state (and the camera is released on close). */
export default function BarcodeScanner(props: Props) {
  return props.open ? <ScannerDialog {...props} /> : null;
}

function ScannerDialog({ onClose, onDone, onCode, onRemoveOne, cartCount, cartTotal }: Props) {
  const { t } = useI18n();
  const videoRef = useRef<HTMLVideoElement>(null);
  const deduper = useRef(new ScanDeduper());
  const busy = useRef(false);
  const onCodeRef = useRef(onCode);
  const mounted = useRef(true);
  const [camera, setCamera] = useState<CameraState>(() => (cameraSupported() ? "starting" : "insecure"));
  const [feedback, setFeedback] = useState<ScanFeedback | null>(null);
  const [lastCode, setLastCode] = useState("");
  const [working, setWorking] = useState(false);
  const [manual, setManual] = useState("");

  useEffect(() => {
    onCodeRef.current = onCode;
  }, [onCode]);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  async function handle(code: string) {
    if (busy.current) return;
    busy.current = true;
    setWorking(true);
    try {
      const fb = await onCodeRef.current(code);
      if (!mounted.current) return;
      setFeedback(fb);
      setLastCode(code);
      try {
        navigator.vibrate?.(fb.kind === "ok" ? 40 : [60, 40, 60]);
      } catch {
        /* vibration is optional */
      }
    } catch {
      if (mounted.current) setFeedback({ kind: "error", title: t("common.loadFailed"), lines: [code] });
    } finally {
      busy.current = false;
      if (mounted.current) setWorking(false);
    }
  }
  const handleRef = useRef(handle);
  useEffect(() => {
    handleRef.current = handle;
  });

  // Camera lifecycle: opened with the dialog, ALWAYS stopped when it closes.
  useEffect(() => {
    if (!cameraSupported()) return; // state is already "insecure"
    let cancelled = false;
    let stop: (() => void) | undefined;

    (async () => {
      try {
        // Loaded only when the scanner is opened, so the normal POS stays small and fast.
        const [{ BrowserMultiFormatOneDReader }, { BarcodeFormat, ChecksumException, DecodeHintType, FormatException, NotFoundException }] = await Promise.all([
          import("@zxing/browser"),
          import("@zxing/library"),
        ]);
        if (cancelled) return;
        const hints = new Map();
        hints.set(DecodeHintType.POSSIBLE_FORMATS, [
          BarcodeFormat.EAN_13,
          BarcodeFormat.EAN_8,
          BarcodeFormat.UPC_A,
          BarcodeFormat.UPC_E,
          BarcodeFormat.CODE_128,
        ]);
        // No TRY_HARDER on purpose: that makes the library rotate each frame, and the rotation code in @zxing/browser
        // throws on a normal frame, which stops the whole camera loop (found with the fake-camera test).
        const reader = new BrowserMultiFormatOneDReader(hints, { delayBetweenScanAttempts: 120, delayBetweenScanSuccess: 120 });
        const controls = await reader.decodeFromConstraints(
          // The back camera ("environment"); the browser asks the user for permission here.
          { audio: false, video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } } },
          videoRef.current ?? undefined,
          (result, err) => {
            // "nothing / unreadable in this frame" is normal. Any OTHER error makes the library stop the camera for good,
            // so tell the cashier instead of leaving a frozen preview.
            const normal = err instanceof NotFoundException || err instanceof ChecksumException || err instanceof FormatException;
            if (err && !normal && !cancelled) {
              console.warn("Barcode scanner stopped:", err);
              setCamera("error");
            }
            if (!result) return;
            const code = normalizeBarcode(result.getText());
            if (!code || busy.current) return;
            if (!deduper.current.accept(code, Date.now())) return;
            void handleRef.current(code);
          },
        );
        if (cancelled) {
          controls.stop();
          return;
        }
        stop = () => controls.stop();
        setCamera("ready");
      } catch (e) {
        if (!cancelled) setCamera(errorKind(e));
      }
    })();

    return () => {
      cancelled = true;
      stop?.();
    };
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  function submitManual(e: FormEvent) {
    e.preventDefault();
    const code = normalizeBarcode(manual);
    if (!code || busy.current) return;
    setManual("");
    void handle(code);
  }

  const cameraMessage: Record<Exclude<CameraState, "ready" | "starting">, string> = {
    denied: t("pos.scanDenied"),
    none: t("pos.scanNoCamera"),
    insecure: t("pos.scanInsecure"),
    error: t("pos.scanError"),
  };
  const fb = feedback;
  const style = fb ? FEEDBACK_STYLE[fb.kind] : null;

  return (
    <div className="fixed inset-0 z-[60] flex items-stretch justify-center bg-slate-900/60 sm:items-center sm:p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t("pos.scanTitle")}
        data-testid="scan-dialog"
        className="relative flex h-[100dvh] w-full flex-col overflow-hidden bg-white shadow-xl sm:h-auto sm:max-h-[94dvh] sm:max-w-lg sm:rounded-xl"
      >
        <header className="flex items-center justify-between gap-2 border-b border-slate-100 px-4 py-2">
          <h2 className="text-base font-semibold text-slate-900">{t("pos.scanTitle")}</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label={t("pos.scanClose")}
            data-testid="scan-close"
            className="flex h-11 w-11 items-center justify-center rounded text-slate-500 hover:bg-slate-100"
          >
            <X size={20} />
          </button>
        </header>

        <div className="relative aspect-[4/3] max-h-[40dvh] w-full flex-none overflow-hidden bg-black sm:max-h-[44dvh]">
          <video
            ref={videoRef}
            data-testid="scan-video"
            aria-label={t("pos.scanTitle")}
            className="h-full w-full object-cover"
            playsInline
            muted
            autoPlay
          />
          {camera === "ready" && (
            <div className="pointer-events-none absolute inset-0 flex items-center justify-center" aria-hidden="true">
              <div className="relative h-[46%] w-[82%] rounded-xl border-2 border-white/80 shadow-[0_0_0_9999px_rgba(0,0,0,0.35)]">
                <div className="absolute inset-x-3 top-1/2 h-0.5 -translate-y-1/2 bg-red-500/80" />
              </div>
            </div>
          )}
          {camera === "starting" && (
            <div className="absolute inset-0 flex items-center justify-center gap-2 text-sm text-white">
              <Loader2 size={18} className="animate-spin" /> {t("pos.scanStarting")}
            </div>
          )}
          {camera !== "ready" && camera !== "starting" && (
            <div data-testid="scan-camera-problem" className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-slate-900 px-5 text-center text-sm text-slate-100">
              <CameraOff size={28} />
              <p>{cameraMessage[camera]}</p>
            </div>
          )}
          {working && camera === "ready" && (
            <div className="absolute right-2 top-2 rounded-full bg-black/60 p-1.5 text-white">
              <Loader2 size={16} className="animate-spin" />
            </div>
          )}
        </div>

        <div className="flex-1 space-y-3 overflow-y-auto p-4">
          <p className="text-sm text-slate-600">{t("pos.scanHint")}</p>

          <div role="status" aria-live="polite" data-testid="scan-feedback">
            {fb && style && (
              <div className={`rounded-lg border p-3 ${style.box}`}>
                <div className="flex items-start gap-2">
                  <style.Icon size={20} className={`mt-0.5 shrink-0 ${style.icon}`} />
                  <div className="min-w-0 flex-1">
                    <p className="font-semibold">{fb.title}</p>
                    {fb.lines.map((line, i) => (
                      <p key={i} className={i === 0 ? "break-words text-sm font-medium" : "break-words text-sm"}>
                        {line}
                      </p>
                    ))}
                  </div>
                </div>
                {fb.productId !== undefined && (
                  <div className="mt-2 flex gap-2">
                    <button
                      type="button"
                      data-testid="scan-minus"
                      disabled={working}
                      onClick={() => {
                        onRemoveOne(fb.productId!);
                        setFeedback({ kind: "warn", title: t("pos.scanRemoved"), lines: fb.lines.slice(0, 1) });
                      }}
                      className="inline-flex min-h-11 flex-1 items-center justify-center gap-1 rounded-lg border border-slate-300 bg-white text-sm font-medium text-slate-700 disabled:opacity-50"
                    >
                      <Minus size={16} /> {t("pos.scanUndo")}
                    </button>
                    <button
                      type="button"
                      data-testid="scan-plus"
                      disabled={working || !lastCode}
                      onClick={() => void handle(lastCode)}
                      className="inline-flex min-h-11 flex-1 items-center justify-center gap-1 rounded-lg border border-slate-300 bg-white text-sm font-medium text-slate-700 disabled:opacity-50"
                    >
                      <Plus size={16} /> {t("pos.scanOneMore")}
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>

          <form onSubmit={submitManual} className="flex gap-2">
            <input
              data-testid="scan-manual-input"
              className={`${inputClass} min-w-0 flex-1`}
              value={manual}
              onChange={(e) => setManual(e.target.value)}
              placeholder={t("pos.scanManual")}
              aria-label={t("pos.scanManual")}
              autoComplete="off"
              autoCapitalize="off"
              inputMode="text"
            />
            <Button type="submit" variant="secondary" data-testid="scan-manual-submit" disabled={working || !manual.trim()}>
              {t("pos.scanManualBtn")}
            </Button>
          </form>
        </div>

        <footer className="flex items-center justify-between gap-3 border-t border-slate-100 px-4 py-3">
          <p className="min-w-0 text-sm text-slate-600" data-testid="scan-cart-summary">
            {t("pos.scanCart")}: <b className="text-slate-900">{cartCount}</b> · <b className="text-slate-900">{cartTotal}</b>
          </p>
          <Button data-testid="scan-done" onClick={onDone} className="!px-5 !py-3 text-base">
            {t("pos.scanDone")}
          </Button>
        </footer>
      </div>
    </div>
  );
}
