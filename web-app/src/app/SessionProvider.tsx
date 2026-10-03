// Sesión de la aplicación: tokens → GET /api/v1/me → permisos y módulos (AccessProvider), idioma y tenant.
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { AccessProvider } from '../kernel/access/AccessProvider'
import { FormatProvider } from '../kernel/format/FormatProvider'
import { api, setAuthLostHandler, unwrap } from '../kernel/api/client'
import { logout as authLogout, switchTenantTokens } from '../kernel/auth/auth'
import { clearTokens, getTokens, subscribeTokens } from '../kernel/auth/tokens'
import { setLang as setI18nLang, type Lang } from '../kernel/i18n/i18n'
import { useLang } from '../kernel/i18n/useT'
import { setAccessDeniedHandler } from './queryClient'
import { ME_QUERY_KEY, SessionContext, type Session } from './session'

const NO_STRINGS: readonly string[] = []

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const tokens = useSyncExternalStore(subscribeTokens, getTokens, getTokens)
  const lang = useLang()
  const isAuthenticated = tokens !== null

  const meQuery = useQuery({
    queryKey: ME_QUERY_KEY,
    queryFn: () => unwrap(api.GET('/api/v1/me')),
    enabled: isAuthenticated,
    staleTime: 5 * 60_000,
    meta: { handleAccessDenied: false },
  })

  // Sesión irrecuperable (refresh rechazado): limpiar y volver al login.
  useEffect(() => {
    setAuthLostHandler(() => {
      clearTokens()
      queryClient.clear()
      navigate('/login', { replace: true })
    })
    setAccessDeniedHandler((code) => navigate(code === 'module_disabled' ? '/module-off' : '/forbidden'))
    return () => {
      setAuthLostHandler(null)
      setAccessDeniedHandler(null)
    }
  }, [navigate, queryClient])

  useEffect(() => {
    if (typeof document !== 'undefined') document.documentElement.lang = lang
  }, [lang])

  const setLang = useCallback(
    (next: Lang) => {
      setI18nLang(next)
      // Las etiquetas de catálogo vienen traducidas del API: se vuelven a pedir sin desmontar nada.
      void queryClient.invalidateQueries()
    },
    [queryClient],
  )

  const logout = useCallback(async () => {
    await authLogout()
    queryClient.clear()
    navigate('/login', { replace: true })
  }, [navigate, queryClient])

  const switchTenant = useCallback(
    async (tenantId: number) => {
      await switchTenantTokens(tenantId)
      // Contexto completo nuevo: menú, datos y permisos del otro tenant.
      queryClient.clear()
      navigate('/', { replace: true })
    },
    [navigate, queryClient],
  )

  const reloadMe = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ME_QUERY_KEY })
  }, [queryClient])

  const me = isAuthenticated ? meQuery.data : undefined
  const permissionList = me?.permissions ?? NO_STRINGS
  const moduleList = me?.enabledModules ?? NO_STRINGS
  const permissions = useMemo(() => new Set(permissionList), [permissionList])
  const modules = useMemo(() => new Set(moduleList), [moduleList])

  const value = useMemo<Session>(
    () => ({
      me,
      isAuthenticated,
      isLoading: isAuthenticated && me === undefined && meQuery.isPending,
      error: meQuery.error,
      tenantId: me?.tenantId ?? tokens?.tenantId ?? null,
      lang,
      setLang,
      logout,
      switchTenant,
      permissions,
      modules,
      reloadMe,
    }),
    [me, isAuthenticated, meQuery.isPending, meQuery.error, tokens, lang, setLang, logout, switchTenant, permissions, modules, reloadMe],
  )

  return (
    <SessionContext.Provider value={value}>
      <AccessProvider permissions={permissionList} modules={moduleList}>
        {/* Región y formatos de la compañía: un solo proveedor para fechas, horas, números, dinero y teléfonos */}
        <FormatProvider enabled={isAuthenticated}>{children}</FormatProvider>
      </AccessProvider>
    </SessionContext.Provider>
  )
}
