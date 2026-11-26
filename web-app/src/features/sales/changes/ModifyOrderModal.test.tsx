// Lote 249 — «Modificar la orden»: el mínimo por línea, el aviso de la orden que solo admite adición, el reabrir el empaque según el permiso y el recorrido completo
// (líneas → términos → autorización → vista previa → guardar) con la solicitud exacta que viaja al servidor.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import type { SalesOrderDto } from '../salesApi'
import { ModifyOrderModal } from './ModifyOrderModal'
import type { EditOptionsDto } from './changesApi'
import { mock, sent, wrap } from './testKit'

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fetchFor } = await import('./testKit')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fetchFor() }) }
})
// el selector de consignatario y el de productos consultan otros endpoints que aquí no importan
vi.mock('../../consignees/ConsigneePicker', () => ({ ConsigneePicker: () => <input aria-label="Consignatario" /> }))
vi.mock('../../warehouse/pickers', () => ({ ProductPicker: () => <input aria-label="Buscar un producto" /> }))

const ORDER: SalesOrderDto = { publicId: 'o-1', number: 'OV-00001', status: 'REQUESTED', clientPublicId: 'c-1', clientName: 'Cliente', requestedDate: '2026-11-20', terms: { paymentMethod: 'CASH', deliveryMode: 'DELIVERY', deliveryModeSet: true }, lines: [] }
const LINE = { lineId: 11, lineNo: 1, productPublicId: 'p-1', sku: 'A1', name: 'Guantes', qtyOrdered: 8, minQuantity: 3, minQuantityWithReopen: 1, inCollected: 0, inPacked: 2, out: 1, invoiced: 0, isRemoved: false, unitPrice: 12.5, taxRatePct: 11.5, hasTaxOverride: false, isKit: false }
const OPTIONS: EditOptionsDto = {
  orderPublicId: 'o-1', orderNumber: 'OV-00001', status: 'REQUESTED', mustUseAddition: false, canAddition: true, lines: [LINE], invoices: [],
  batches: [{ publicId: 'b-1', number: 'REC-1', status: 'PACKED', stage: 'REOPENABLE', packNumber: 'EMP-00001', shipmentNumber: 'ORD-00009' }],
  settings: { approvalMode: 'NONE', attachmentRequired: false, approvalFromClient: false, attachmentFromClient: false },
  permissions: { modify: true, price: true, tax: true, reopenPack: true, approve: true, creditOverride: false }, billingTrigger: 'ON_DISPATCH', total: 100, rowVersion: 'rv1',
}
const PREVIEW = {
  before: { lines: [], terms: {}, subtotal: 0, taxAmount: 0, shippingCharge: 0, shippingTaxAmount: 0, total: 100 },
  after: { lines: [], terms: {}, subtotal: 0, taxAmount: 0, shippingCharge: 0, shippingTaxAmount: 0, total: 110 },
  diff: { added: [], removed: [], changed: [{ lineId: 11, lineNo: 1, sku: 'A1', name: 'Guantes', change: 'CHANGED', fields: [{ field: 'qtyOrdered', before: '8', after: '9' }] }], terms: [], totalBefore: 100, totalAfter: 110, delta: 10 },
  summary: 'x', effects: { reversedBatches: [], reopenedPacks: ['EMP-00001'], warehouseImpact: '' }, credit: {}, creditChecked: false, creditExceeded: false, requiresApproval: false, attachmentRequired: false,
  needsReopen: true, blockers: [], totalBefore: 100, totalAfter: 110, totalDelta: 10,
}

describe('Modificar la orden', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.routes = {
      'GET /api/v1/sales-orders/o-1/edit-options': () => ({ json: OPTIONS }),
      'POST /api/v1/sales-orders/o-1/changes/preview': () => ({ json: PREVIEW }),
      'POST /api/v1/sales-orders/o-1/changes': () => ({ json: { item: { changeNo: 3, isPending: false } } }),
      'GET /api/v1/lookups': () => ({ json: [] }),
      'GET /api/v1/clients/c-1': () => ({ json: { publicId: 'c-1', contacts: [] } }),
    }
  })

  it('muestra la posición y el mínimo de la línea; reabrir el empaque baja el mínimo y solo se ofrece con el permiso', async () => {
    const user = userEvent.setup()
    wrap(<ModifyOrderModal order={ORDER} onClose={() => {}} onAddition={() => {}} />)
    expect(await screen.findByText('Mínimo: 3')).toBeInTheDocument()
    expect(screen.getByText('Empacada: se puede reabrir')).toBeInTheDocument()
    await user.click(screen.getByRole('switch', { name: /Reabrir el empaque EMP-00001/ }))
    expect(screen.getByText('Mínimo: 1')).toBeInTheDocument()
  })

  it('sin warehouse.reopen-pack sugiere la orden interna de adición en vez de la casilla', async () => {
    wrap(<ModifyOrderModal order={ORDER} onClose={() => {}} onAddition={() => {}} />, ['sales.view', 'sales.modify'])
    expect(await screen.findByText(/exige el permiso warehouse.reopen-pack/)).toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: /Reabrir el empaque/ })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Precio')).not.toBeInTheDocument()
  })

  it('una orden facturada solo admite adición: el aviso exacto y el botón de la orden interna', async () => {
    mock.routes['GET /api/v1/sales-orders/o-1/edit-options'] = () => ({
      json: { ...OPTIONS, mustUseAddition: true, orderBlocker: 'La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.' },
    })
    const onAddition = vi.fn()
    const user = userEvent.setup()
    wrap(<ModifyOrderModal order={ORDER} onClose={() => {}} onAddition={onAddition} />)
    expect(await screen.findByText('La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Orden interna de adición' }))
    expect(onAddition).toHaveBeenCalled()
  })

  it('recorre los pasos y registra con la autorización, el empaque reabierto y la huella de la orden', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    wrap(<ModifyOrderModal order={ORDER} onClose={onClose} onAddition={() => {}} />)
    await screen.findByLabelText('Cantidad')
    const dialog = screen.getByRole('dialog', { name: /Modificar la orden OV-00001/ })
    await user.click(await within(dialog).findByRole('switch', { name: /Reabrir el empaque/ }))
    const qty = await within(dialog).findByLabelText('Cantidad')
    await user.clear(qty)
    await user.type(qty, '9')
    await user.click(within(dialog).getByRole('button', { name: 'Siguiente' }))
    await user.click(within(dialog).getByRole('button', { name: 'Siguiente' }))
    // sin quién autorizó no avanza
    await user.click(within(dialog).getByRole('button', { name: 'Siguiente' }))
    expect(within(dialog).getByText('Indique quién autorizó el cambio en el cliente.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Nombre de quien autorizó'), 'Marta Ríos')
    await user.selectOptions(within(dialog).getByLabelText('Cómo autorizó'), 'PHONE')
    await user.type(within(dialog).getByLabelText('Motivo del cambio'), 'Lo pidió por teléfono')
    await user.click(within(dialog).getByRole('button', { name: 'Siguiente' }))
    expect(await within(dialog).findByText(/Total después/)).toBeInTheDocument()
    expect(within(dialog).getByText(/Se reabre el empaque EMP-00001/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar la modificación' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/o-1/changes')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-orders/o-1/changes')[0].body).toMatchObject({
      ops: [{ kind: 'QTY', lineId: 11, quantity: 9 }], authorizedByName: 'Marta Ríos', authorizationChannel: 'PHONE', reason: 'Lo pidió por teléfono', reopenPack: true, rowVersion: 'rv1',
    })
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('los bloqueos de la vista previa salen en el idioma de la interfaz y frenan el guardado', async () => {
    setLang('en')
    mock.routes['POST /api/v1/sales-orders/o-1/changes/preview'] = () => ({ json: { ...PREVIEW, blockers: ['No se puede bajar A1 a 2: ya salieron 3.'] } })
    const user = userEvent.setup()
    wrap(<ModifyOrderModal order={ORDER} onClose={() => {}} onAddition={() => {}} />)
    await screen.findByLabelText('Quantity')
    const dialog = screen.getByRole('dialog', { name: /Modify order OV-00001/ })
    const qty = await within(dialog).findByLabelText('Quantity')
    await user.clear(qty)
    await user.type(qty, '9')
    for (let i = 0; i < 2; i++) await user.click(within(dialog).getByRole('button', { name: 'Next' }))
    await user.type(within(dialog).getByLabelText('Name of who authorized'), 'Marta')
    await user.selectOptions(within(dialog).getByLabelText('How they authorized'), 'EMAIL')
    await user.type(within(dialog).getByLabelText('Reason for the change'), 'Email')
    await user.click(within(dialog).getByRole('button', { name: 'Next' }))
    expect(await within(dialog).findByText('A1 cannot be lowered to 2: 3 already shipped.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Save the modification' })).toBeDisabled()
    setLang('es')
  })
})
