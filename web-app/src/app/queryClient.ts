// QueryClient de la app. Las lecturas que fallan con 403 module_disabled / forbidden llevan a la pantalla propia
// ('Módulo apagado' / 'Sin permiso'), salvo que la consulta declare `meta: { handleAccessDenied: false }`.
// Las escrituras (mutaciones) no se interceptan: la pantalla muestra el título con applyProblemDetails.
import { QueryCache, QueryClient } from '@tanstack/react-query'
import { ApiError } from '../kernel/api/problem'

export type AccessDeniedCode = 'module_disabled' | 'forbidden'

let accessDeniedHandler: ((code: AccessDeniedCode) => void) | null = null

/** El shell registra la navegación a /module-off o /forbidden. */
export function setAccessDeniedHandler(handler: ((code: AccessDeniedCode) => void) | null): void {
  accessDeniedHandler = handler
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (query.meta?.handleAccessDenied === false) return
        if (error instanceof ApiError && (error.code === 'module_disabled' || error.code === 'forbidden')) {
          accessDeniedHandler?.(error.code)
        }
      },
    }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        // Los 4xx no se reintentan (no van a cambiar); errores de red o 5xx, hasta 2 veces.
        retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
      },
      mutations: { retry: false },
    },
  })
}

export const queryClient = createQueryClient()
