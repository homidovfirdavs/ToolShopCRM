import {
  createContext,
  useContext,
  useState,
  useEffect,
  type ReactNode,
} from "react";
import apiClient, { setAuthToken } from "../api/client";

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

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const storedRefreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);

    if (!storedRefreshToken) {
      setIsLoading(false);
      return;
    }

    apiClient
      .post("/auth/refresh", { refreshToken: storedRefreshToken })
      .then((response) => {
        const { accessToken, refreshToken, user } = response.data;
                setAccessToken(accessToken);
        setAuthToken(accessToken);
        setUser(user);
        sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
      })
      .catch(() => {
        sessionStorage.removeItem(REFRESH_TOKEN_KEY);
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

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}