/**
 * Client-side check: 12..128 characters, no forced digits or capitals (a long phrase is fine).
 * The server stays authoritative: it also rejects common or obvious passwords.
 */
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;

export function isStrongPassword(p: string): boolean {
  return p.length >= PASSWORD_MIN_LENGTH && p.length <= PASSWORD_MAX_LENGTH;
}
