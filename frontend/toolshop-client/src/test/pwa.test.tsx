import { describe, it, expect, vi, afterEach } from "vitest";
import { act, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { InternalAxiosRequestConfig } from "axios";
import apiClient from "../api/client";
import { errorCode, errorMessage } from "../lib/apiError";
import OfflineBanner from "../components/pwa/OfflineBanner";
import UpdatePrompt from "../components/pwa/UpdatePrompt";
import InstallButton from "../components/pwa/InstallButton";
import { dismissPwaUpdate, setNeedRefresh } from "../pwa/pwaStore";
import { translations } from "../i18n/translations";
import { renderApp, T } from "./utils";

function setOnline(value: boolean) {
  vi.spyOn(navigator, "onLine", "get").mockReturnValue(value);
}

/** Replaces the network layer; returns the list of requests that actually "left" the browser. */
function fakeNetwork() {
  const sent: InternalAxiosRequestConfig[] = [];
  const original = apiClient.defaults.adapter;
  apiClient.defaults.adapter = async (config) => {
    sent.push(config);
    return { data: {}, status: 200, statusText: "OK", headers: {}, config };
  };
  return {
    sent,
    restore: () => {
      apiClient.defaults.adapter = original;
    },
  };
}

afterEach(() => {
  dismissPwaUpdate();
});

describe("offline guard (api client)", () => {
  it("rejects writes while offline and never sends them (sale, payment, stock adjustment)", async () => {
    const net = fakeNetwork();
    try {
      setOnline(false);
      for (const [method, url] of [
        ["post", "/orders"],
        ["post", "/orders/5/complete"],
        ["post", "/payments"],
        ["post", "/inventory/adjust"],
        ["put", "/products/3"],
        ["delete", "/expenses/2"],
      ] as const) {
        const err = await apiClient.request({ method, url, data: {} }).then(
          () => null,
          (e: unknown) => e,
        );
        expect(err, `${method} ${url}`).not.toBeNull();
        expect(errorCode(err)).toBe("OFFLINE");
        expect(errorMessage(err)).toBe(translations.tj["offline.blocked"]);
      }
      expect(net.sent).toHaveLength(0);
    } finally {
      net.restore();
    }
  });

  it("uses the selected language for the offline message", async () => {
    const net = fakeNetwork();
    try {
      setOnline(false);
      localStorage.setItem("toolshop_lang", "ru");
      const err = await apiClient.post("/orders", {}).catch((e: unknown) => e);
      expect(errorMessage(err)).toBe(translations.ru["offline.blocked"]);
    } finally {
      net.restore();
    }
  });

  it("lets reads through while offline (they fail on their own; no stale data is served)", async () => {
    const net = fakeNetwork();
    try {
      setOnline(false);
      await apiClient.get("/orders");
      expect(net.sent).toHaveLength(1);
    } finally {
      net.restore();
    }
  });

  it("keeps normal handling of token refresh / logout while offline", async () => {
    const net = fakeNetwork();
    try {
      setOnline(false);
      await apiClient.post("/auth/refresh", { refreshToken: "x" });
      await apiClient.post("/auth/logout", { refreshToken: "x" });
      expect(net.sent.map((c) => c.url)).toEqual(["/auth/refresh", "/auth/logout"]);
    } finally {
      net.restore();
    }
  });

  it("sends writes normally while online", async () => {
    const net = fakeNetwork();
    try {
      setOnline(true);
      await apiClient.post("/orders", {});
      expect(net.sent).toHaveLength(1);
    } finally {
      net.restore();
    }
  });
});

describe("OfflineBanner", () => {
  it("is hidden online and appears when the connection drops", async () => {
    setOnline(true);
    renderApp(<OfflineBanner />);
    expect(screen.queryByTestId("offline-banner")).not.toBeInTheDocument();

    setOnline(false);
    await act(async () => {
      window.dispatchEvent(new Event("offline"));
    });
    expect(screen.getByTestId("offline-banner")).toHaveTextContent(T("offline.banner"));

    setOnline(true);
    await act(async () => {
      window.dispatchEvent(new Event("online"));
    });
    expect(screen.queryByTestId("offline-banner")).not.toBeInTheDocument();
  });
});

describe("UpdatePrompt", () => {
  it("shows only when an update is waiting and applies it only after the user taps Reload", async () => {
    const apply = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderApp(<UpdatePrompt />);
    expect(screen.queryByTestId("update-prompt")).not.toBeInTheDocument();

    act(() => setNeedRefresh(true, apply));
    expect(screen.getByTestId("update-prompt")).toBeInTheDocument();
    expect(apply).not.toHaveBeenCalled(); // no silent reload

    await user.click(screen.getByRole("button", { name: T("pwa.reload") }));
    expect(apply).toHaveBeenCalledTimes(1);
  });

  it("'Later' hides the prompt without updating", async () => {
    const apply = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderApp(<UpdatePrompt />);
    act(() => setNeedRefresh(true, apply));
    await user.click(screen.getByRole("button", { name: T("pwa.later") }));
    expect(screen.queryByTestId("update-prompt")).not.toBeInTheDocument();
    expect(apply).not.toHaveBeenCalled();
  });
});

describe("InstallButton", () => {
  it("is hidden when the browser offers no install path", () => {
    renderApp(<InstallButton />);
    expect(screen.queryByTestId("install-button")).not.toBeInTheDocument();
  });

  it("Chrome/Edge (Windows, macOS, Android): uses the browser install prompt", async () => {
    const user = userEvent.setup();
    renderApp(<InstallButton />);
    const prompt = vi.fn().mockResolvedValue(undefined);
    const ev = Object.assign(new Event("beforeinstallprompt", { cancelable: true }), {
      prompt,
      userChoice: Promise.resolve({ outcome: "accepted" as const }),
    });
    await act(async () => {
      window.dispatchEvent(ev);
    });
    expect(ev.defaultPrevented).toBe(true);
    await user.click(screen.getByTestId("install-button"));
    expect(prompt).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId("install-button")).not.toBeInTheDocument();
  });

  it("iPhone/iPad Safari: shows the Add to Home Screen steps", async () => {
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue(
      "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 Version/17.5 Mobile/15E148 Safari/604.1",
    );
    const user = userEvent.setup();
    renderApp(<InstallButton />);
    await user.click(screen.getByTestId("install-button"));
    expect(screen.getByRole("dialog", { name: T("pwa.iosTitle") })).toBeInTheDocument();
    expect(screen.getByText(T("pwa.iosStep3"))).toBeInTheDocument();
  });

  it("is hidden when already running as an installed app", () => {
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X)");
    window.matchMedia = vi.fn().mockReturnValue({ matches: true, addEventListener() {}, removeEventListener() {} });
    try {
      renderApp(<InstallButton />);
      expect(screen.queryByTestId("install-button")).not.toBeInTheDocument();
    } finally {
      // @ts-expect-error jsdom has no matchMedia by default
      delete window.matchMedia;
    }
  });
});
