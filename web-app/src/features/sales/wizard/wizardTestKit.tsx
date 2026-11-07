// Lote 223 (P3) — SOLO PARA PRUEBAS: monta una pieza del pedido guiado con sesión, permisos, consultas y enrutador (no lo importa ninguna pantalla).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { SessionContext, type Session } from '../../../app/session'
import { AccessProvider } from '../../../kernel/access'
import { SELLER } from './wizardTestApi'
import { emptyWizard, type WizardState } from './wizardRules'

function session(permissions: string[], mfaEnabled = false): Session {
  return {
    me: { userId: 99, tenantName: 'Solutions', mfaEnabled },
    isAuthenticated: true,
    isLoading: false,
    error: null,
    tenantId: 7,
    lang: 'es',
    setLang: () => {},
    logout: async () => {},
    switchTenant: async () => {},
    permissions: new Set(permissions),
    modules: new Set(['SALES_ORDERS', 'CATALOG']),
    reloadMe: async () => {},
  } as unknown as Session
}

/** Sesión, permisos, consultas (sin reintentos) y enrutador: `render(<WizardProviders>{ui}</WizardProviders>)`. */
export function WizardProviders({ children, permissions, mfa, route }: { children: ReactNode; permissions?: string[]; mfa?: boolean; route?: string }) {
  const [s] = useState(() => session(permissions ?? SELLER, mfa))
  const [client] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }))
  return (
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={s}>
        <AccessProvider permissions={s.permissions} modules={s.modules}>
          <MemoryRouter initialEntries={[route ?? '/sales-orders/new-order']}>{children}</MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>
  )
}

/** Guarda el estado del asistente para probar un paso suelto; `onState` ve cada cambio. */
export function StepHarness({ initial, onState, children }: { initial?: Partial<WizardState>; onState?: (s: WizardState) => void; children: (s: WizardState, change: (next: (s: WizardState) => WizardState) => void) => ReactNode }) {
  const [state, setState] = useState<WizardState>(() => ({ ...emptyWizard(), ...initial }))
  const change = (next: (s: WizardState) => WizardState) =>
    setState((s) => {
      const n = next(s)
      onState?.(n)
      return n
    })
  return <>{children(state, change)}</>
}
