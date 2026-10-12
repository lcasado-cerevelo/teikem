// Lote F-C1 (ajuste) — el aviso «Orden … guardada» de la última alta se quita al abrir otra vez «Nueva orden»: no debe quedar
// pegado a un formulario nuevo. El formulario se simula (solo avisa que creó una orden); lo demás va sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import OrderListScreen from './OrderListScreen'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const path = new URL(req.url).pathname
    const body = path === '/api/v1/orders' ? { total: 0, items: [] } : []
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

vi.mock('./OrderEntryForm', () => ({
  OrderEntryForm: ({ onCreated }: { onCreated: (order: { publicId: string; orderNumber: string }, confirmed: boolean) => void }) => (
    <button type="button" onClick={() => onCreated({ publicId: 'o-9', orderNumber: 'ORD-00009' }, false)}>
      simular alta
    </button>
  ),
}))

beforeAll(() => setLang('es'))

describe('Órdenes — aviso de la última alta', () => {
  it('se muestra tras crear y desaparece al abrir «Nueva orden» otra vez', async () => {
    const user = userEvent.setup()
    window.localStorage.setItem('teikem.orders.entryMode', 'detail') // el aviso es del alta Detallada; la Rápida es la predeterminada (F-C3)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <MemoryRouter initialEntries={['/orders']}>
        <QueryClientProvider client={queryClient}>
          <AccessProvider permissions={['orders.view', 'orders.create']} modules={['LTL_GROUND', 'CATALOG']}>
            <Routes>
              <Route path="/orders" element={<OrderListScreen />} />
            </Routes>
          </AccessProvider>
        </QueryClientProvider>
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole('button', { name: /Nueva orden/ }))
    await user.click(screen.getByRole('button', { name: 'simular alta' }))
    expect(await screen.findByText(/ORD-00009/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Nueva orden/ }))
    expect(screen.queryByText(/ORD-00009/)).toBeNull()
  })
})
