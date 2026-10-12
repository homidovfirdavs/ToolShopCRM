import {
  createContext,
  useContext,
  useState,
  useEffect,
  type ReactNode,
} from "react";
import apiClient, { setAuthToken } from "../api/client";
import { errorStatus } from "../lib/apiError";

interface UserInfo {
  id: string;
  username: string;
  fullName: string;
  email: string;
  roles: string[];
}

interface AuthContextType {
  user: UserInfo | null;
  accessToken: string | null;
  isLoading: boolean;
  login: (usernameOrEmail: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const REFRESH_TOKEN_KEY = "toolshop_refresh_token";

// One refresh request at a time. React StrictMode (dev) runs effects twice; two concurrent
// requests with the same single-use refresh token would make the second one fail (401)
// and wrongly clear the freshly stored token.
let refreshInFlight: Promise<{ data: { accessToken: string; refreshToken: string; user: UserInfo } }> | null = null;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(() => !!sessionStorage.getItem(REFRESH_TOKEN_KEY));

  useEffect(() => {
    const storedRefreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);

    if (!storedRefreshToken) {
      return;
    }

    refreshInFlight ??= apiClient
      .post("/auth/refresh", { refreshToken: storedRefreshToken })
      .finally(() => {
        window.setTimeout(() => {
          refreshInFlight = null;
        }, 0);
      });

    refreshInFlight
      .then((response) => {
        const { accessToken, refreshToken, user } = response.data;
                setAccessToken(accessToken);
        setAuthToken(accessToken);
        setUser(user);
        sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
      })
      .catch((err) => {
        // Only a server rejection ends the session. A network failure (offline start) keeps the
        // token so the user does not have to sign in again once the connection is back.
        const status = errorStatus(err);
        if (status === 400 || status === 401) sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      })
      .finally(() => {
        setIsLoading(false);
      });
  }, []);

  async function login(usernameOrEmail: string, password: string) {
    const response = await apiClient.post("/auth/login", {
      usernameOrEmail,
      password,
    });

    const { accessToken, refreshToken, user } = response.data;
       setAccessToken(accessToken);
    setAuthToken(accessToken);
    setUser(user);
    sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  }

  async function logout() {
    const storedRefreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);

    if (storedRefreshToken) {
      try {
        await apiClient.post("/auth/logout", {
          refreshToken: storedRefreshToken,
        });
      } catch {
        // Ignore network errors during logout; clear local state anyway.
      }
    }

        sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    setAccessToken(null);
    setAuthToken(null);
    setUser(null);
  }

  return (
    <AuthContext.Provider
      value={{ user, accessToken, isLoading, login, logout }}
    >
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}