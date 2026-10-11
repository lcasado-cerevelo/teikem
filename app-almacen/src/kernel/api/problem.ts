// ProblemDetails del API (ExceptionHandlingMiddleware): { type, title, status, code, errors, correlationId }.
// Mismo contrato que web-app/src/kernel/api/problem.ts, sin la integración con react-hook-form (la app no usa formularios
// de varios campos; cada pantalla captura un valor a la vez).
import { t } from '../i18n/i18n'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  code?: string
  errors?: Record<string, string[]> | null
  correlationId?: string
}

/** Error del API con el ProblemDetails ya interpretado. Lo lanza `unwrap()`. code === 'network' → sin conexión. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly title: string
  readonly errors: Record<string, string[]>
  readonly correlationId?: string

  constructor(status: number, problem: ProblemDetails | null) {
    const title = problem?.title || fallbackTitle(status)
    super(title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.errors = problem?.errors ?? {}
    this.code = problem?.code || codeFromStatus(status, this.errors)
    this.correlationId = problem?.correlationId
  }
}

/** Texto cuando el servidor no mandó título. 2026-10-11: las políticas de permiso del API ([RequirePermission]) responden
 *  403 SIN cuerpo; antes se leía «Ocurrió un error. Intente de nuevo.», que invita a reintentar algo que no va a pasar.
 *  Un 403 con título (ProblemDetails: módulo apagado, regla del servicio…) conserva el mensaje del servidor. */
function fallbackTitle(status: number): string {
  if (status === 403) return t('errors.forbidden')
  return t('errors.generic')
}

function codeFromStatus(status: number, errors: Record<string, string[]>): string {
  if (status === 0) return 'network'
  if (status === 400) return Object.keys(errors).length > 0 ? 'validation' : 'bad_request'
  if (status === 401) return 'unauthorized'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not_found'
  if (status === 409) return 'conflict'
  if (status === 422) return 'status_rule'
  if (status === 429) return 'rate_limited'
  if (status >= 500) return 'internal'
  return 'error'
}

function isProblem(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'code' in value || 'errors' in value)
}

/** Convierte lo que devuelve openapi-fetch en `error` (cuerpo ya parseado) en un ApiError. */
export function toApiError(error: unknown, response?: Response): ApiError {
  if (error instanceof ApiError) return error
  const status = response?.status ?? (isProblem(error) && typeof error.status === 'number' ? error.status : 0)
  if (isProblem(error)) return new ApiError(status, error)
  if (typeof error === 'string' && error.trim()) return new ApiError(status, { title: error })
  return new ApiError(status, null)
}

/** Texto para mostrar de un ApiError: los mensajes por campo (`errors`, sin repetir) si los hay —el título de una
 *  validación con varios campos es solo "Datos inválidos."—; si no, el título (el mensaje exacto del servidor). */
export function apiErrorMessage(error: ApiError): string {
  const messages = [...new Set(Object.values(error.errors ?? {}).flat().filter((m) => typeof m === 'string' && m.trim()))]
  return messages.length > 0 ? messages.join(' ') : error.title
}

/** true si el error es de conexión (sin red o el servidor no respondió): la operación debe encolarse, no perderse. */
export function isNetworkError(error: unknown): boolean {
  return error instanceof ApiError && error.code === 'network'
}
