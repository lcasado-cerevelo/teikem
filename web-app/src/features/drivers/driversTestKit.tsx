// Utilería de las pruebas de Choferes (F-D1): lo del API simulado (`driversMock`) más `wrap()`, que monta la pantalla con permisos.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { AccessProvider } from '../../kernel/access'
import DriverListScreen from './DriverListScreen'

export * from './driversMock'

export const FLEET = ['fleet.view', 'fleet.manage']
export const PAY = ['driverpay.view', 'driverpay.manage']
export const ALL = [...FLEET, ...PAY, 'admin.users', 'contacts.manage']

export function wrap(permissions: string[], entry = '/catalog/drivers') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['CATALOG', 'CUSTOM_FIELDS']}>
          <DriverListScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
