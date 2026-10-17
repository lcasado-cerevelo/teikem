// Cliente único del API: openapi-fetch sobre los tipos generados (schema.d.ts, `npm run api:types`; nunca DTOs a mano).
// - La URL del API sale de EXPO_PUBLIC_API_URL (o `extra.apiUrl` de la configuración de Expo).
// - Authorization: Bearer <access token> de la sesión, salvo las llamadas con su propio encabezado (challenge del MFA) y las públicas.
// - 401 → renueva la sesión UNA vez (una sola renovación aunque fallen varias llamadas a la vez) y reintenta; si no se puede, cierra la sesión local y avisa a la raíz (vuelve al login).
// - Sin red (fetch lanza) → ApiError con code 'network'.
import Constants from 'expo-constants'
import createClient from 'openapi-fetch'

import { getLang } from '../i18n/i18n'
import { clearSession, getSessionState, saveSession } from '../auth/session'
import { toApiError } from './problem'
import type { paths } from './schema'

export { ApiError, apiErrorMessage, isNetworkError, toApiError } from './problem'

const DEFAULT_URL = 'http://localhost:5000'

export function getApiBaseUrl(): string {
  const fromEnv = process.env.EXPO_PUBLIC_API_URL
  const fromConfig = (Constants.expoConfig?.extra as { apiUrl?: string } | undefined)?.apiUrl
  return (fromEnv || fromConfig || DEFAULT_URL).replace(/\/+$/, '')
}

/** Rutas que nunca disparan una renovación de sesión al responder 401 (credenciales, renovación y pasos del acceso). */
const NO_REFRESH = ['/api/v1/portal/auth/login', '/api/v1/portal/auth/config', '/api/v1/auth/refresh', '/api/v1/auth/confirm-browser', '/api/v1/legal/current']

let authLostHandler: (() => void) | null = null
/** La raíz registra qué hacer cuando la sesión no se puede recuperar (vuelve al login). */
export function setAuthLostHandler(handler: (() => void) | null): void {
  authLostHandler = handler
}

function pathOf(url: string): string {
  try {
    return new URL(url).pathname
  } catch {
    return url
  }
}

export interface ApiClientOptions {
  /** fetch subyacente; por defecto el global. Las pruebas lo sustituyen. */
  fetch?: (request: Request) => Promise<Response>
  baseUrl?: string
}

export function createApiClient(options: ApiClientOptions = {}) {
  const baseFetch = options.fetch ?? ((r: Request) => globalThis.fetch(r))
  const baseUrl = options.baseUrl ?? getApiBaseUrl()

  async function safeFetch(request: Request): Promise<Response> {
    try {
      return await baseFetch(request)
    } catch {
      return Response.error()   // status 0: falla de red real
    }
  }

  const bare = createClient<paths>({ baseUrl, fetch: safeFetch })
  let refreshing: Promise<boolean> | null = null

  async function doRefresh(): Promise<boolean> {
    const session = getSessionState().session
    if (!session) return false
    const { data, response } = await bare.POST('/api/v1/auth/refresh', { body: { refreshToken: session.refreshToken }, headers: { 'Accept-Language': getLang() } })
    if (!response.ok || !data?.accessToken || !data.refreshToken) return false
    await saveSession({
      ...session,
      accessToken: data.accessToken,
      accessExpiresAtUtc: data.accessExpiresAtUtc ?? session.accessExpiresAtUtc,
      refreshToken: data.refreshToken,
      refreshExpiresAtUtc: data.refreshExpiresAtUtc ?? session.refreshExpiresAtUtc,
      tenantId: data.tenantId ?? session.tenantId,
    })
    return true
  }

  function refreshOnce(): Promise<boolean> {
    refreshing ??= doRefresh().finally(() => {
      refreshing = null
    })
    return refreshing
  }

  function prepare(template: Request): { request: Request; explicitAuth: boolean } {
    const request = template.clone()
    const explicitAuth = request.headers.has('Authorization')
    const session = getSessionState().session
    if (session && !explicitAuth) request.headers.set('Authorization', `Bearer ${session.accessToken}`)
    if (!request.headers.has('Accept-Language')) request.headers.set('Accept-Language', getLang())
    return { request, explicitAuth }
  }

  async function authFetch(template: Request): Promise<Response> {
    const first = prepare(template)
    let response = await safeFetch(first.request)
    if (response.status === 401 && !first.explicitAuth && !NO_REFRESH.includes(pathOf(template.url)) && getSessionState().session) {
      if (!(await refreshOnce())) {
        await clearSession()
        authLostHandler?.()
        return response
      }
      response = await safeFetch(prepare(template).request)
    }
    return response
  }

  return createClient<paths>({ baseUrl, fetch: authFetch })
}

export const api = createApiClient()

/** Espera la llamada y devuelve `data`, o lanza ApiError con el ProblemDetails (o code 'network' sin respuesta). */
export async function unwrap<T extends { response: Response; data?: unknown }>(call: Promise<T>): Promise<NonNullable<T['data']>> {
  const result = (await call) as T & { data?: unknown; error?: unknown }
  if (result.response.status === 0) throw toApiError(null)
  if (result.error !== undefined || !result.response.ok) throw toApiError(result.error, result.response)
  return result.data as NonNullable<T['data']>
}
