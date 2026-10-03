// Cliente único del API: openapi-fetch sobre los tipos generados (schema.d.ts). Mismo patrón que web-app/src/kernel/
// api/client.ts, adaptado a la sesión de un aparato registrado (device auth, Lote 8A): el access token viaja con el
// claim `did` y el refresh revalida también al aparato y al usuario (docs/lote8A-decisiones.md). Sin MFA ni
// reautenticación aal2 en el aparato (decisión ratificada, docs/mobile/app-almacen-plan.md §7.1).
// - La URL del API es configurable en tiempo de ejecución (Sincronización → "Servidor"), así que el cliente se crea con
//   un origen de relleno (createClient necesita una URL absoluta) y cada petición se reapunta al origen real justo
//   antes de salir; así el usuario nunca tiene que recompilar la app para apuntar a otro servidor.
// - Authorization: Bearer <access token> del usuario que entró con su PIN, salvo NO_REFRESH (login/enroll/refresh).
// - 401 → refresca UNA vez (una sola petición de refresh aunque fallen varias llamadas a la vez) y reintenta; si el
//   refresh falla, limpia la sesión de usuario y avisa a la raíz (vuelve a "¿Quién eres?").
// - Sin red (fetch lanza) → ApiError con code 'network' (kernel/api/problem.ts, isNetworkError), para que el motor de
//   sincronización encole la operación en vez de descartarla.
import createClient from 'openapi-fetch'

import { clearUserSession, getSessionState, saveUserSession } from '../auth/session'
import { getKv, KvKeys, setKv } from '../db/kv'
import { getLang } from '../i18n/i18n'
import { toApiError } from './problem'
import type { paths } from './schema'

export { ApiError, apiErrorMessage, isNetworkError, toApiError } from './problem'
export type { ProblemDetails } from './problem'

const PLACEHOLDER_ORIGIN = 'http://device.teikem.local'

const NO_REFRESH = ['/api/v1/auth/device/users', '/api/v1/auth/device/login', '/api/v1/auth/refresh', '/api/v1/devices/enroll']

export function getApiBaseUrl(): string {
  return getKv(KvKeys.apiBaseUrl) ?? ''
}

export function setApiBaseUrl(url: string): void {
  setKv(KvKeys.apiBaseUrl, url.trim().replace(/\/+$/, ''))
}

let authLostHandler: (() => void) | null = null
/** RootGate registra qué hacer cuando la sesión no se puede recuperar (vuelve a "¿Quién eres?"). */
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

/** Reconstruye la petición apuntando al origen real. `new Request(url, request)` pierde el cuerpo bajo el `fetch` de
 *  React Native (el servidor lo recibe vacío); copiarlo explícito como bytes lo preserva en cualquier plataforma. */
async function retarget(request: Request): Promise<Request> {
  const u = new URL(request.url)
  const hasBody = request.method !== 'GET' && request.method !== 'HEAD'
  const body = hasBody ? await request.clone().arrayBuffer() : undefined
  return new Request(`${getApiBaseUrl()}${u.pathname}${u.search}`, { method: request.method, headers: request.headers, body })
}

export interface ApiClientOptions {
  /** fetch subyacente; por defecto el global. Las pruebas lo sustituyen por un doble. */
  fetch?: (request: Request) => Promise<Response>
}

/** Crea un cliente con la política de reintento. La app usa `api`; las pruebas crean otras instancias. */
export function createApiClient(options: ApiClientOptions = {}) {
  const baseFetch = options.fetch ?? ((r: Request) => globalThis.fetch(r))

  async function safeFetch(request: Request): Promise<Response> {
    try {
      return await baseFetch(await retarget(request))
    } catch {
      // Response.error(): la única forma estándar de construir una Response con status 0 (falla de red real, no un
      // 4xx/5xx del servidor); `new Response(null, { status: 0 })` no es válido (RangeError, fuera de [200, 599]).
      return Response.error()
    }
  }

  // Cliente sin política (solo para el refresh, que no debe re-entrar en la lógica de 401).
  const bare = createClient<paths>({ baseUrl: PLACEHOLDER_ORIGIN, fetch: safeFetch })

  let refreshing: Promise<boolean> | null = null

  async function doRefresh(): Promise<boolean> {
    const session = getSessionState().session
    if (!session) return false
    const { data, response } = await bare.POST('/api/v1/auth/refresh', {
      body: { refreshToken: session.refreshToken },
      headers: { 'Accept-Language': getLang() },
    })
    if (!response.ok || !data?.accessToken || !data.refreshToken) return false
    await saveUserSession({
      ...session,
      accessToken: data.accessToken,
      accessExpiresAtUtc: data.accessExpiresAtUtc ?? session.accessExpiresAtUtc,
      refreshToken: data.refreshToken,
      refreshExpiresAtUtc: data.refreshExpiresAtUtc ?? session.refreshExpiresAtUtc,
      tenantId: data.tenantId ?? session.tenantId,
    })
    return true
  }

  /** Un solo refresh en vuelo: las peticiones que fallen a la vez esperan el mismo. */
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

    const path = pathOf(template.url)
    if (response.status === 401 && !first.explicitAuth && !NO_REFRESH.includes(path) && getSessionState().session) {
      const renewed = await refreshOnce()
      if (!renewed) {
        await clearUserSession()
        authLostHandler?.()
        return response
      }
      response = await safeFetch(prepare(template).request)
    }
    return response
  }

  return createClient<paths>({ baseUrl: PLACEHOLDER_ORIGIN, fetch: authFetch })
}

/** Cliente del API de la aplicación. `api.GET('/api/v1/warehouses')`. */
export const api = createApiClient()

/**
 * Espera la llamada y devuelve `data` (nunca undefined), o lanza ApiError con el ProblemDetails (o code 'network' sin
 * respuesta). `T` se infiere del objeto completo que devuelve openapi-fetch (la unión data/error tal cual la tipa la
 * librería); sacar `data` con un tipo indexado y `NonNullable` evita depender de que TypeScript unifique esa unión
 * contra un tipo espejo declarado aparte, que en algunas combinaciones de versión no infiere D sin `| undefined`.
 */
export async function unwrap<T extends { response: Response; data?: unknown }>(call: Promise<T>): Promise<NonNullable<T['data']>> {
  const result = (await call) as T & { data?: unknown; error?: unknown }
  if (result.response.status === 0) throw toApiError(null)
  if (result.error !== undefined || !result.response.ok) throw toApiError(result.error, result.response)
  return result.data as NonNullable<T['data']>
}
