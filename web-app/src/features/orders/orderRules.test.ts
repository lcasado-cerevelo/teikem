// Lote F-C1 — reglas puras de Órdenes: filtros → consulta, filas por tipo de paquete, esquema del alta (mensajes del servidor),
// request de POST /orders, renombre de errores y detección de crédito excedido / factura repetida.
import { describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import es from '../../kernel/i18n/es.json'
import en from '../../kernel/i18n/en.json'
import {
  buildOrderQuery,
  buildOrderRequest,
  canDeleteOrder,
  dayStart,
  duplicateInvoiceOf,
  EMPTY_ORDER_FILTERS,
  emptyOrderValues,
  emptyPackage,
  exclusiveTo,
  isCreditExceeded,
  orderSchema,
  packageGroups,
  packageSummaryText,
  remapOrderProblem,
  type OrderFormValues,
  type OrderMessages,
} from './orderRules'

const M: OrderMessages = {
  clientRequired: 'El cliente es obligatorio.',
  serviceRequired: 'El tipo de servicio es obligatorio.',
  consigneeRequired: 'El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.',
  packagesRequired: 'Indique al menos una línea de paquete.',
  packageTypeRequired: 'El tipo de paquete es obligatorio.',
  pieces: 'La cantidad de piezas debe ser al menos 1.',
  codNegative: 'El monto COD no puede ser negativo.',
  specialRequired: 'El servicio especial es obligatorio en una entrega especial.',
  numberMax: 'El número no puede exceder 40 caracteres.',
  descriptionMax: 'Máximo 200 caracteres.',
  dueBeforeReady: 'La fecha límite no puede ser anterior a la fecha lista.',
}

const valid = (over: Partial<OrderFormValues> = {}): OrderFormValues => ({
  ...emptyOrderValues('2026-10-12'),
  clientPublicId: 'c-1',
  serviceType: 'STANDARD',
  consigneeLocationPublicId: 'l-1',
  packages: [{ packageType: 'BOX', description: '', pieces: 2 }],
  ...over,
})

/** Mensajes por ruta de campo del esquema. */
function issues(v: OrderFormValues): Record<string, string> {
  const r = orderSchema(M).safeParse(v)
  if (r.success) return {}
  return Object.fromEntries(r.error.issues.map((i) => [i.path.join('.'), i.message]))
}

describe('filtros de la lista', () => {
  it('lo vacío no se manda y la paginación sale de la página', () => {
    expect(buildOrderQuery(EMPTY_ORDER_FILTERS, 3, 25)).toEqual({
      from: undefined, to: undefined, orderNumber: undefined, consignee: undefined, clientIds: undefined, serviceTypes: undefined,
      packageTypes: undefined, cities: undefined, postalCodePrefix: undefined, invoice: undefined, packBatch: undefined, statuses: undefined,
      search: undefined, skip: 50, take: 25,
    })
  })

  it('manda los arreglos tal cual, el texto recortado y «Hasta» como día siguiente (exclusivo)', () => {
    const q = buildOrderQuery(
      { ...EMPTY_ORDER_FILTERS, from: '2026-10-01', to: '2026-10-31', orderNumber: ' ORD-1 ', clientIds: ['a', 'b'], serviceTypes: ['STANDARD', 'EXPRESS'], packageTypes: ['BOX'], cities: ['Ponce'], postalCodePrefix: '009', statuses: ['DRAFT'], search: 'x' },
      1,
      10,
    )
    expect(q).toMatchObject({ from: '2026-10-01', to: '2026-11-01', orderNumber: 'ORD-1', clientIds: ['a', 'b'], serviceTypes: ['STANDARD', 'EXPRESS'], packageTypes: ['BOX'], cities: ['Ponce'], postalCodePrefix: '009', statuses: ['DRAFT'], search: 'x', skip: 0, take: 10 })
  })

  it('exclusiveTo cruza el fin de mes y de año y rechaza lo que no es fecha', () => {
    expect(exclusiveTo('2026-12-31')).toBe('2027-01-01')
    expect(exclusiveTo('')).toBeUndefined()
    expect(exclusiveTo('nope')).toBeUndefined()
  })
})

describe('filas por tipo de paquete', () => {
  it('agrupa las líneas del mismo tipo (suma piezas, junta descripciones distintas) en orden de aparición', () => {
    const groups = packageGroups({
      totalPieces: 7,
      packagesSummary: 'x',
      packages: [
        { packageType: 'BOX', packageTypeLabel: 'Caja', pieces: 2, description: 'Ropa' },
        { packageType: 'ENV', packageTypeLabel: 'Sobre', pieces: 1, description: '' },
        { packageType: 'BOX', packageTypeLabel: 'Caja', pieces: 4, description: 'Ropa' },
        { packageType: 'BOX', packageTypeLabel: 'Caja', pieces: 0, description: 'Zapatos' },
      ],
    })
    expect(groups).toEqual([
      { label: 'Caja', descriptions: ['Ropa', 'Zapatos'], pieces: 6 },
      { label: 'Sobre', descriptions: [], pieces: 1 },
    ])
    expect(packageSummaryText(groups)).toBe('Caja ×6 + Sobre ×1')
  })

  it('una orden sin líneas cae al resumen y a las piezas totales; la entrega especial usa el nombre del servicio', () => {
    expect(packageGroups({ packages: [], packagesSummary: 'Caja ×2', totalPieces: 2 })).toEqual([{ label: 'Caja ×2', descriptions: [], pieces: 2 }])
    expect(packageGroups({ packages: null, packagesSummary: null, totalPieces: 0 })).toEqual([{ label: '—', descriptions: [], pieces: 0 }])
    expect(packageGroups({ totalPieces: 1, packagesSummary: null, packages: [{ packageType: null, description: 'Traer vagón', pieces: 1 }] })).toEqual([
      { label: 'Traer vagón', descriptions: [], pieces: 1 },
    ])
  })

  it('la papelera solo en el estatus inicial y activa', () => {
    expect(canDeleteOrder({ isInitialStatus: true, isActive: true })).toBe(true)
    expect(canDeleteOrder({ isInitialStatus: false, isActive: true })).toBe(false)
    expect(canDeleteOrder({ isInitialStatus: true, isActive: false })).toBe(false)
    expect(canDeleteOrder({})).toBe(false)
  })
})

describe('esquema del alta (mensajes exactos del servidor)', () => {
  it('una orden válida no tiene problemas', () => {
    expect(issues(valid())).toEqual({})
  })

  it('cliente, servicio y consignatario obligatorios', () => {
    expect(issues(valid({ clientPublicId: null, serviceType: '', consigneeLocationPublicId: '' }))).toEqual({
      clientPublicId: M.clientRequired,
      serviceType: M.serviceRequired,
      consigneeLocationPublicId: M.consigneeRequired,
    })
  })

  it('paquetes: al menos uno, tipo obligatorio, piezas ≥ 1 y enteras', () => {
    expect(issues(valid({ packages: [] }))).toEqual({ packages: M.packagesRequired })
    expect(issues(valid({ packages: [emptyPackage()] }))).toEqual({ 'packages.0.packageType': M.packageTypeRequired })
    expect(issues(valid({ packages: [{ packageType: 'BOX', description: '', pieces: 0 }] }))).toEqual({ 'packages.0.pieces': M.pieces })
    expect(issues(valid({ packages: [{ packageType: 'BOX', description: '', pieces: 1.5 }] }))).toEqual({ 'packages.0.pieces': M.pieces })
    expect(issues(valid({ packages: [{ packageType: 'BOX', description: '', pieces: null }] }))).toEqual({ 'packages.0.pieces': M.pieces })
  })

  it('COD negativo; descripción y números con tope', () => {
    expect(issues(valid({ codAmount: -1 }))).toEqual({ codAmount: M.codNegative })
    expect(issues(valid({ codAmount: 0 }))).toEqual({})
    expect(issues(valid({ clientInvoiceNumber: 'x'.repeat(41) }))).toEqual({ clientInvoiceNumber: M.numberMax })
    expect(issues(valid({ packages: [{ packageType: 'BOX', description: 'x'.repeat(4001), pieces: 1 }] }))).toEqual({ 'packages.0.description': M.descriptionMax })
  })

  it('el límite no puede ser anterior a la fecha lista', () => {
    expect(issues(valid({ readyDate: '2026-10-12', dueDate: '2026-10-11' }))).toEqual({ dueDate: M.dueBeforeReady })
    expect(issues(valid({ readyDate: '2026-10-12', dueDate: '' }))).toEqual({})
  })

  it('entrega especial: exige el servicio y no valida paquetes ni COD', () => {
    const base = valid({ isSpecial: true, packages: [emptyPackage()], codAmount: -5 })
    expect(issues(base)).toEqual({ specialServiceId: M.specialRequired })
    expect(issues({ ...base, specialServiceId: '12' })).toEqual({})
  })
})

describe('request de POST /orders', () => {
  const opts = { invoiceEditable: false, orderNumberEditable: false }

  it('Sometida: paquetes, COD, fechas Lista/Límite y sin factura si la asigna Teikem', () => {
    const req = buildOrderRequest(
      valid({
        readyDate: '2026-10-12',
        dueDate: '2026-10-14',
        clientInvoiceNumber: 'FAC-9',
        codAmount: 50,
        packages: [
          { packageType: 'BOX', description: ' Ropa ', pieces: 2 },
          { packageType: 'ENV', description: '', pieces: 1 },
        ],
      }),
      opts,
    )
    expect(req).toEqual({
      clientPublicId: 'c-1',
      consigneeLocationPublicId: 'l-1',
      serviceType: 'STANDARD',
      requestedDate: '2026-10-12T00:00:00',
      promisedDate: '2026-10-14T00:00:00',
      clientInvoiceNumber: null,
      packages: [
        { packageType: 'BOX', description: 'Ropa', pieces: 2 },
        { packageType: 'ENV', description: null, pieces: 1 },
      ],
      codAmount: 50,
    })
    expect('newConsignee' in req).toBe(false)
  })

  it('la factura tecleada solo viaja si el cliente la asigna; COD 0 o vacío no se manda', () => {
    const v = valid({ clientInvoiceNumber: ' FAC-9 ', codAmount: 0 })
    expect(buildOrderRequest(v, { ...opts, invoiceEditable: true }).clientInvoiceNumber).toBe('FAC-9')
    expect(buildOrderRequest(v, { ...opts, invoiceEditable: true }).codAmount).toBeNull()
    expect(buildOrderRequest(valid({ clientInvoiceNumber: '  ' }), { ...opts, invoiceEditable: true }).clientInvoiceNumber).toBeNull()
  })

  it('autorizar crédito y confirmar factura repetida solo van cuando se piden; confirmNow nunca se manda', () => {
    const plain = buildOrderRequest(valid(), opts)
    expect('confirmNow' in plain).toBe(false)
    expect('overrideCredit' in plain).toBe(false)
    expect('confirmDuplicateInvoice' in plain).toBe(false)
    const full = buildOrderRequest(valid(), { ...opts, overrideCredit: true, confirmDuplicateInvoice: true })
    expect(full).toMatchObject({ overrideCredit: true, confirmDuplicateInvoice: true })
  })

  it('entrega especial: isSpecialDelivery + specialServiceId, sin paquetes ni COD ni chofer', () => {
    const req = buildOrderRequest(valid({ isSpecial: true, specialServiceId: '12', orderNumber: ' PO-1 ', codAmount: 30 }), { ...opts, orderNumberEditable: true })
    expect(req).toMatchObject({ isSpecialDelivery: true, specialServiceId: 12, orderNumber: 'PO-1' })
    expect('packages' in req).toBe(false)
    expect('codAmount' in req).toBe(false)
    expect('driverPublicId' in req).toBe(false)
    // si Teikem asigna el número de orden, no se manda
    expect('orderNumber' in buildOrderRequest(valid({ isSpecial: true, specialServiceId: '12', orderNumber: 'X' }), opts)).toBe(false)
  })

  it('dayStart', () => {
    expect(dayStart('2026-10-12')).toBe('2026-10-12T00:00:00')
    expect(dayStart('')).toBeNull()
  })
})

describe('errores del servidor', () => {
  const api = (status: number, title: string, code: string, errors: Record<string, string[]> = {}) => new ApiError(status, { title, code, errors })

  it('renombra consignatario y líneas, y no pinta los datos de la factura repetida', () => {
    const out = remapOrderProblem(
      api(400, 'x', 'validation', {
        consignee: ['A'],
        'newConsignee.name': ['B'],
        'packages[1].packageType': ['C'],
        'packages[0].pieces': ['D'],
        clientInvoiceNumber: ['E'],
        existingOrderNumber: ['ORD-1'],
        existingOrderPublicId: ['abc'],
        driverPublicId: ['F'],
      }),
    ) as ApiError
    expect(out.errors).toEqual({
      consigneeLocationPublicId: ['A', 'B'],
      'packages.1.packageType': ['C'],
      'packages.0.pieces': ['D'],
      clientInvoiceNumber: ['E'],
      driverPublicId: ['F'],
    })
    expect(out.title).toBe('x')
    expect(remapOrderProblem(new Error('z'))).toBeInstanceOf(Error)
  })

  it('crédito excedido: 422 con code credit_exceeded', () => {
    expect(isCreditExceeded(api(422, 'El cliente excede su límite de crédito.', 'credit_exceeded'))).toBe(true)
    expect(isCreditExceeded(api(422, 'No hay tarifa vigente', 'status_rule'))).toBe(false)
    expect(isCreditExceeded(new Error('x'))).toBe(false)
  })

  it('factura repetida: confirmable o bloqueada, con la orden existente', () => {
    expect(duplicateInvoiceOf(api(409, 'm', 'duplicate_invoice_confirmable', { existingOrderNumber: ['ORD-7'], existingOrderPublicId: ['p-7'] }))).toEqual({
      confirmable: true,
      message: 'm',
      existingOrderNumber: 'ORD-7',
      existingOrderPublicId: 'p-7',
    })
    expect(duplicateInvoiceOf(api(409, 'm', 'duplicate_invoice'))?.confirmable).toBe(false)
    expect(duplicateInvoiceOf(api(409, 'm', 'conflict'))).toBeNull()
    expect(duplicateInvoiceOf(api(422, 'm', 'duplicate_invoice'))).toBeNull()
  })
})

describe('textos', () => {
  it('es y en tienen las mismas claves de orders.*', () => {
    const keys = (o: unknown, prefix = ''): string[] =>
      Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => (typeof v === 'object' && v !== null ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]))
    expect(keys(en.orders).sort()).toEqual(keys(es.orders).sort())
  })

  it('los mensajes de validación en español son los del servidor (manual 03 §1.1)', () => {
    const e = es.orders.entry.errors
    expect(e.clientRequired).toBe(M.clientRequired)
    expect(e.serviceRequired).toBe(M.serviceRequired)
    expect(e.consigneeRequired).toBe(M.consigneeRequired)
    expect(e.packagesRequired).toBe(M.packagesRequired)
    expect(e.packageTypeRequired).toBe(M.packageTypeRequired)
    expect(e.pieces).toBe(M.pieces)
    expect(e.codNegative).toBe(M.codNegative)
    expect(e.specialRequired).toBe(M.specialRequired)
    expect(e.numberMax).toBe(M.numberMax)
  })
})
