import { useMemo } from "react";
import { useAuth } from "../context/AuthContext";

function decodePermissions(token: string | null): string[] {
  if (!token) return [];
  try {
    const payload = token.split(".")[1];
    const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
    const data = JSON.parse(json) as Record<string, unknown>;
    const raw = data["permission"];
    if (Array.isArray(raw)) return raw.map(String);
    if (typeof raw === "string") return [raw];
  } catch {
    /* ignore */
  }
  return [];
}

/** UI-only helper. Backend authorization remains authoritative. */
export function usePermissions() {
  const { user, accessToken } = useAuth();
  const permissions = useMemo(() => decodePermissions(accessToken), [accessToken]);
  const isAdmin = !!user?.roles.includes("Admin");

  const has = (permission: string) => isAdmin || permissions.includes(permission);
  const hasRole = (...roles: string[]) => !!user && roles.some((r) => user.roles.includes(r));

  return { permissions, has, hasRole, isAdmin };
}
