// Reglas de la captura rápida: búsqueda de consignatarios, la fila siguiente conserva fecha/servicio/paquete, validación con las mismas claves de «Nuevo envío» y la solicitud al servidor.
import type { PortalPlaceDto } from '../portalApi'
import { emptyConsignee, emptyQuickRow, filterSavedEntries, firstErrorField, matchConsignees, nextQuickRow, quickRequest, setRangeEdge, splitInvoices, summarizeSaved, widenRange, todayText, toDraft, validateQuickRow } from './quickRules'

const PLACES: PortalPlaceDto[] = [
  { publicId: 'a', name: 'Farmacia Norte', line1: 'Calle 1', city: 'Bayamón', code: 'C-100' },
  { publicId: 'b', name: 'Colmado Sur', line1: 'Av. Principal 22', city: 'Ponce' },
  { publicId: 'c', name: 'Farmacia del Sur', line1: 'Calle 9', city: 'Ponce' },
]

const TODAY = new Date(2026, 9, 22)
const RULES = { hasServiceTypes: true, hasPackageTypes: true, typesInvoice: true, assignsOrderNumber: true }

describe('quickRules', () => {
  it('la fecha de hoy sale como AAAA-MM-DD', () => {
    expect(todayText(TODAY)).toBe('2026-10-22')
  })

  it('busca el consignatario por nombre, número, dirección o pueblo, con todas las palabras', () => {
    expect(matchConsignees(PLACES, 'farm').map((c) => c.publicId)).toEqual(['a', 'c'])
    expect(matchConsignees(PLACES, 'farmacia ponce').map((c) => c.publicId)).toEqual(['c'])
    expect(matchConsignees(PLACES, 'AV PRINCIPAL').map((c) => c.publicId)).toEqual(['b'])
    expect(matchConsignees(PLACES, 'c-100').map((c) => c.publicId)).toEqual(['a'])
    expect(matchConsignees(PLACES, '   ')).toEqual([])
    expect(matchConsignees(PLACES, 'zzz')).toEqual([])
  })

  it('la fila siguiente conserva la fecha, el servicio y el tipo de paquete y deja lo demás en blanco', () => {
    const prev = { ...emptyQuickRow('2026-10-23', { serviceType: 'STD', packageType: 'BOX' }), consigneeId: 'a', cod: '10', notes: 'x', orderNumber: 'O-1' }
    expect(nextQuickRow(prev)).toEqual(emptyQuickRow('2026-10-23', { serviceType: 'STD', packageType: 'BOX' }))
  })

  it('sin consignatario no se guarda y el foco va al consignatario; sin servicio ni paquete tampoco', () => {
    const errors = validateQuickRow({ ...emptyQuickRow('2026-10-22'), consigneeId: 'a' }, RULES, TODAY)
    expect(errors).toEqual(['service', 'packageType'])
    const noCons = validateQuickRow(emptyQuickRow('2026-10-22', { serviceType: 'STD', packageType: 'BOX' }), RULES, TODAY)
    expect(noCons).toEqual(['consignee'])
    expect(firstErrorField(noCons)).toBe('consignee')
  })

  it('con consignatario, servicio, paquete y fecha válidas no hay errores; sin fecha la rápida exige fecha', () => {
    const base = { ...emptyQuickRow('2026-10-22', { serviceType: 'STD', packageType: 'BOX' }), consigneeId: 'a' }
    expect(validateQuickRow(base, RULES, TODAY)).toEqual([])
    expect(validateQuickRow({ ...base, date: '' }, RULES, TODAY)).toEqual(['date'])
  })

  it('valida COD y fecha como «Nuevo envío» y el primer error sigue el orden de la fila', () => {
    const base = { ...emptyQuickRow('2026-10-22', { serviceType: 'STD', packageType: 'BOX' }), consigneeId: 'a' }
    expect(validateQuickRow({ ...base, cod: 'x' }, RULES, TODAY)).toContain('cod')
    expect(validateQuickRow({ ...base, date: '2026-10-01' }, RULES, TODAY)).toContain('date')
    expect(validateQuickRow({ ...base, orderNumber: 'x'.repeat(41) }, RULES, TODAY)).toContain('orderNumber')
    expect(firstErrorField(validateQuickRow({ ...base, cod: 'x', date: '2026-10-01' }, RULES, TODAY))).toBe('date')
  })

  it('un consignatario nuevo pide nombre, dirección y pueblo', () => {
    const row = { ...emptyQuickRow('2026-10-22', { serviceType: 'STD', packageType: 'BOX' }), newConsignee: { ...emptyConsignee(), name: 'Tienda Nueva' } }
    expect(validateQuickRow(row, RULES, TODAY)).toContain('consignee')
    expect(validateQuickRow({ ...row, newConsignee: { ...emptyConsignee(), name: 'Tienda Nueva', line1: 'Calle 5', city: 'Caguas' } }, RULES, TODAY)).toEqual([])
  })

  it('arma la solicitud: un paquete de una pieza con su tipo, servicio, la fecha como lista y límite, COD y los números solo si el cliente los asigna', () => {
    const row = { ...emptyQuickRow('2026-10-22', { serviceType: 'STD', packageType: 'BOX' }), consigneeId: 'a', cod: '25.5', invoice: 'F-9', orderNumber: 'O-7', notes: 'frágil' }
    expect(toDraft(row).packages).toEqual([{ packageType: 'BOX', description: '', pieces: '1', weightKg: '' }])
    const req = quickRequest(row, RULES)
    expect(req).toMatchObject({ consigneePublicId: 'a', serviceType: 'STD', codAmount: 25.5, clientInvoiceNumber: 'F-9', orderNumber: 'O-7', notes: 'frágil', requestedDate: '2026-10-22T00:00:00Z', promisedDate: '2026-10-22T00:00:00Z' })
    expect(req.packages).toEqual([{ packageType: 'BOX', description: null, pieces: 1, weightKg: null }])
    const plain = quickRequest(row, { ...RULES, typesInvoice: false, assignsOrderNumber: false })
    expect(plain.clientInvoiceNumber).toBeNull()
    expect(plain.orderNumber).toBeNull()
  })
})

describe('lo capturado: rango de fechas, búsqueda, resumen del pie', () => {
  const rows = [
    { date: '2026-10-23', invoice: 'F-1', packageLabel: 'Caja', serviceLabel: 'Estándar', cod: 10.1, consignee: 'Farmacia Central' },
    { date: '2026-10-23', invoice: 'F-2, F-3', packageLabel: 'Caja', serviceLabel: 'Estándar', cod: 20.2, consignee: 'Colmado Boricua' },
    { date: '2026-10-23', invoice: 'f-1', packageLabel: 'Sobre', serviceLabel: 'Expreso', cod: 0, consignee: 'Café Ñandú' },
    { date: '2026-10-24', invoice: '', packageLabel: 'Caja', serviceLabel: 'Expreso', cod: 5, consignee: 'Farmacia Norte' },
  ]
  const texts = (r: (typeof rows)[number]) => [r.consignee, r.packageLabel, r.serviceLabel, r.cod]
  const day = (d: string) => ({ from: d, to: d })

  it('deja las del rango (desde y hasta incluidos); un solo día es desde = hasta', () => {
    expect(filterSavedEntries(rows, day('2026-10-23'), '', texts)).toHaveLength(3)
    expect(filterSavedEntries(rows, day('2026-10-24'), '', texts)).toHaveLength(1)
    expect(filterSavedEntries(rows, { from: '2026-10-23', to: '2026-10-24' }, '', texts)).toHaveLength(4)
    expect(filterSavedEntries(rows, day('2026-10-25'), '', texts)).toHaveLength(0)
  })
  it('la búsqueda mira cualquier texto, sin importar mayúsculas ni acentos, con todas las palabras', () => {
    const d = day('2026-10-23')
    expect(filterSavedEntries(rows, d, 'FARMACIA', texts).map((r) => r.consignee)).toEqual(['Farmacia Central'])
    expect(filterSavedEntries(rows, d, 'cafe nandu', texts)).toHaveLength(1)
    expect(filterSavedEntries(rows, d, 'caja estandar', texts)).toHaveLength(2)
    expect(filterSavedEntries(rows, d, '20.2', texts)).toHaveLength(1)
    expect(filterSavedEntries(rows, d, 'farmacia norte', texts)).toHaveLength(0)   // es de otro día
  })
  it('capturar una fecha ensancha el rango: el desde se queda en hoy y el hasta sigue a la más adelantada; con una más vieja, el desde baja', () => {
    const today = day('2026-10-23')
    expect(widenRange(today, '2026-10-23')).toEqual(today)
    expect(widenRange(today, '2026-10-26')).toEqual({ from: '2026-10-23', to: '2026-10-26' })
    expect(widenRange({ from: '2026-10-23', to: '2026-10-26' }, '2026-10-20')).toEqual({ from: '2026-10-20', to: '2026-10-26' })
    expect(widenRange({ from: '2026-10-23', to: '2026-10-26' }, '2026-10-25')).toEqual({ from: '2026-10-23', to: '2026-10-26' })
    expect(widenRange(today, '')).toEqual(today)
  })
  it('mover un extremo a mano no deja el rango al revés', () => {
    const r = { from: '2026-10-23', to: '2026-10-25' }
    expect(setRangeEdge(r, 'from', '2026-10-24')).toEqual({ from: '2026-10-24', to: '2026-10-25' })
    expect(setRangeEdge(r, 'from', '2026-10-30')).toEqual({ from: '2026-10-30', to: '2026-10-30' })
    expect(setRangeEdge(r, 'to', '2026-10-20')).toEqual({ from: '2026-10-20', to: '2026-10-20' })
    expect(setRangeEdge(r, 'to', '')).toEqual(r)
  })
  it('los números de factura se separan por coma: cada coma es otra factura', () => {
    expect(splitInvoices('F-2, F-3 ,, F-4')).toEqual(['F-2', 'F-3', 'F-4'])
    expect(splitInvoices('  ')).toEqual([])
    expect(splitInvoices(undefined)).toEqual([])
  })
  it('el resumen cuenta paquetes por tipo y servicio (las más numerosas primero), facturas DISTINTAS (con comas, sin importar mayúsculas) y suma el COD', () => {
    const s = summarizeSaved(filterSavedEntries(rows, day('2026-10-23'), '', texts))
    expect(s.total).toBe(3)
    expect(s.invoices).toBe(3)   // F-1 (y f-1, la misma), F-2 y F-3
    expect(s.cod).toBe(30.3)
    expect(s.lines).toEqual([
      { packageLabel: 'Caja', serviceLabel: 'Estándar', count: 2 },
      { packageLabel: 'Sobre', serviceLabel: 'Expreso', count: 1 },
    ])
    expect(summarizeSaved([])).toEqual({ total: 0, invoices: 0, cod: 0, lines: [] })
  })
})

