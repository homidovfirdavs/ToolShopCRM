import { useRef, useState } from "react";
import {
  addCashMovement,
  closeCashShift,
  getCashShifts,
  getCurrentCashShift,
  openCashShift,
  reviewCashShift,
  voidCashMovement,
  type CashMovement,
  type CashMovementType,
  type CashShift,
  type CashShiftListItem,
  type CurrentCashShift,
} from "../api/cashShifts";
import { usePermissions } from "../auth/usePermissions";
import Button from "../components/ui/Button";
import Card from "../components/ui/Card";
import FormField from "../components/ui/FormField";
import Modal from "../components/ui/Modal";
import PageHeader from "../components/ui/PageHeader";
import StatusBadge from "../components/ui/StatusBadge";
import { EmptyState, ErrorState, LoadingSkeleton } from "../components/ui/States";
import { inputClass, tdClass, thClass } from "../components/ui/formStyles";
import { useToast } from "../components/ui/useToast";
import { useI18n } from "../i18n/useI18n";
import { errorCode, errorMessage } from "../lib/apiError";
import { formatDateTime, formatMoney } from "../lib/format";
import { newIdempotencyKey } from "../lib/idempotency";
import { useAsyncData } from "../lib/useAsyncData";

type Loaded = { disabled: true } | { disabled: false; current: CurrentCashShift; history: CashShiftListItem[] };

async function loadAll(): Promise<Loaded> {
  try {
    const [current, history] = await Promise.all([getCurrentCashShift(), getCashShifts(1)]);
    return { disabled: false, current, history: history.items };
  } catch (err) {
    if (errorCode(err) === "CASH_SHIFTS_DISABLED") return { disabled: true };
    throw err;
  }
}

/** Parses a money input ("12,5" or "12.5"); returns null for empty / invalid / negative. */
function parseAmount(text: string): number | null {
  const v = text.trim().replace(",", ".");
  if (v === "") return null;
  const n = Number(v);
  return Number.isFinite(n) && n >= 0 ? n : null;
}

export default function CashShiftPage() {
  const { t } = useI18n();
  const toast = useToast();
  const { has } = usePermissions();
  const canOperate = has("CashShifts.Operate");
  const canReview = has("CashShifts.Review");

  const { data, error, loading, reload } = useAsyncData(loadAll, "cash-shifts");

  const [openingText, setOpeningText] = useState("");
  const [busy, setBusy] = useState(false);

  const [movementType, setMovementType] = useState<CashMovementType | null>(null);
  const [movAmount, setMovAmount] = useState("");
  const [movReason, setMovReason] = useState("");
  const movementKey = useRef(newIdempotencyKey());

  const [closing, setClosing] = useState(false);
  const [actualText, setActualText] = useState("");
  const [closeNote, setCloseNote] = useState("");
  const [noteRequired, setNoteRequired] = useState(false);
  const [closedResult, setClosedResult] = useState<CashShift | null>(null);

  const [voidTarget, setVoidTarget] = useState<CashMovement | null>(null);
  const [voidReason, setVoidReason] = useState("");

  const [reviewTarget, setReviewTarget] = useState<CashShiftListItem | null>(null);
  const [reviewNote, setReviewNote] = useState("");

  const fail = (err: unknown) => {
    const code = errorCode(err);
    const known: Record<string, string> = {
      CASH_SHIFT_ALREADY_OPEN: "cash.err.alreadyOpen",
      CASH_SHIFT_NOT_OPEN: "cash.err.notOpen",
      CASH_SHIFT_NOT_YOURS: "cash.err.notYours",
      CASH_MOVEMENT_LIMIT: "cash.err.limit",
      CASH_OUT_EXCEEDS_DRAWER: "cash.err.exceeds",
      CASH_MOVEMENT_ALREADY_VOIDED: "cash.err.alreadyVoided",
      CASH_SHIFT_NOT_REVIEWABLE: "cash.err.notReviewable",
    };
    toast.error(code && known[code] ? t(known[code]) : errorMessage(err) ?? t("common.error"));
  };

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    try {
      await action();
    } catch (err) {
      fail(err);
      if (["CASH_SHIFT_ALREADY_OPEN", "CASH_SHIFT_NOT_OPEN"].includes(errorCode(err) ?? "")) reload();
    } finally {
      setBusy(false);
    }
  };

  const doOpen = () => {
    const amount = parseAmount(openingText);
    if (amount === null) return toast.error(t("cash.err.amount"));
    void run(async () => {
      await openCashShift(amount);
      setOpeningText("");
      setClosedResult(null);
      toast.success(t("cash.opened"));
      reload();
    });
  };

  const shiftId = data && !data.disabled && data.current.isOpen ? data.current.shift.id : null;

  const doMovement = () => {
    const amount = parseAmount(movAmount);
    if (shiftId === null || movementType === null) return;
    if (amount === null || amount <= 0) return toast.error(t("cash.err.amount"));
    if (!movReason.trim()) return toast.error(t("cash.err.reason"));
    void run(async () => {
      await addCashMovement(shiftId, movementType, amount, movReason, movementKey.current);
      movementKey.current = newIdempotencyKey(); // done: the next movement is a new intention
      setMovementType(null);
      setMovAmount("");
      setMovReason("");
      toast.success(t("cash.movementSaved"));
      reload();
    });
  };

  const doClose = () => {
    const amount = parseAmount(actualText);
    if (shiftId === null) return;
    if (amount === null) return toast.error(t("cash.err.amount"));
    void run(async () => {
      try {
        const result = await closeCashShift(shiftId, amount, closeNote);
        setClosedResult(result);
        setClosing(false);
        setActualText("");
        setCloseNote("");
        setNoteRequired(false);
        toast.success(t("cash.closed"));
        reload();
      } catch (err) {
        if (errorCode(err) === "CASH_DIFFERENCE_NOTE_REQUIRED") {
          setNoteRequired(true); // keep the dialog open: the cashier must explain the difference
          toast.error(t("cash.err.noteRequired"));
          return;
        }
        throw err;
      }
    });
  };

  const doVoid = () => {
    if (!voidTarget) return;
    if (!voidReason.trim()) return toast.error(t("cash.err.reason"));
    void run(async () => {
      await voidCashMovement(voidTarget.id, voidReason);
      setVoidTarget(null);
      setVoidReason("");
      toast.success(t("cash.voided"));
      reload();
    });
  };

  const doReview = () => {
    if (!reviewTarget) return;
    void run(async () => {
      await reviewCashShift(reviewTarget.id, reviewNote);
      setReviewTarget(null);
      setReviewNote("");
      toast.success(t("cash.reviewed"));
      reload();
    });
  };

  const header = <PageHeader title={t("cash.title")} subtitle={t("cash.subtitle")} />;

  if (loading) return <div>{header}<LoadingSkeleton /></div>;
  if (error || !data) return <div>{header}<ErrorState message={t("common.loadFailed")} onRetry={reload} /></div>;
  if (data.disabled) return <div>{header}<Card><EmptyState message={t("cash.disabled")} /></Card></div>;

  const { current, history } = data;
  const diffTone = (d: number | null) => (d === null ? "gray" : d === 0 ? "green" : "red");

  return (
    <div>
      {header}

      {closedResult && (
        <Card title={t("cash.closedSummary")} className="mb-4">
          <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
            <Stat label={t("cash.expected")} value={formatMoney(closedResult.expectedCash)} />
            <Stat label={t("cash.counted")} value={formatMoney(closedResult.closingCash)} />
            <Stat label={t("cash.difference")} value={formatMoney(closedResult.cashDifference)} />
            <Stat label={t("cash.shiftNo")} value={closedResult.shiftNumber} />
          </dl>
        </Card>
      )}

      {!current.isOpen ? (
        <Card title={t("cash.noOpen")} className="mb-4">
          {canOperate ? (
            <div className="max-w-sm space-y-3">
              {current.previousClosingCash !== null && (
                <p className="text-sm text-slate-500">
                  {t("cash.previousClosing")}: <b>{formatMoney(current.previousClosingCash)}</b>
                </p>
              )}
              <FormField label={t("cash.openingCash")} required>
                <input
                  className={inputClass}
                  inputMode="decimal"
                  value={openingText}
                  onChange={(e) => setOpeningText(e.target.value)}
                />
              </FormField>
              <Button onClick={doOpen} loading={busy}>{t("cash.openShift")}</Button>
            </div>
          ) : (
            <EmptyState message={t("cash.noOpen")} />
          )}
        </Card>
      ) : (
        <>
          <Card
            title={`${t("cash.currentShift")} ${current.shift.shiftNumber}`}
            className="mb-4"
            actions={
              canOperate ? (
                <div className="flex gap-2">
                  <Button size="sm" variant="secondary" onClick={() => setMovementType("CashIn")}>{t("cash.cashIn")}</Button>
                  <Button size="sm" variant="secondary" onClick={() => setMovementType("CashOut")}>{t("cash.cashOut")}</Button>
                  <Button size="sm" variant="danger" onClick={() => setClosing(true)}>{t("cash.closeShift")}</Button>
                </div>
              ) : undefined
            }
          >
            <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
              <Stat label={t("cash.cashier")} value={current.shift.cashierName ?? "N/A"} />
              <Stat label={t("cash.openedAt")} value={formatDateTime(current.shift.openedAt)} />
              <Stat label={t("cash.openingCash")} value={formatMoney(current.shift.openingCash)} />
              <Stat
                label={t("cash.expected")}
                value={current.shift.expectedCash === null ? t("cash.hiddenUntilClose") : formatMoney(current.shift.expectedCash)}
              />
              <Stat label={t("cash.sales.card")} value={formatMoney(current.shift.breakdown.cardSales)} />
              <Stat label={t("cash.sales.transfer")} value={formatMoney(current.shift.breakdown.bankTransferSales)} />
              <Stat label={t("cash.sales.qr")} value={formatMoney(current.shift.breakdown.qrSales)} />
              <Stat label={t("cash.sales.other")} value={formatMoney(current.shift.breakdown.otherSales)} />
            </dl>
            <p className="mt-3 text-xs text-slate-500">{t("cash.debtNote")}</p>
          </Card>

          <Card title={t("cash.movements")} className="mb-4">
            {current.movements.length === 0 ? (
              <EmptyState message={t("cash.noMovements")} />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr>
                      <th className={thClass}>{t("cash.time")}</th>
                      <th className={thClass}>{t("cash.type")}</th>
                      <th className={thClass}>{t("cash.amount")}</th>
                      <th className={thClass}>{t("cash.reason")}</th>
                      <th className={thClass} />
                    </tr>
                  </thead>
                  <tbody>
                    {current.movements.map((m) => (
                      <tr key={m.id} className="border-t border-slate-100">
                        <td className={tdClass}>{formatDateTime(m.createdAt)}</td>
                        <td className={tdClass}>{m.type === "CashIn" ? t("cash.cashIn") : t("cash.cashOut")}</td>
                        <td className={tdClass}>{formatMoney(m.amount)}</td>
                        <td className={tdClass}>
                          {m.reason} {m.isVoided && <StatusBadge label={t("cash.voidedBadge")} tone="gray" />}
                        </td>
                        <td className={tdClass}>
                          {canReview && !m.isVoided && (
                            <Button size="sm" variant="ghost" onClick={() => setVoidTarget(m)}>{t("cash.void")}</Button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      )}

      <Card title={t("cash.history")}>
        {history.length === 0 ? (
          <EmptyState message={t("cash.noHistory")} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr>
                  <th className={thClass}>{t("cash.shiftNo")}</th>
                  <th className={thClass}>{t("cash.cashier")}</th>
                  <th className={thClass}>{t("cash.openedAt")}</th>
                  <th className={thClass}>{t("cash.expected")}</th>
                  <th className={thClass}>{t("cash.counted")}</th>
                  <th className={thClass}>{t("cash.difference")}</th>
                  <th className={thClass} />
                </tr>
              </thead>
              <tbody>
                {history.map((s) => (
                  <tr key={s.id} className="border-t border-slate-100">
                    <td className={tdClass}>{s.shiftNumber}</td>
                    <td className={tdClass}>{s.cashierName ?? "N/A"}</td>
                    <td className={tdClass}>{formatDateTime(s.openedAt)}</td>
                    <td className={tdClass}>{formatMoney(s.expectedCash)}</td>
                    <td className={tdClass}>{formatMoney(s.closingCash)}</td>
                    <td className={tdClass}>
                      {s.status === "Open" ? (
                        <StatusBadge label={t("cash.statusOpen")} tone="blue" />
                      ) : (
                        <StatusBadge label={formatMoney(s.cashDifference)} tone={diffTone(s.cashDifference)} />
                      )}
                    </td>
                    <td className={tdClass}>
                      {canReview && s.requiresReview && !s.reviewed && (
                        <Button size="sm" variant="secondary" onClick={() => setReviewTarget(s)}>{t("cash.review")}</Button>
                      )}
                      {s.requiresReview && s.reviewed && <StatusBadge label={t("cash.reviewedBadge")} tone="green" />}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={movementType !== null}
        title={movementType === "CashIn" ? t("cash.cashIn") : t("cash.cashOut")}
        onClose={() => setMovementType(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setMovementType(null)}>{t("common.cancel")}</Button>
            <Button onClick={doMovement} loading={busy}>{t("common.save")}</Button>
          </>
        }
      >
        <div className="space-y-3">
          <FormField label={t("cash.amount")} required>
            <input className={inputClass} inputMode="decimal" value={movAmount} onChange={(e) => setMovAmount(e.target.value)} />
          </FormField>
          <FormField label={t("cash.reason")} required>
            <input className={inputClass} value={movReason} maxLength={500} onChange={(e) => setMovReason(e.target.value)} />
          </FormField>
        </div>
      </Modal>

      <Modal
        open={closing}
        title={t("cash.closeShift")}
        onClose={() => setClosing(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setClosing(false)}>{t("common.cancel")}</Button>
            <Button variant="danger" onClick={doClose} loading={busy}>{t("cash.closeShift")}</Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-500">{t("cash.countFirst")}</p>
          <FormField label={t("cash.counted")} required>
            <input className={inputClass} inputMode="decimal" value={actualText} onChange={(e) => setActualText(e.target.value)} />
          </FormField>
          <FormField label={t("cash.note")} required={noteRequired} error={noteRequired && !closeNote.trim() ? t("cash.err.noteRequired") : null}>
            <textarea className={inputClass} rows={3} maxLength={1000} value={closeNote} onChange={(e) => setCloseNote(e.target.value)} />
          </FormField>
        </div>
      </Modal>

      <Modal
        open={voidTarget !== null}
        title={t("cash.void")}
        onClose={() => setVoidTarget(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setVoidTarget(null)}>{t("common.cancel")}</Button>
            <Button variant="danger" onClick={doVoid} loading={busy}>{t("cash.void")}</Button>
          </>
        }
      >
        <FormField label={t("cash.reason")} required>
          <input className={inputClass} value={voidReason} maxLength={500} onChange={(e) => setVoidReason(e.target.value)} />
        </FormField>
      </Modal>

      <Modal
        open={reviewTarget !== null}
        title={t("cash.review")}
        onClose={() => setReviewTarget(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setReviewTarget(null)}>{t("common.cancel")}</Button>
            <Button onClick={doReview} loading={busy}>{t("common.confirm")}</Button>
          </>
        }
      >
        <FormField label={t("cash.note")}>
          <textarea className={inputClass} rows={3} maxLength={1000} value={reviewNote} onChange={(e) => setReviewNote(e.target.value)} />
        </FormField>
      </Modal>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="mt-0.5 font-semibold text-slate-900">{value}</dd>
    </div>
  );
}
