import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { env } from './env';

// ---------------------------------------------------------------------------
// Tokens de acceso EN MEMORIA, uno por compañía (un chofer puede trabajar en varias con una sola sesión).
// La capa de acceso los pone con setCompanyTokens() al entrar y al renovar; vivir aquí (y no importar el módulo de acceso) evita un ciclo de dependencias.
// Cada llamada dice de qué compañía es con `tenantId` en su configuración (axios.get(url, { tenantId })); sin él se usa la primera compañía (solo para las
// llamadas de la sesión que no son de una compañía en particular, como el perfil y el documento legal).
// ---------------------------------------------------------------------------
declare module 'axios' {
  interface AxiosRequestConfig {
    /** Compañía a la que va la llamada (decide con qué token de acceso se firma). */
    tenantId?: number;
  }
}

let companyTokens = new Map<number, string>();
export function setCompanyTokens(tokens: Record<number, string> | null): void {
  companyTokens = new Map(Object.entries(tokens ?? {}).map(([k, v]) => [Number(k), v]));
}
export function getAccessToken(tenantId?: number | null): string | null {
  if (tenantId != null) return companyTokens.get(tenantId) ?? null;
  return companyTokens.values().next().value ?? null;
}

// Gancho opcional: la capa de acceso lo registra para ser avisada de un 401 que la renovación no pudo resolver (para cerrar la sesión).
let onUnauthorized: (() => void) | null = null;
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler;
}

// Gancho que la capa de acceso registra para renovar en silencio los tokens vencidos. Devuelve true si hay tokens nuevos (se reintenta la llamada).
// Decide ella si «no se pudo renovar» es un cierre de sesión real (el servidor rechazó la sesión) o solo «sin señal».
let onRefresh: (() => Promise<boolean>) | null = null;
export function setRefreshHandler(handler: (() => Promise<boolean>) | null): void {
  onRefresh = handler;
}

// El API responde 403 cuando el token es válido pero el usuario no puede usar esa función (no es chofer, falta el permiso…).
export interface ForbiddenInfo {
  code?: string;
  title?: string;
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

// El token de la compañía de cada llamada va como Bearer.
api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = getAccessToken(config.tenantId);
  if (token) config.headers.set('Authorization', `Bearer ${token}`);
  return config;
});

// Ante un 401 se intenta UNA renovación silenciosa y se reintenta. Sin esto, un token de acceso vencido (normal en una ruta de horas con mala señal) se vería
// igual que «sesión cerrada» y tiraría la sesión entera en la primera llamada tras recuperar la señal.
api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as (InternalAxiosRequestConfig & { _retriedAfterRefresh?: boolean }) | undefined;
    if (error.response?.status === 401 && config && !config._retriedAfterRefresh && onRefresh) {
      config._retriedAfterRefresh = true;
      if (await onRefresh()) {
        const token = getAccessToken(config.tenantId);
        if (token) config.headers.set('Authorization', `Bearer ${token}`);
        return api(config);
      }
      return Promise.reject(error);
    }
    if (error.response?.status === 401 && !onRefresh && onUnauthorized) onUnauthorized();
    if (error.response?.status === 403 && onForbidden) onForbidden((error.response.data ?? {}) as ForbiddenInfo);
    return Promise.reject(error);
  },
);
