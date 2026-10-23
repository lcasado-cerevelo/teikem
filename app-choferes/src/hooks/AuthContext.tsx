import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { Alert } from 'react-native';
import { AxiosError } from 'axios';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import {
  setAccessToken,
  setForbiddenHandler,
  setRefreshHandler,
  setUnauthorizedHandler,
  type ForbiddenInfo,
} from '@config/axios';
import { clearOfflineData, drainOutbox } from '@/offline';
import { useAppStore } from '@/stores';
import { sessionStorage, type StoredSession } from '@services/auth/tokenStorage';
import {
  passwordRealmLogin,
  refreshSession,
  fetchProfile,
  type AuthUser,
} from '@services/auth/auth0';

interface LoginInput {
  userName: string;
  password: string;
}

interface AuthContextValue {
  isLoading: boolean; // restoring session on startup
  isLoggedIn: boolean;
  user: AuthUser | null;
  loading: boolean; // login request in progress
  // Resolves true on success so the Login screen can chain follow-ups
  // (e.g. offering to enable biometric sign-in).
  login: (input: LoginInput) => Promise<boolean>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [isLoading, setIsLoading] = useState(true);
  const [loading, setLoading] = useState(false);
  const [session, setSession] = useState<StoredSession | null>(null);
  const [user, setUser] = useState<AuthUser | null>(null);

  // Keep the axios Bearer token in sync with the session.
  useEffect(() => {
    setAccessToken(session?.accessToken ?? null);
  }, [session]);

  const sessionRef = useRef<StoredSession | null>(null);
  useEffect(() => {
    sessionRef.current = session;
  }, [session]);

  const applySession = useCallback(async (s: StoredSession) => {
    setSession(s);
    await sessionStorage.save(s);
    // Cache the profile alongside the token so the driver's name/initials
    // still show on a cold start with no signal (see restore effect below).
    const profile = await fetchProfile(s.accessToken);
    if (profile) {
      setUser(profile);
      await sessionStorage.save({ ...s, user: profile });
    }
  }, []);

  // Logout must leave the phone CLEAN for the next driver. Anything that
  // survives here (persisted query cache, outbox, zustand bridge) shows up as
  // the previous driver's shipments after the next login — the static ['me']
  // query key even serves the old identity. Best-effort drain first: queued
  // deliveries still belong to THIS driver and need this driver's token.
  const logout = useCallback(async () => {
    try {
      await drainOutbox();
    } catch {
      /* offline: whatever couldn't be sent is dropped below (Profile warns first) */
    }
    setSession(null);
    setUser(null);
    setAccessToken(null);
    await sessionStorage.clear();
    useAppStore.setState({ manifestIds: [], manifests: [], shipmentIds: [], shipment: null });
    queryClient.clear();
    await clearOfflineData();
  }, [queryClient]);

  const logoutRef = useRef(logout);
  logoutRef.current = logout;
  useEffect(() => {
    setUnauthorizedHandler(() => {
      void logoutRef.current();
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  // 403 = Auth0 te autenticó pero el sistema no te puede servir: tu 'sub' no
  // está mapeado en Users.Auth0, o tu usuario está desactivado. Antes esto se
  // tragaba en silencio y el chofer quedaba mirando una pantalla vacía sin
  // nombre ni entregas, creyendo que la app estaba rota.
  //
  // Se avisa UNA sola vez por sesión: cada pantalla dispara varias consultas y
  // todas fallan con 403, así que sin el cerrojo saldrían cinco alertas
  // encimadas. Y se cierra la sesión, porque quedarse "dentro" es mentira.
  const forbiddenShown = useRef(false);
  useEffect(() => {
    const handler = (info: ForbiddenInfo) => {
      if (forbiddenShown.current) return;
      forbiddenShown.current = true;

      const body =
        info.code === 'user_inactive'
          ? t('LOGIN.ACCESS_INACTIVE')
          : t('LOGIN.ACCESS_NOT_PROVISIONED');
      // El sub es EL dato que el administrador necesita para arreglarlo
      // (UPDATE Users SET Auth0 = <sub>), así que se muestra en vez de
      // esconderlo — al chofer no le estorba y a soporte le ahorra el viaje.
      const withCode = info.sub ? `${body}\n\n${t('LOGIN.ACCESS_CODE')}: ${info.sub}` : body;

      Alert.alert(t('LOGIN.ACCESS_DENIED_TITLE'), withCode, [
        {
          text: 'OK',
          onPress: () => {
            forbiddenShown.current = false;
            void logoutRef.current();
          },
        },
      ]);
    };
    setForbiddenHandler(handler);
    return () => setForbiddenHandler(null);
  }, [t]);

  // Silent refresh for axios' 401 handler. A merely-expired access token is
  // extremely likely mid-route (hours with spotty signal) and must NOT read
  // as "logged out" — only an explicit rejection from Auth0 (refresh token
  // itself revoked/expired) is a real logout. A network error just means
  // "still offline"; keep the session and let the caller's request fail
  // normally (the offline outbox/cache already handle that).
  const refreshHandler = useCallback(async (): Promise<string | null> => {
    const current = sessionRef.current;
    if (!current?.refreshToken) {
      await logoutRef.current();
      return null;
    }
    try {
      const fresh = await refreshSession(current.refreshToken);
      await applySession(fresh);
      return fresh.accessToken;
    } catch (err) {
      const ax = err as AxiosError;
      if (ax.response) {
        await logoutRef.current();
      }
      return null;
    }
  }, [applySession]);
  useEffect(() => {
    setRefreshHandler(refreshHandler);
    return () => setRefreshHandler(null);
  }, [refreshHandler]);

  // Restore (and refresh if needed) the session on startup.
  useEffect(() => {
    let active = true;
    (async () => {
      const stored = await sessionStorage.load();
      if (!stored) {
        if (active) setIsLoading(false);
        return;
      }
      // Show the cached identity immediately, before any network round trip.
      if (stored.user) setUser(stored.user);
      let current = stored;
      const nearExpiry = stored.expiresAt - Date.now() < 60_000;
      if (nearExpiry) {
        if (!stored.refreshToken) {
          await sessionStorage.clear();
          if (active) setIsLoading(false);
          return;
        }
        try {
          current = await refreshSession(stored.refreshToken);
        } catch (err) {
          const ax = err as AxiosError;
          if (ax.response) {
            // Auth0 explicitly rejected the refresh token — really logged out.
            await sessionStorage.clear();
            if (active) setIsLoading(false);
            return;
          }
          // No network at startup: keep the stale session so the app still
          // works from cache. The axios refresh-on-401 handler retries this
          // transparently once a real request needs it and signal is back.
          current = stored;
        }
      }
      if (!active) return;
      await applySession(current);
      setIsLoading(false);
    })();
    return () => {
      active = false;
    };
  }, [applySession]);

  const login = useCallback(
    async ({ userName, password }: LoginInput): Promise<boolean> => {
      try {
        setLoading(true);
        const s = await passwordRealmLogin(userName, password);
        await applySession(s);
        return true;
      } catch (err) {
        const ax = err as AxiosError;
        if (!ax.response) {
          Alert.alert(t('LOGIN.NETWORK_ERROR_TITLE'), t('LOGIN.NETWORK_ERROR'));
        } else {
          Alert.alert(t('LOGIN.ERROR_TITLE'), t('LOGIN.INVALID_CREDENTIALS'));
        }
        return false;
      } finally {
        setLoading(false);
      }
    },
    [applySession, t],
  );

  return (
    <AuthContext.Provider
      value={{
        isLoading,
        isLoggedIn: Boolean(session?.accessToken),
        user,
        loading,
        login,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}
