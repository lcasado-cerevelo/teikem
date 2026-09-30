// Lote 14 — detalle de solo lectura de un movimiento: datos del movimiento, documento de origen con "Abrir" protegido por
// permiso y módulo (la tarea abre su documento padre), movimientos relacionados y el 404 del API tal cual.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { KardexTransactionModal } from './KardexTransactionModal'

const mock = vi.hoisted(() => ({ detail: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    if (url.pathname === '/api/v1/inventory/transactions/5') {
      return mock.detail instanceof Response
        ? mock.detail
        : new Response(JSON.stringify(mock.detail), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }
    return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const row = {
  id: 5,
  createdAtUtc: '2026-09-30T14:05:00',
  typeCode: 'ADJUSTMENT',
  type: 'Ajuste',
  sku: 'TORN-01',
  productName: 'Tornillo',
  quantity: 2,
  signedQuantity: -2,
  fromWarehouseCode: 'ALM-01',
  fromBinCode: 'A-01',
  lotNumber: 'L-7',
  reasonCode: 'DAMAGE',
  reason: 'Daño',
  notes: 'Caja rota',
  userName: 'Luis C.',
}

function wrap(permissions: string[], modules: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <KardexTransactionModal txnId={5} onClose={() => {}} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.detail = null
})

describe('KardexTransactionModal', () => {
  it('muestra el movimiento (tipo con color, cantidad con signo, de → a, lote con vencimiento, motivo, nota, usuario) y sin documento: manual', async () => {
    mock.detail = { transaction: row, ownerName: 'Propio', categoryName: 'Ferretería', lotExpiryDate: '2027-01-31', document: null, related: [row], relatedTruncated: false }
    wrap(['inventory.view'], ['WMS_LOTSERIAL'])
    const dialog = await screen.findByRole('dialog', { name: 'Movimiento #5' })
    expect(await within(dialog).findByText('Luis C.')).toBeInTheDocument()
    expect(within(dialog).getAllByText('Ajuste')[0]).toHaveClass('s-fail')
    expect(within(dialog).getAllByText('−2')[0]).toHaveClass('qty-out')
    expect(within(dialog).getAllByText('ALM-01/A-01')[0]).toBeInTheDocument()
    expect(within(dialog).getByText(/L-7 · vence/)).toBeInTheDocument()
    expect(within(dialog).getByText('Caja rota')).toBeInTheDocument()
    expect(within(dialog).getByText('Movimiento manual (sin documento)')).toBeInTheDocument()
    expect(within(dialog).queryByRole('link', { name: 'Abrir' })).toBeNull()
    expect(within(dialog).getByText('Movimientos relacionados (1)')).toBeInTheDocument()
  })

  it('documento de compra: "Abrir" solo con purchasing.view y el módulo PURCHASING', async () => {
    mock.detail = {
      transaction: { ...row, typeCode: 'RECEIPT', type: 'Recepción', signedQuantity: 2 },
      document: { entityCode: 'PURCHASE_ORDER', entityLabel: 'Orden de compra', id: 3, publicId: 'po-1', number: 'OC-00003', status: 'Enviada', partyName: 'Proveedor X' },
      related: [],
    }
    const { unmount } = wrap(['inventory.view'], ['WMS_LOTSERIAL'])
    let dialog = await screen.findByRole('dialog', { name: 'Movimiento #5' })
    expect(await within(dialog).findByText('OC-00003')).toBeInTheDocument()
    expect(within(dialog).queryByRole('link', { name: 'Abrir' })).toBeNull()
    unmount()
    wrap(['inventory.view', 'purchasing.view'], ['WMS_LOTSERIAL', 'PURCHASING'])
    dialog = await screen.findByRole('dialog', { name: 'Movimiento #5' })
    expect(await within(dialog).findByRole('link', { name: 'Abrir' })).toHaveAttribute('href', '/warehouse/purchase-orders/po-1')
  })

  it('tarea de almacén: se muestra la tarea y su documento padre, que es el que se abre', async () => {
    mock.detail = {
      transaction: { ...row, typeCode: 'TRANSFER', type: 'Transferencia' },
      document: {
        entityCode: 'WAREHOUSE_TASK',
        entityLabel: 'Tarea de almacén',
        id: 9,
        number: 'Tarea #9',
        status: 'Completada',
        reference: 'Acomodo',
        parent: { entityCode: 'RECEIPT', entityLabel: 'Recibo', id: 1, publicId: 'rec-1', number: 'REC-000001' },
      },
      related: [],
    }
    wrap(['inventory.view'], ['WMS_LOTSERIAL'])
    const dialog = await screen.findByRole('dialog', { name: 'Movimiento #5' })
    expect(await within(dialog).findByText('Tarea #9')).toBeInTheDocument()
    expect(within(dialog).getByText('REC-000001')).toBeInTheDocument()
    const links = within(dialog).getAllByRole('link', { name: 'Abrir' })
    expect(links).toHaveLength(1)
    expect(links[0]).toHaveAttribute('href', '/warehouse/receipts?receipt=rec-1')
  })

  it('404 del API: "Movimiento no encontrado." tal cual', async () => {
    mock.detail = new Response(JSON.stringify({ title: 'Movimiento no encontrado.', status: 404, code: 'not_found' }), {
      status: 404,
      headers: { 'Content-Type': 'application/problem+json' },
    })
    wrap(['inventory.view'], ['WMS_LOTSERIAL'])
    const dialog = await screen.findByRole('dialog', { name: 'Movimiento #5' })
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Movimiento no encontrado.')
  })
})
