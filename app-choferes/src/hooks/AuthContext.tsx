import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { Alert, AppState } from 'react-native';
import { AxiosError } from 'axios';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { setAccessToken, setForbiddenHandler, setRefreshHandler, setUnauthorizedHandler, type ForbiddenInfo } from '@config/axios';
import { clearOfflineData, countByStatus, drainOutbox } from '@/offline';
import { useAppStore } from '@/stores';
import { sessionStorage, type AuthUser, type StoredSession } from '@services/auth/tokenStorage';
import {
  confirmMfa as apiConfirmMfa,
  enrollMfa as apiEnrollMfa,
  fetchLegalStatus,
  fetchMe,
  login as apiLogin,
  refreshTokens,
  serverLogout,
  switchTenant as apiSwitchTenant,
  toSession,
  verifyMfa as apiVerifyMfa,
  type BrowserConfirmationDto,
  type LoginOutcome,
  type MfaChallenge,
  type MfaEnrollResultDto,
} from '@services/auth/teikemAuth';
import { getBiometricEnabled, setBiometricEnabled as storeBiometricEnabled, shouldLock } from '@services/auth/biometricAuth';

interface LoginInput {
  userName: string;
  password: string;
}

/** Cómo terminó el paso de la contraseña: entró, falta el código MFA, falta confirmar el teléfono por correo, falta el primer ingreso en la web, o falló. */
export type LoginResult = 'ok' | 'mfa' | 'browser' | 'onboarding' | 'error';

interface AuthContextValue {
  isLoading: boolean; // restaurando la sesión al arrancar
  isLoggedIn: boolean;
  user: AuthUser | null;
  loading: boolean; // un paso del login en curso
  /** Hay un paso de MFA pendiente (después de la contraseña): la pantalla del código. */
  mfaChallenge: MfaChallenge | null;
  /** Pide confirmar este teléfono por correo (lo manda el servidor). */
  browserConfirmation: BrowserConfirmationDto | null;
  /** ¿Falta aceptar la política de privacidad y los términos de uso? (null = todavía no se sabe) */
  legalRequired: boolean | null;
  /** La app está bloqueada y espera la huella. */
  locked: boolean;
  /** «Abrir con huella» activo. */
  biometricEnabled: boolean;
  login: (input: LoginInput) => Promise<LoginResult>;
  verifyMfa: (code: string) => Promise<boolean>;
  enrollMfa: () => Promise<MfaEnrollResultDto | null>;
  confirmMfa: (code: string) => Promise<string[] | null>;
  /** Termina la inscripción del MFA con los códigos de recuperación ya vistos: vuelve al inicio de sesión. */
  finishEnrollment: () => void;
  cancelMfa: () => void;
  logout: () => Promise<void>;
  /** Cambia la compañía activa (usuarios con acceso a varias). */
  switchTenant: (tenantId: number) => Promise<boolean>;
  /** El token de acceso vigente, para las llamadas de las pantallas del documento legal. */
  accessToken: () => string | null;
  legalAccepted: () => void;
  setBiometricEnabled: (value: boolean) => Promise<void>;
  unlock: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

/** Mensaje del servidor (título del ProblemDetails) o null. */
function serverMessage(err: unknown): string | null {
  const data = (err as AxiosError<{ title?: string }>).response?.data;
  return data && typeof data.title === 'string' && data.title.trim() ? data.title : null;
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const [isLoading, setIsLoading] = useState(true);
  const [loading, setLoading] = useState(false);
  const [session, setSession] = useState<StoredSession | null>(null);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [mfaChallenge, setMfaChallenge] = useState<MfaChallenge | null>(null);
  const [browserConfirmation, setBrowserConfirmation] = useState<BrowserConfirmationDto | null>(null);
  const [legalRequired, setLegalRequired] = useState<boolean | null>(null);
  const [locked, setLocked] = useState(false);
  const [biometricEnabled, setBiometricEnabledState] = useState(false);

  const lang = () => (i18n.language === 'en' ? 'en' : 'es');

  useEffect(() => {
    setAccessToken(session?.accessToken ?? null);
  }, [session]);

  const sessionRef = useRef<StoredSession | null>(null);
  useEffect(() => {
    sessionRef.current = session;
  }, [session]);

  // Guarda la sesión y, ya con ella, trae quién es y si falta aceptar el documento legal.
  const applySession = useCallback(async (s: StoredSession) => {
    setSession(s);
    setAccessToken(s.accessToken);
    await sessionStorage.save(s);
    try {
      const me = await fetchMe(s.accessToken);
      setUser(me);
      await sessionStorage.save({ ...s, user: me });
    } catch {
      /* sin red: queda el nombre guardado */
    }
    try {
      const legal = await fetchLegalStatus(s.accessToken);
      setLegalRequired(Boolean(legal.required) && !legal.accepted);
    } catch {
      setLegalRequired(false); // si no se puede leer, el servidor igual lo exige
    }
  }, []);

  // El cierre de sesión deja el teléfono LIMPIO para el siguiente chofer: todo lo que quede (caché, cola de envío) se vería como los envíos del anterior.
  // Primero se intenta enviar lo pendiente, que todavía es de ESTE chofer y necesita su sesión.
  const logout = useCallback(async () => {
    try {
      await drainOutbox();
    } catch {
      /* sin red: lo que no salió se pierde (el Perfil avisa antes) */
    }
    const current = sessionRef.current;
    if (current) await serverLogout(current.refreshToken);
    setSession(null);
    setUser(null);
    setMfaChallenge(null);
    setBrowserConfirmation(null);
    setLegalRequired(null);
    setLocked(false);
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

  // 403 = el servidor te conoce pero no te deja usar esto (no eres chofer, sin permiso…). Se avisa UNA vez con el mensaje del servidor y NO se cierra la sesión:
  // un permiso que falta en una acción no debe sacar al chofer de la app.
  const forbiddenShown = useRef(false);
  useEffect(() => {
    const handler = (info: ForbiddenInfo & { title?: string }) => {
      if (forbiddenShown.current) return;
      forbiddenShown.current = true;
      Alert.alert(t('LOGIN.ACCESS_DENIED_TITLE'), info.title ?? t('LOGIN.ACCESS_NOT_PROVISIONED'), [
        { text: 'OK', onPress: () => { forbiddenShown.current = false; } },
      ]);
    };
    setForbiddenHandler(handler);
    return () => setForbiddenHandler(null);
  }, [t]);

  // Renovación silenciosa para el manejador del 401. Un token de acceso vencido es lo normal a mitad de una ruta (horas con mala señal) y NO es «sesión cerrada»:
  // solo si el servidor RECHAZA el refresh (vencido o revocado: la sesión de la compañía terminó) se cierra la sesión. Un error de red solo quiere decir «sin señal».
  const refreshHandler = useCallback(async (): Promise<string | null> => {
    const current = sessionRef.current;
    if (!current?.refreshToken || current.refreshExpiresAt < Date.now()) {
      await logoutRef.current();
      return null;
    }
    try {
      const fresh = toSession(await refreshTokens(current.refreshToken), current);
      setSession(fresh);
      await sessionStorage.save(fresh);
      return fresh.accessToken;
    } catch (err) {
      if ((err as AxiosError).response) await logoutRef.current();
      return null;
    }
  }, []);
  useEffect(() => {
    setRefreshHandler(refreshHandler);
    return () => setRefreshHandler(null);
  }, [refreshHandler]);

  // Al arrancar: restaura la sesión (y la renueva si hace falta). Con «Abrir con huella» activo, arranca bloqueada.
  useEffect(() => {
    let active = true;
    (async () => {
      const bio = await getBiometricEnabled();
      if (active) setBiometricEnabledState(bio);
      const stored = await sessionStorage.load();
      if (!stored) {
        if (active) setIsLoading(false);
        return;
      }
      // La sesión que fijó la compañía ya terminó: hay que entrar de nuevo.
      if (stored.refreshExpiresAt < Date.now()) {
        await sessionStorage.clear();
        if (active) setIsLoading(false);
        return;
      }
      if (stored.user) setUser(stored.user);
      let current = stored;
      if (stored.expiresAt - Date.now() < 60_000) {
        try {
          current = toSession(await refreshTokens(stored.refreshToken), stored);
        } catch (err) {
          if ((err as AxiosError).response) {
            await sessionStorage.clear(); // el servidor rechazó la sesión: de verdad se cerró
            if (active) setIsLoading(false);
            return;
          }
          current = stored; // sin red al arrancar: se sigue con la sesión guardada (la caché sirve sin señal)
        }
      }
      if (!active) return;
      await applySession(current);
      if (bio) setLocked(true);
      setIsLoading(false);
    })();
    return () => {
      active = false;
    };
  }, [applySession]);

  // Bloqueo con huella al volver de segundo plano.
  const backgroundedAt = useRef<number | null>(null);
  const bioRef = useRef(false);
  bioRef.current = biometricEnabled;
  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'background') backgroundedAt.current = Date.now();
      else if (state === 'active') {
        if (sessionRef.current && shouldLock(bioRef.current, backgroundedAt.current, Date.now())) setLocked(true);
        backgroundedAt.current = null;
      }
    });
    return () => sub.remove();
  }, []);

  const finish = useCallback(
    async (outcome: LoginOutcome): Promise<LoginResult> => {
      if (outcome.status === 'mfa_required') {
        setMfaChallenge(outcome.challenge);
        return 'mfa';
      }
      if (outcome.status === 'browser_confirmation_required') {
        setBrowserConfirmation(outcome.browser);
        return 'browser';
      }
      if (outcome.status === 'onboarding_required') return 'onboarding';
      setMfaChallenge(null);
      setBrowserConfirmation(null);
      await applySession(outcome.session);
      return 'ok';
    },
    [applySession],
  );

  const showError = useCallback(
    (err: unknown) => {
      const ax = err as AxiosError;
      if (!ax.response) Alert.alert(t('LOGIN.NETWORK_ERROR_TITLE'), t('LOGIN.NETWORK_ERROR'));
      else Alert.alert(t('LOGIN.ERROR_TITLE'), serverMessage(err) ?? t('LOGIN.INVALID_CREDENTIALS'));
    },
    [t],
  );

  const login = useCallback(
    async ({ userName, password }: LoginInput): Promise<LoginResult> => {
      try {
        setLoading(true);
        return await finish(await apiLogin(userName, password, lang()));
      } catch (err) {
        showError(err);
        return 'error';
      } finally {
        setLoading(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [finish, showError],
  );

  const verifyMfa = useCallback(
    async (code: string): Promise<boolean> => {
      if (!mfaChallenge) return false;
      try {
        setLoading(true);
        await finish(await apiVerifyMfa(mfaChallenge, code.trim(), lang()));
        return true;
      } catch (err) {
        showError(err);
        return false;
      } finally {
        setLoading(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [mfaChallenge, finish, showError],
  );

  const enrollMfa = useCallback(async (): Promise<MfaEnrollResultDto | null> => {
    if (!mfaChallenge) return null;
    try {
      setLoading(true);
      return await apiEnrollMfa(mfaChallenge, lang());
    } catch (err) {
      showError(err);
      return null;
    } finally {
      setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mfaChallenge, showError]);

  // Confirmar la inscripción devuelve los códigos de recuperación (se ven una sola vez). El servidor no entrega sesión en este paso: al terminar se vuelve al inicio de sesión (igual que la web).
  const confirmMfa = useCallback(
    async (code: string): Promise<string[] | null> => {
      if (!mfaChallenge) return null;
      try {
        setLoading(true);
        return await apiConfirmMfa(mfaChallenge, code.trim(), lang());
      } catch (err) {
        showError(err);
        return null;
      } finally {
        setLoading(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [mfaChallenge, showError],
  );

  const finishEnrollment = useCallback(() => {
    setMfaChallenge(null);
  }, []);

  const cancelMfa = useCallback(() => {
    setMfaChallenge(null);
    setBrowserConfirmation(null);
  }, []);

  const switchTenant = useCallback(
    async (tenantId: number): Promise<boolean> => {
      const current = sessionRef.current;
      if (!current) return false;
      try {
        setLoading(true);
        // Cambiar de compañía limpia lo local: lo que no se haya enviado se perdería. Primero se intenta enviar; si queda algo (sin señal), NO se cambia.
        await drainOutbox().catch(() => undefined);
        const pending = await countByStatus('pending');
        if (pending > 0) {
          Alert.alert(t('PROFILE.COMPANY_SWITCH_TITLE'), t('PROFILE.COMPANY_SWITCH_PENDING', { count: pending }));
          return false;
        }
        const next = toSession(await apiSwitchTenant(current.refreshToken, tenantId), current);
        // otra compañía = otros envíos: se limpia lo local como en un cierre de sesión (la sesión sigue)
        await clearOfflineData();
        queryClient.clear();
        useAppStore.setState({ manifestIds: [], manifests: [], shipmentIds: [], shipment: null });
        await applySession({ ...next, user: undefined });
        return true;
      } catch (err) {
        showError(err);
        return false;
      } finally {
        setLoading(false);
      }
    },
    [applySession, queryClient, showError, t],
  );

  const setBiometricEnabled = useCallback(async (value: boolean) => {
    await storeBiometricEnabled(value);
    setBiometricEnabledState(value);
    if (!value) setLocked(false);
  }, []);

  return (
    <AuthContext.Provider
      value={{
        isLoading,
        isLoggedIn: Boolean(session?.accessToken),
        user,
        loading,
        mfaChallenge,
        browserConfirmation,
        legalRequired,
        locked,
        biometricEnabled,
        login,
        verifyMfa,
        enrollMfa,
        confirmMfa,
        finishEnrollment,
        cancelMfa,
        logout,
        switchTenant,
        accessToken: () => sessionRef.current?.accessToken ?? null,
        legalAccepted: () => setLegalRequired(false),
        setBiometricEnabled,
        unlock: () => setLocked(false),
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
