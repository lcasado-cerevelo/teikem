// Pruebas de los ríos del Pulso (Lote 88) sobre un fetch simulado: las tarjetas con su número y su texto, el enlace de cada una, las barritas y el error.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { CodRiver, OrdersRiver } from './RiversPanel'

const mock = vi.hoisted(() => ({ fail: false }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const p = new URL(req.url).pathname
    if (mock.fail) return new Response(JSON.stringify({ title: 'Error interno.', status: 500 }), { status: 500, headers: { 'Content-Type': 'application/problem+json' } })
    if (p === '/api/v1/pulse/rivers/orders') {
      return json({
        river: 'ORDERS', day: '2026-10-17',
        nodes: [
          { key: 'ENTRY', value: 14, kind: 'count', details: [{ key: 'waiting', value: 6, kind: 'count' }], spark: [1, 2, 3, 4, 5, 6, 14] },
          { key: 'IN_WAREHOUSE', value: 9, kind: 'count', details: [], spark: null },
          { key: 'TO_DISPATCH', value: 3, kind: 'count', details: [], spark: null },
          { key: 'ON_ROUTE', value: 22, kind: 'count', details: [{ key: 'drivers', value: 4, kind: 'count' }], spark: null },
          { key: 'DELIVERED', value: 31, kind: 'count', details: [], spark: [0, 0, 0, 0, 0, 5, 31] },
        ],
      })
    }
    if (p === '/api/v1/pulse/rivers/cod') {
      return json({
        river: 'COD', day: '2026-10-17',
        nodes: [
          { key: 'TO_COLLECT', value: 263, kind: 'count', details: [{ key: 'amount', value: 5320, kind: 'money' }], spark: null },
          { key: 'COLLECTED', value: 4210, kind: 'money', details: [{ key: 'cash', value: 2840, kind: 'money' }, { key: 'checks', value: 1370, kind: 'money' }, { key: 'other', value: 0, kind: 'money' }], spark: null },
          { key: 'RECONCILED', value: 900, kind: 'money', details: [{ key: 'clients', value: 3, kind: 'count' }], spark: null },
          { key: 'REMITTANCE', value: 1200, kind: 'money', details: [{ key: 'remittances', value: 2, kind: 'count' }, { key: 'clients', value: 2, kind: 'count' }], spark: null },
        ],
      })
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(ui: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ríos del Pulso', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.fail = false
  })

  it('«Paquetes en la calle»: las cinco tarjetas con su número, su texto y su enlace', async () => {
    wrap(<OrdersRiver />)
    const entry = await screen.findByText('Entrada')
    const card = (key: string) => document.querySelector(`[data-node="${key}"]`) as HTMLElement
    expect(entry).toBeInTheDocument()
    expect(within(card('ENTRY')).getByText('14')).toBeInTheDocument()
    expect(within(card('ENTRY')).getByText('envíos nuevos hoy · 6 esperan')).toBeInTheDocument()
    expect(within(card('IN_WAREHOUSE')).getByText('9')).toBeInTheDocument()
    expect(within(card('TO_DISPATCH')).getByText('sin chofer')).toBeInTheDocument()
    expect(within(card('ON_ROUTE')).getByText('4 choferes')).toBeInTheDocument()
    expect(within(card('DELIVERED')).getByText('31')).toBeInTheDocument()
    expect(card('TO_DISPATCH').querySelector('a')).toHaveAttribute('href', '/ops/dispatch')
    expect(card('ON_ROUTE').querySelector('a')).toHaveAttribute('href', '/ops/monitor')
    // Entrada y Entregado llevan barritas de 7 días; las demás no
    expect(card('ENTRY').querySelector('.wh-mini')).not.toBeNull()
    expect(card('DELIVERED').querySelector('.wh-mini')).not.toBeNull()
    expect(card('IN_WAREHOUSE').querySelector('.wh-mini')).toBeNull()
  })

  it('«Dinero COD»: montos en dinero, el desglose por método y a dónde lleva cada tarjeta', async () => {
    wrap(<CodRiver />)
    await screen.findByText('Por cobrar')
    const card = (key: string) => document.querySelector(`[data-node="${key}"]`) as HTMLElement
    expect(within(card('TO_COLLECT')).getByText('263')).toBeInTheDocument()
    expect(within(card('TO_COLLECT')).getByText(/faltan \$5,320\.00/)).toBeInTheDocument()
    expect(within(card('COLLECTED')).getByText('$4,210.00')).toBeInTheDocument()
    expect(within(card('COLLECTED')).getByText(/efectivo \$2,840\.00 · cheques \$1,370\.00/)).toBeInTheDocument()
    expect(within(card('RECONCILED')).getByText(/3 clientes/)).toBeInTheDocument()
    expect(within(card('REMITTANCE')).getByText(/2 remesas · 2 clientes/)).toBeInTheDocument()
    expect(card('COLLECTED').querySelector('a')).toHaveAttribute('href', '/money/cod?tab=reconcile')
    expect(card('REMITTANCE').querySelector('a')).toHaveAttribute('href', '/money/cod?tab=remittances')
    expect(document.querySelector('.pipe.money')).not.toBeNull()   // la tubería del dinero es naranja
  })

  it('si el servidor falla, la sección dice el error en lugar de las tarjetas', async () => {
    mock.fail = true
    wrap(<CodRiver />)
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(document.querySelector('[data-node]')).toBeNull()
  })
})
