// Cliente único del API: openapi-fetch sobre los tipos generados (schema.d.ts).
// - Authorization: Bearer <access token> (salvo que la llamada traiga su propio Authorization, p. ej. el challenge MFA).
// - Accept-Language: idioma actual de la interfaz.
// - 401 → refresca el token UNA vez (una sola petición de refresh aunque fallen varias a la vez) y reintenta; si el
//   refresh falla, avisa al shell (vuelve al login).
// - 403 aal2_required → pide reautenticación (modal) y reintenta una vez.
import createClient from 'openapi-fetch'
import { getAccessToken, getRefreshToken, getTokens, setTokens } from '../auth/tokens'
import { getLang } from '../i18n/i18n'
import { toApiError } from './problem'
import type { paths } from './schema'

export { ApiError, applyProblemDetails, normalizeField, toApiError } from './problem'
export type { AppliedProblem, ProblemDetails } from './problem'

export const API_BASE_URL: string = (import.meta.env.VITE_API_URL as string | undefined) ?? ''

// Rutas donde un 401 es la respuesta del propio paso de autenticación (credenciales, código): no se refresca.
const NO_REFRESH = [
  '/api/v1/auth/login',
  '/api/v1/auth/mfa/verify',
  '/api/v1/auth/refresh',
  '/api/v1/auth/logout',
  '/api/v1/auth/switch-tenant',
  '/api/v1/auth/reauth',
]

let authLostHandler: (() => void) | null = null
let stepUpHandler: (() => Promise<boolean>) | null = null

/** El shell registra qué hacer cuando la sesión no se puede recuperar (limpiar y volver al login). */
export function setAuthLostHandler(handler: (() => void) | null): void {
  authLostHandler = handler
}

/** ReauthProvider registra el modal de reautenticación; devuelve true si el usuario se reautenticó. */
export function setStepUpHandler(handler: (() => Promise<boolean>) | null): void {
  stepUpHandler = handler
}

function pathOf(url: string): string {
  try {
    return new URL(url, 'http://localhost').pathname
  } catch {
    return url
  }
}

async function readCode(res: Response): Promise<string | undefined> {
  try {
    const body: unknown = await res.clone().json()
    if (typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string') return body.code
  } catch {
    // cuerpo vacío o no JSON
  }
  return undefined
}

export interface ApiClientOptions {
  baseUrl: string
  /** fetch subyacente; por defecto el global en el momento de la llamada (las pruebas lo sustituyen). */
  fetch?: (request: Request) => Promise<Response>
}

/** Crea un cliente tipado con la política de autenticación. La app usa la instancia `api`; las pruebas crean otras. */
export function createApiClient(options: ApiClientOptions) {
  const baseFetch = options.fetch ?? ((r: Request) => globalThis.fetch(r))
  // Cliente sin política (solo para el refresh, que no debe re-entrar en la lógica de 401).
  const bare = createClient<paths>({ baseUrl: options.baseUrl, fetch: baseFetch })

  let refreshing: Promise<boolean> | null = null

  async function doRefresh(): Promise<boolean> {
    const refreshToken = getRefreshToken()
    if (!refreshToken) return false
    try {
      const { data } = await bare.POST('/api/v1/auth/refresh', {
        body: { refreshToken },
        headers: { 'Accept-Language': getLang() },
      })
      if (!data?.accessToken || !data.refreshToken) return false
      setTokens({
        accessToken: data.accessToken,
        accessExpiresAtUtc: data.accessExpiresAtUtc,
        refreshToken: data.refreshToken, // rotación: el anterior queda revocado en el servidor
        refreshExpiresAtUtc: data.refreshExpiresAtUtc,
        tenantId: data.tenantId ?? getTokens()?.tenantId ?? 0,
      })
      return true
    } catch {
      return false
    }
  }

  /** Un solo refresh en vuelo: las peticiones que fallen a la vez esperan el mismo. */
  function refreshOnce(): Promise<boolean> {
    refreshing ??= doRefresh().finally(() => {
      refreshing = null
    })
    return refreshing
  }

  let steppingUp: Promise<boolean> | null = null
  function stepUpOnce(handler: () => Promise<boolean>): Promise<boolean> {
    steppingUp ??= handler()
      .catch(() => false)
      .finally(() => {
        steppingUp = null
      })
    return steppingUp
  }

  function prepare(template: Request): { request: Request; token: string | null; explicitAuth: boolean } {
    const request = template.clone()
    const explicitAuth = request.headers.has('Authorization')
    const token = explicitAuth ? null : getAccessToken()
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    if (!request.headers.has('Accept-Language')) request.headers.set('Accept-Language', getLang())
    return { request, token, explicitAuth }
  }

  async function authFetch(template: Request): Promise<Response> {
    const first = prepare(template)
    let response = await baseFetch(first.request)

    if (response.status === 401 && !first.explicitAuth && !NO_REFRESH.includes(pathOf(template.url)) && getRefreshToken()) {
      const current = getAccessToken()
      // Si otra petición ya rotó el token mientras esta viajaba, basta con reintentar.
      const renewed = current !== null && current !== first.token ? true : await refreshOnce()
      if (!renewed) {
        authLostHandler?.()
        return response
      }
      response = await baseFetch(prepare(template).request)
    }

    if (response.status === 403 && stepUpHandler && (await readCode(response)) === 'aal2_required') {
      if (await stepUpOnce(stepUpHandler)) response = await baseFetch(prepare(template).request)
    }
    return response
  }

  return createClient<paths>({ baseUrl: options.baseUrl, fetch: authFetch })
}

/** Cliente del API de la aplicación. `api.GET('/api/v1/clients', { params: { query } })`. */
export const api = createApiClient({ baseUrl: API_BASE_URL })

type ApiResult<D> = { data: D; error?: never; response: Response } | { data?: never; error: unknown; response: Response }

/**
 * Espera la llamada y devuelve `data`, o lanza ApiError con el ProblemDetails. Pensado para queryFn/mutationFn:
 * `queryFn: () => unwrap(api.GET('/api/v1/me'))`.
 */
export async function unwrap<D>(call: Promise<ApiResult<D>>): Promise<D> {
  const result = await call
  if (result.error !== undefined || !result.response.ok) throw toApiError(result.error, result.response)
  return result.data as D
}
