// Lote 183 (C4) — pestaña «Maestros» de Recibo sobre un fetch simulado: la lista, «Recibir juntos» (bloquea otro dueño, pide dos), la ficha con sus recibos, confirmar todo y eliminar.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ReceiptGroupsTab } from './ReceiptGroupsTab'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], status: 'OPEN' as string }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const [status, out] = route(req.method, url)
    return new Response(status === 204 ? null : JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const G = 'cccccccc-0000-0000-0000-000000000001'
const R1 = 'dddddddd-0000-0000-0000-000000000001'
const R2 = 'dddddddd-0000-0000-0000-000000000002'
const member = (id: string, number: string, ref: string, confirmed: boolean) => ({ receiptPublicId: id, receiptNumber: number, statusCode: confirmed ? 'RECEIVED' : 'RECEIVING', status: confirmed ? 'Recibido' : 'Recibiendo', originKind: 'ASN', originRef: ref, lineCount: 1, expectedQty: 5, receivedQty: 5, isConfirmed: confirmed })
const group = (status: string) => ({
  publicId: G, number: 'RCM-00001', statusCode: status, warehousePublicId: 'w1', warehouseCode: 'ALM-01', ownerClientPublicId: 'c1', ownerName: 'ASICS Care', note: 'Camión de la mañana',
  createdAtUtc: '2026-10-21T12:00:00Z', createdBy: 'Ana Ruiz', memberCount: 2, confirmedCount: status === 'CONFIRMED' ? 2 : 0, expectedQty: 10, receivedQty: 10, canConfirm: status === 'OPEN', canDelete: status === 'OPEN', isActive: true,
  rowVersion: 'AA==', members: [member(R1, 'REC-00001', 'LLE-00001', status === 'CONFIRMED'), member(R2, 'REC-00002', 'LLE-00002', status === 'CONFIRMED')],
})
const asn = (id: number, number: string, client: string | null, clientName: string | null, warehouse = 'w1') => ({ id, number, warehousePublicId: warehouse, warehouseCode: warehouse === 'w1' ? 'ALM-01' : 'ALM-02', clientPublicId: client, clientName, reference: `ref-${id}`, statusCode: 'EXPECTED', status: 'Esperado', isActive: true, lines: [] })

function route(method: string, url: URL): [number, unknown] {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/receipts/groups') return [200, { total: 1, skip: 0, take: 25, items: [group(mock.status)] }]
  if (method === 'GET' && p === `/api/v1/receipts/groups/${G}`) return [200, group(mock.status)]
  if (method === 'GET' && p === '/api/v1/asns') return [200, [asn(1, 'LLE-00001', 'c1', 'ASICS Care'), asn(2, 'LLE-00002', 'c1', 'ASICS Care'), asn(3, 'LLE-00003', 'c2', 'Otra Marca'), asn(4, 'LLE-00004', 'c1', 'ASICS Care', 'w2')]]
  if (method === 'GET' && p === '/api/v1/purchase-orders') return [200, { total: 1, skip: 0, take: 50, items: [{ publicId: 'po1', number: 'PO-00001', supplierName: 'Proveedor Uno', warehousePublicId: 'w1', warehouseCode: 'ALM-01', statusCode: 'SENT' }, { publicId: 'po2', number: 'PO-00002', supplierName: 'Borrador', warehousePublicId: 'w1', warehouseCode: 'ALM-01', statusCode: 'DRAFT' }] }]
  if (method === 'POST' && p === '/api/v1/receipts/groups') return [200, group('OPEN')]
  if (method === 'POST' && p === `/api/v1/receipts/groups/${G}/confirm`) return [200, group('CONFIRMED')]
  if (method === 'DELETE' && p === `/api/v1/receipts/groups/${G}`) return [204, null]
  return [200, []]
}

function wrap(permissions = ['inventory.view', 'warehouse.receive'], onOpenReceipt = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <ReceiptGroupsTab onOpenReceipt={onOpenReceipt} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
  return onOpenReceipt
}
const sent = (method: string, suffix: string) => mock.calls.filter((c) => c.method === method && c.url.pathname.endsWith(suffix))

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.status = 'OPEN'
  window.localStorage.clear()
})

describe('Recepciones maestras (pestaña Maestros)', () => {
  it('lista las recepciones con su dueño, sus documentos y su estatus', async () => {
    wrap()
    expect(await screen.findByText('RCM-00001')).toBeInTheDocument()
    expect(screen.getByText('ASICS Care')).toBeInTheDocument()
    expect(screen.getByText('LLE-00001, LLE-00002')).toBeInTheDocument()
    expect(screen.getAllByText('Abierta').length).toBeGreaterThan(1)   // la opción del filtro y el estatus de la fila
  })

  it('«Recibir juntos»: pide dos, bloquea lo de otro dueño o almacén y manda los avisos con el transporte', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Recibir juntos' }))
    const dialog = await screen.findByRole('dialog', { name: 'Recibir juntos' })
    expect(await within(dialog).findByText('LLE-00001')).toBeInTheDocument()
    const submit = within(dialog).getByRole('button', { name: 'Recibir juntos (0)' })
    expect(submit).toBeDisabled()
    await user.click(within(dialog).getByRole('checkbox', { name: /LLE-00001/ }))
    expect(within(dialog).getByRole('checkbox', { name: /LLE-00003/ })).toBeDisabled()   // otro dueño
    expect(within(dialog).getByRole('checkbox', { name: /LLE-00004/ })).toBeDisabled()   // otro almacén
    expect(within(dialog).getByText('Elija al menos dos documentos.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('checkbox', { name: /LLE-00002/ }))
    await user.type(within(dialog).getByLabelText('Transporte (opcional)'), 'Transportes Uno')
    await user.click(within(dialog).getByRole('button', { name: 'Recibir juntos (2)' }))
    await waitFor(() => expect(sent('POST', '/receipts/groups')).toHaveLength(1))
    expect(sent('POST', '/receipts/groups')[0].body).toMatchObject({ documents: [{ asnId: 1 }, { asnId: 2 }], carrier: 'Transportes Uno' })
    expect(await screen.findByRole('dialog', { name: /RCM-00001/ })).toBeInTheDocument()
  })

  it('también se eligen órdenes de compra (lo propio): solo las enviadas y no se mezclan con lo de un cliente', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Recibir juntos' }))
    const dialog = await screen.findByRole('dialog', { name: 'Recibir juntos' })
    const order = await within(dialog).findByRole('checkbox', { name: /PO-00001/ })
    expect(within(dialog).queryByRole('checkbox', { name: /PO-00002/ })).toBeNull()   // un borrador no se recibe
    await user.click(order)
    expect(within(dialog).getByRole('checkbox', { name: /LLE-00001/ })).toBeDisabled()   // lo de un cliente no se junta con lo propio
  })

  it('la ficha muestra los recibos, abre uno, confirma todo y elimina con confirmación', async () => {
    const user = userEvent.setup()
    const onOpenReceipt = wrap()
    await user.click(await screen.findByText('RCM-00001'))
    const dialog = await screen.findByRole('dialog', { name: /RCM-00001/ })
    expect(await within(dialog).findByText('REC-00001')).toBeInTheDocument()
    expect(within(dialog).getByText('0 de 2 recibos confirmados · 10 / 10')).toBeInTheDocument()
    await user.click(within(dialog).getAllByRole('button', { name: 'Abrir recibo' })[1])
    expect(onOpenReceipt).toHaveBeenCalledWith(R2)

    await user.click(within(dialog).getByRole('button', { name: 'Confirmar todo' }))
    const confirm = await screen.findByRole('dialog', { name: '¿Confirmar todos los recibos?' })
    await user.click(within(confirm).getByRole('button', { name: 'Confirmar todo' }))
    await waitFor(() => expect(sent('POST', `/groups/${G}/confirm`)).toHaveLength(1))

    mock.status = 'OPEN'
    await user.click(await within(dialog).findByRole('button', { name: 'Eliminar' }))
    const del = await screen.findByRole('dialog', { name: '¿Eliminar la recepción maestra?' })
    await user.click(within(del).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(sent('DELETE', `/groups/${G}`)).toHaveLength(1))
  })

  it('una confirmada no ofrece confirmar ni eliminar; sin permiso de recibir no hay botones', async () => {
    const user = userEvent.setup()
    mock.status = 'CONFIRMED'
    wrap(['inventory.view'])
    expect(screen.queryByRole('button', { name: 'Recibir juntos' })).toBeNull()
    await user.click(await screen.findByText('RCM-00001'))
    const dialog = await screen.findByRole('dialog', { name: /RCM-00001/ })
    await within(dialog).findByText('REC-00001')
    expect(within(dialog).queryByRole('button', { name: 'Confirmar todo' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Eliminar' })).toBeNull()
  })
})
