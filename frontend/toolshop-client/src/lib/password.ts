/** Same rule as the server (Identity): >= 8 chars, upper, lower, digit. The server stays authoritative. */
export function isStrongPassword(p: string): boolean {
  return p.length >= 8 && /[a-z]/.test(p) && /[A-Z]/.test(p) && /\d/.test(p);
}
