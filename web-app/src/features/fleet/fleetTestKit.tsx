// Utilería de las pruebas de Flota (F-D2): lo del API simulado (`fleetMock`) más `wrap()`, que monta la pantalla con permisos y una ruta
// de Choferes de mentira (para ver a dónde navega «Documentos por vencer» con un chofer).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { AccessProvider } from '../../kernel/access'
import FleetScreen from './FleetScreen'

export * from './fleetMock'

export const VIEW = ['fleet.view']
export const MANAGE = ['fleet.view', 'fleet.manage']
export const ALL = [...MANAGE, 'contacts.manage']

function DriversProbe() {
  const loc = useLocation()
  return <div data-testid="drivers-screen">{`${loc.pathname}${loc.search}`}</div>
}

export function wrap(permissions: string[], entry = '/catalog/fleet') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['CATALOG', 'CUSTOM_FIELDS']}>
          <Routes>
            <Route path="/catalog/fleet" element={<FleetScreen />} />
            <Route path="/catalog/drivers" element={<DriversProbe />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
