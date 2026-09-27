// Sesión de la aplicación: contexto y hook. El proveedor está en SessionProvider.tsx.
import { createContext, useContext } from 'react'
import type { components } from '../kernel/api/schema'
import type { Lang } from '../kernel/i18n/i18n'

export type MeDto = components['schemas']['MeDto']

export interface Session {
  /** Sesión actual (`GET /api/v1/me`): usuario, tenant activo, membresías, permisos, módulos, MFA. */
  me: MeDto | undefined
  /** Hay tokens (la sesión puede estar cargando `me`). */
  isAuthenticated: boolean
  /** Hay tokens pero `me` aún no llega. */
  isLoading: boolean
  /** Error al cargar `me` (distinto de 401, que devuelve al login). */
  error: unknown
  tenantId: number | null
  lang: Lang
  /** Cambia el idioma sin reiniciar la pantalla ni el menú: diccionario + Accept-Language + invalidar consultas. */
  setLang: (lang: Lang) => void
  logout: () => Promise<void>
  /** Cambia de compañía: nuevos tokens, se descarta la caché y se vuelve al inicio. */
  switchTenant: (tenantId: number) => Promise<void>
  permissions: ReadonlySet<string>
  modules: ReadonlySet<string>
  /** Vuelve a pedir `me` (p. ej. tras activar MFA). */
  reloadMe: () => Promise<void>
}

export const ME_QUERY_KEY = ['/api/v1/me'] as const

export const SessionContext = createContext<Session | null>(null)

/** `const { me, lang, setLang, logout, switchTenant } = useSession()` */
export function useSession(): Session {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession requiere <SessionProvider>.')
  return ctx
}
