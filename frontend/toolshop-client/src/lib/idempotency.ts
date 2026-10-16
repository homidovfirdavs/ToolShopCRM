import { isUncertainOutcome } from "./apiError";

/**
 * A new Idempotency-Key: one per user INTENTION (opening the payment form), not per HTTP attempt. Sending the same request
 * again with the same key can never record the payment twice; a new intention gets a new key.
 * crypto.randomUUID() exists only on secure pages (HTTPS / localhost), so a phone on plain HTTP falls back to
 * getRandomValues, and finally to Math.random (a key only has to be unique, it is not a secret).
 */
export function newIdempotencyKey(): string {
  const c: Crypto | undefined = globalThis.crypto;
  if (c && typeof c.randomUUID === "function") return c.randomUUID();

  const bytes = new Uint8Array(16);
  if (c && typeof c.getRandomValues === "function") c.getRandomValues(bytes);
  else for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
}

/** Waits between automatic re-sends after an uncertain outcome. */
export const UNCERTAIN_RETRY_DELAYS_MS = [500, 1500];

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/**
 * Runs `send` and, ONLY when the outcome is uncertain (no answer, timeout, 5xx), runs the SAME request again after a short
 * wait. This is safe only because `send` carries one fixed Idempotency-Key: the server stores the work once per key and answers
 * a repeat with the stored result. Definite answers (2xx, 4xx) and non-HTTP errors are never re-sent.
 */
export async function retryUncertain<T>(send: () => Promise<T>, delaysMs: number[] = UNCERTAIN_RETRY_DELAYS_MS): Promise<T> {
  for (let attempt = 0; ; attempt++) {
    try {
      return await send();
    } catch (err) {
      if (!isUncertainOutcome(err) || attempt >= delaysMs.length) throw err;
      await sleep(delaysMs[attempt]);
    }
  }
}
