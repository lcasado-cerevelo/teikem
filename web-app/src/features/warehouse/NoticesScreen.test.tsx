// «Llegadas y salidas»: el botón «juntos» sigue a la pestaña (llegadas: Recibir juntos; salidas: Recolectar juntos) y solo se habilita con dos o más documentos que juntar en ESA pestaña
// (avisos + órdenes de compra en llegadas; avisos de salida + órdenes de venta en salidas). Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import NoticesScreen from './NoticesScreen'

const mock = vi.hoisted(() => ({ asns: 0, pos: 0, docs: 0 }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const p = new URL(req.url).pathname
    let out: unknown = []
    if (p === '/api/v1/asns') out = Array.from({ length: mock.asns }, (_, i) => ({ id: i + 1, number: `LLE-${i + 1}`, warehousePublicId: 'w1', statusCode: 'EXPECTED', isActive: true, lines: [] }))
    else if (p === '/api/v1/purchase-orders') out = { total: mock.pos, skip: 0, take: 50, items: Array.from({ length: mock.pos }, (_, i) => ({ publicId: `po${i}`, number: `PO-${i}`, statusCode: 'SENT', warehousePublicId: 'w1' })) }
    else if (p === '/api/v1/pick-batches/documents') out = { total: mock.docs, skip: 0, take: 200, items: Array.from({ length: mock.docs }, (_, i) => ({ kind: i % 2 ? 'SALES_ORDER' : 'EXIT_NOTICE', publicId: `d${i}`, number: `D-${i}`, reserved: 3, clientName: 'ASICS' })) }
    else if (p === '/api/v1/exit-notices') out = { total: 0, skip: 0, take: 25, items: [] }
    return new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.asns = 0
  mock.pos = 0
  mock.docs = 0
})

function wrap(url = '/warehouse/notices') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view', 'warehouse.receive', 'warehouse.pick']} modules={['WMS_LOTSERIAL']}>
          <NoticesScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('Llegadas y salidas — botón «juntos»', () => {
  it('en llegadas: deshabilitado con menos de dos documentos; con un aviso y una orden de compra (suman) se habilita', async () => {
    mock.asns = 1
    mock.pos = 0
    wrap()
    const btn = await screen.findByRole('button', { name: 'Recibir juntos' })
    await waitFor(() => expect(btn).toBeDisabled())
  })

  it('en llegadas: un aviso + una orden de compra = dos y se habilita', async () => {
    mock.asns = 1
    mock.pos = 1
    wrap()
    const btn = await screen.findByRole('button', { name: 'Recibir juntos' })
    await waitFor(() => expect(btn).toBeEnabled())
  })

  it('en salidas: el botón cambia a «Recolectar juntos»; sin dos documentos (avisos de salida u órdenes de venta) está deshabilitado, con dos se habilita', async () => {
    mock.docs = 1
    wrap('/warehouse/notices?tab=exit-notices')
    const btn = await screen.findByRole('button', { name: 'Recolectar juntos' })
    expect(screen.queryByRole('button', { name: 'Recibir juntos' })).toBeNull()
    await waitFor(() => expect(btn).toBeDisabled())
  })

  it('en salidas con un aviso de salida y una orden de venta se habilita', async () => {
    mock.docs = 2
    wrap('/warehouse/notices?tab=exit-notices')
    const btn = await screen.findByRole('button', { name: 'Recolectar juntos' })
    await waitFor(() => expect(btn).toBeEnabled())
  })
})
