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
