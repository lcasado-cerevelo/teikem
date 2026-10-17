// ProblemDetails del API ({ type, title, status, code, errors, correlationId }) interpretado: lo lanza `unwrap()`. code === 'network' → sin conexión.
import { t } from '../i18n/i18n'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  code?: string
  errors?: Record<string, string[]> | null
  correlationId?: string
}

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
    this.code = problem?.code || codeFromStatus(status)
    this.correlationId = problem?.correlationId
  }
}

function fallbackTitle(status: number): string {
  if (status === 0) return t('errors.network')
  if (status === 403) return t('errors.forbidden')
  return t('errors.generic')
}

function codeFromStatus(status: number): string {
  if (status === 0) return 'network'
  if (status === 401) return 'unauthorized'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not_found'
  if (status === 409) return 'conflict'
  if (status === 429) return 'rate_limited'
  if (status >= 500) return 'internal'
  return 'error'
}

function isProblem(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'code' in value || 'errors' in value)
}

/** Convierte el `error` de openapi-fetch en un ApiError. */
export function toApiError(error: unknown, response?: Response): ApiError {
  if (error instanceof ApiError) return error
  const status = response?.status ?? (isProblem(error) && typeof error.status === 'number' ? error.status : 0)
  if (isProblem(error)) return new ApiError(status, error)
  if (typeof error === 'string' && error.trim()) return new ApiError(status, { title: error })
  return new ApiError(status, null)
}

/** Texto para mostrar: los mensajes por campo (sin repetir) o el título (el mensaje exacto del servidor). */
export function apiErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return t('errors.generic')
  const messages = [...new Set(Object.values(error.errors ?? {}).flat().filter((m) => typeof m === 'string' && m.trim()))]
  return messages.length > 0 ? messages.join(' ') : error.title
}

export function isNetworkError(error: unknown): boolean {
  return error instanceof ApiError && error.code === 'network'
}
