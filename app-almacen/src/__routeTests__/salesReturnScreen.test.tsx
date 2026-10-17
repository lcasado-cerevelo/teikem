// Recibir una devolución de venta (Lote 83, P6): la lista de devoluciones por recibir, la recepción por línea (bueno / dañado / lote) y el envío por la cola de salida (kind salesReturnReceive)
// con la ruta de la devolución; un rechazo del servidor muestra su mensaje exacto. En archivo propio: renderRouter() no aísla del todo su estado global de navegación.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { applySalesReturns } from '../kernel/sync/download'
import { __resetSyncEngineForTests } from '../kernel/sync/engine'
import { listOutbox } from '../kernel/sync/outbox'
import { json, mockFetch, setupDevice } from './countKit'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  __resetSyncEngineForTests()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

const RET = {
  id: 9, publicId: 'dv-9', number: 'DV-00009', warehousePublicId: 'wh-1', clientName: 'Cliente 1', orderNumber: 'OV-00005', statusCode: 'AUTHORIZED', note: 'Cajas rotas', requestedAtUtc: '2026-10-16T10:00:00Z', isActive: true,
  lines: [
    { id: 90, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', trackingTypeCode: 'NONE', qtyRequested: 3, qtyReceived: 0, qtyDamaged: 0 },
    { id: 91, productPublicId: 'p2', sku: 'LOTE', productName: 'Con lote', trackingTypeCode: 'LOT', qtyRequested: 2, qtyReceived: 0, qtyDamaged: 0 },
  ],
}

const ME = (c: { path: string }) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['sales.view', 'warehouse.receive'] }) : null)

async function seed() {
  await setupDevice()
  applySalesReturns(getDb(), 'wh-1', [RET])
}

async function openReturn() {
  await renderRouter('src/app', { initialUrl: '/sales-return' })
  await waitFor(() => expect(screen.getByText('Recibir devolución')).toBeTruthy())
  await fireEvent.press(await screen.findByTestId('return-DV-00009'))
  await waitFor(() => expect(screen.getByTestId('return-line-MED')).toBeTruthy())
}

describe('Recibir devolución de venta en el aparato', () => {
  it('por omisión llega todo bueno: pide el lote del producto que lo lleva y manda POST /sales-returns/{id}/receive', async () => {
    await seed()
    const calls = mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/sales-returns/dv-9/receive' ? json(200, { publicId: 'dv-9', status: 'RECEIVED' }) : null),
    ])
    await openReturn()
    expect((screen.getByTestId('return-good-MED').props as { value: string }).value).toBe('3')
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('LOTE: escribe el lote.')).toBeTruthy())
    await fireEvent.changeText(screen.getByTestId('return-lot-LOTE'), 'l-1')
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('Devolución recibida.')).toBeTruthy())
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/sales-returns/dv-9/receive')
    expect(post?.body).toEqual({ lines: [{ lineId: 90, goodQty: 3, damagedQty: 0 }, { lineId: 91, goodQty: 2, damagedQty: 0, lot: { number: 'l-1' } }] })
    expect(post?.idempotencyKey).toMatch(/^app-/)
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([['salesReturnReceive', 'sent']])
  })

  it('lo dañado pide qué le pasó y manda la descripción y el desecho', async () => {
    await seed()
    const calls = mockFetch([ME, (c) => (c.method === 'POST' && c.path === '/api/v1/sales-returns/dv-9/receive' ? json(200, {}) : null)])
    await openReturn()
    await fireEvent.changeText(screen.getByTestId('return-good-MED'), '2')
    await fireEvent.changeText(screen.getByTestId('return-damaged-MED'), '1')
    await fireEvent.changeText(screen.getByTestId('return-good-LOTE'), '')
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('MED: describe qué le pasó a lo dañado.')).toBeTruthy())
    await fireEvent.changeText(screen.getByTestId('return-note-MED'), 'Caja rota')
    await fireEvent.press(screen.getByTestId('return-discard-MED'))
    await fireEvent.press(screen.getByTestId('return-close-rest'))
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('Devolución recibida.')).toBeTruthy())
    expect(calls.find((c) => c.path === '/api/v1/sales-returns/dv-9/receive')?.body).toEqual({
      lines: [{ lineId: 90, goodQty: 2, damagedQty: 1, damageNote: 'Caja rota', damageDiscard: true }],
      closeRest: true,
    })
  })

  it('no deja recibir más de lo pendiente', async () => {
    await seed()
    mockFetch([ME])
    await openReturn()
    await fireEvent.changeText(screen.getByTestId('return-good-MED'), '4')
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('MED: no puedes recibir más de lo pendiente (3).')).toBeTruthy())
    expect(listOutbox()).toEqual([])
  })

  it('sin señal: queda en la cola, la devolución sale de la lista y el aviso lo dice', async () => {
    await seed()
    mockFetch([ME])
    await openReturn()
    await fireEvent.changeText(screen.getByTestId('return-lot-LOTE'), 'L1')
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('Recepción guardada: se envía cuando haya señal.')).toBeTruthy())
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([['salesReturnReceive', 'pending']])
    expect(getDb().getAllSync('SELECT * FROM sales_return')).toEqual([])
  })

  it('el servidor lo rechaza: se muestra su mensaje y la fila no queda', async () => {
    await seed()
    mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/sales-returns/dv-9/receive' ? json(422, { title: 'De MED solo quedan 1 por recibir.', status: 422, code: 'status_rule' }) : null),
    ])
    await openReturn()
    await fireEvent.changeText(screen.getByTestId('return-lot-LOTE'), 'L1')
    await fireEvent.press(screen.getByTestId('return-receive'))
    await waitFor(() => expect(screen.getByText('De MED solo quedan 1 por recibir.')).toBeTruthy())
    expect(listOutbox()).toEqual([])
  })

  it('escanear un número que no existe avisa', async () => {
    await seed()
    mockFetch([ME])
    await renderRouter('src/app', { initialUrl: '/sales-return' })
    await fireEvent.changeText(await screen.findByLabelText('Número de la devolución'), 'DV-99999')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByText(/Esa devolución no está por recibir/)).toBeTruthy())
  })
})
