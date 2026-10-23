import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { env } from './env';

// ---------------------------------------------------------------------------
// In-memory access token holder.
// The auth layer calls setAccessToken() on login/refresh/logout. Keeping it
// here (instead of importing the auth module) avoids a circular dependency.
// ---------------------------------------------------------------------------
let accessToken: string | null = null;
export function setAccessToken(token: string | null): void {
  accessToken = token;
}
export function getAccessToken(): string | null {
  return accessToken;
}

// Optional hook the auth layer can register to be notified of a 401 that the
// refresh handler couldn't recover from (e.g. to clear the session and
// redirect to Login).
let onUnauthorized: (() => void) | null = null;
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler;
}

// Optional hook the auth layer registers to silently refresh an expired
// access token. Returns the new token to retry the request, or null if the
// caller should give up on this request (the handler itself decides whether
// that also means logging out — see AuthContext).
let onRefresh: (() => Promise<string | null>) | null = null;
export function setRefreshHandler(handler: (() => Promise<string | null>) | null): void {
  onRefresh = handler;
}

// El API responde 403 cuando el token es válido pero el usuario no puede usar
// el sistema: su 'sub' de Auth0 no está mapeado en Users.Auth0, o está
// desactivado. Auth0 autentica igual (no sabe nada de nuestra tabla Users), así
// que sin esto el login "entra" y la app se queda en blanco para siempre — que
// es exactamente el bug que reportó Luis.
export interface ForbiddenInfo {
  code?: string; // 'not_provisioned' | 'user_inactive' | 'no_sub'
  sub?: string;
  error?: string;
}
let onForbidden: ((info: ForbiddenInfo) => void) | null = null;
export function setForbiddenHandler(handler: ((info: ForbiddenInfo) => void) | null): void {
  onForbidden = handler;
}

export const api = axios.create({
  baseURL: env.baseUrl,
  timeout: 20000,
  headers: { 'Content-Type': 'application/json' },
});

// Attach the Bearer token on every request. The backend is JWT-only now —
// there is no ApiKey header anymore.
api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (accessToken) {
    config.headers.set('Authorization', `Bearer ${accessToken}`);
  }
  return config;
});

// On a 401, try ONE silent token refresh + retry before giving up. Without
// this, a merely-expired access token (very plausible on a multi-hour route
// with spotty signal) looked identical to a real "you're logged out" and blew
// away the whole session on the first API call after reconnecting.
api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as
      | (InternalAxiosRequestConfig & { _retriedAfterRefresh?: boolean })
      | undefined;
    if (error.response?.status === 401 && config && !config._retriedAfterRefresh && onRefresh) {
      config._retriedAfterRefresh = true;
      const newToken = await onRefresh();
      if (newToken) {
        config.headers.set('Authorization', `Bearer ${newToken}`);
        return api(config);
      }
      // onRefresh() itself decides whether "couldn't refresh" means a real
      // logout (invalid/revoked refresh token) or just "offline right now" —
      // it calls onUnauthorized internally only in the former case.
      return Promise.reject(error);
    }
    if (error.response?.status === 401 && !onRefresh && onUnauthorized) {
      onUnauthorized();
    }
    if (error.response?.status === 403 && onForbidden) {
      onForbidden((error.response.data ?? {}) as ForbiddenInfo);
    }
    return Promise.reject(error);
  },
);
