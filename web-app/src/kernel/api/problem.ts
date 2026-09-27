// ProblemDetails del API (ExceptionHandlingMiddleware): { type, title, status, code, errors, correlationId }.
// Las validaciones de modelo de ASP.NET llegan sin `code` y con `errors` por campo.
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { t } from '../i18n/i18n'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  code?: string
  errors?: Record<string, string[]> | null
  correlationId?: string
}

/** Error del API con el ProblemDetails ya interpretado. Lo lanza `unwrap()`. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly title: string
  readonly errors: Record<string, string[]>
  readonly correlationId?: string

  constructor(status: number, problem: ProblemDetails | null) {
    const title = problem?.title || t('errors.generic')
    super(title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.errors = problem?.errors ?? {}
    this.code = problem?.code || codeFromStatus(status, this.errors)
    this.correlationId = problem?.correlationId
  }
}

function codeFromStatus(status: number, errors: Record<string, string[]>): string {
  if (status === 400) return Object.keys(errors).length > 0 ? 'validation' : 'bad_request'
  if (status === 401) return 'unauthorized'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not_found'
  if (status === 409) return 'conflict'
  if (status === 422) return 'status_rule'
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

/** Nombre de campo del servidor → nombre del formulario: quita "$." y pasa la primera letra a minúscula. */
export function normalizeField(field: string): string {
  const f = field.startsWith('$.') ? field.slice(2) : field
  return f ? f.charAt(0).toLowerCase() + f.slice(1) : f
}

export interface AppliedProblem {
  /** Mensaje general para el toast. */
  title: string
  /** Código del API (`validation`, `conflict`, `status_rule`, `aal2_required`, `network`…). */
  code: string
  /** Errores por campo (nombres ya normalizados a camelCase). */
  errors: Record<string, string[]>
}

/**
 * Interpreta cualquier error de una llamada al API. Con `form` (react-hook-form) pone cada error de campo con
 * `setError` para que se muestre bajo el campo. Devuelve { title, code, errors } para el toast.
 */
export function applyProblemDetails<T extends FieldValues>(
  error: unknown,
  form?: { setError: UseFormSetError<T> },
): AppliedProblem {
  let applied: AppliedProblem
  if (error instanceof ApiError) {
    applied = { title: error.title, code: error.code, errors: error.errors }
  } else if (isProblem(error)) {
    const e = toApiError(error)
    applied = { title: e.title, code: e.code, errors: e.errors }
  } else if (error instanceof TypeError) {
    // fetch lanza TypeError cuando no hay red o el servidor no responde
    applied = { title: t('errors.network'), code: 'network', errors: {} }
  } else {
    applied = { title: t('errors.generic'), code: 'error', errors: {} }
  }
  const errors: Record<string, string[]> = {}
  for (const [field, messages] of Object.entries(applied.errors)) {
    const name = normalizeField(field)
    errors[name] = messages
    if (form && messages.length > 0) form.setError(name as Path<T>, { type: 'server', message: messages.join(' ') })
  }
  return { ...applied, errors }
}
