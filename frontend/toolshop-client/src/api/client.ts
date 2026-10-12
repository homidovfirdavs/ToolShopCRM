import axios from "axios";
import { translations, type Lang } from "../i18n/translations";

// Same-origin "/api" behind a reverse proxy in production; override with VITE_API_URL.
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? "http://localhost:5252/api",
  headers: {
    "Content-Type": "application/json",
  },
});

export function setAuthToken(token: string | null) {
  if (token) {
    apiClient.defaults.headers.common["Authorization"] = `Bearer ${token}`;
  } else {
    delete apiClient.defaults.headers.common["Authorization"];
  }
}

const READ_METHODS = new Set(["get", "head", "options"]);
// Token refresh / logout fail on their own when offline and must keep their normal handling.
const OFFLINE_EXEMPT = ["/auth/refresh", "/auth/logout"];

function offlineMessage(): string {
  let lang: Lang = "tj";
  try {
    const saved = localStorage.getItem("toolshop_lang");
    if (saved === "en" || saved === "ru" || saved === "tj") lang = saved;
  } catch {
    /* ignore */
  }
  return translations[lang]["offline.blocked"] ?? "You are offline.";
}

// Offline guard: sales, payments, stock changes and every other write need the server.
// While the browser is offline, writes are rejected here and never queued or retried later.
apiClient.interceptors.request.use((config) => {
  const method = (config.method ?? "get").toLowerCase();
  const url = config.url ?? "";
  if (!navigator.onLine && !READ_METHODS.has(method) && !OFFLINE_EXEMPT.some((p) => url.startsWith(p))) {
    return Promise.reject(
      Object.assign(new Error("OFFLINE"), {
        response: { status: 0, data: { code: "OFFLINE", message: offlineMessage() } },
      }),
    );
  }
  return config;
});

export default apiClient;
