// Reglas de la captura rápida: búsqueda de consignatarios, la fila siguiente conserva la fecha, validación con las mismas claves de «Nuevo envío» y la solicitud al servidor.
import type { PortalPlaceDto } from '../portalApi'
import { emptyConsignee, emptyQuickRow, firstErrorField, matchConsignees, nextQuickRow, quickRequest, todayText, toDraft, validateQuickRow } from './quickRules'

const PLACES: PortalPlaceDto[] = [
  { publicId: 'a', name: 'Farmacia Norte', line1: 'Calle 1', city: 'Bayamón' },
  { publicId: 'b', name: 'Colmado Sur', line1: 'Av. Principal 22', city: 'Ponce' },
  { publicId: 'c', name: 'Farmacia del Sur', line1: 'Calle 9', city: 'Ponce' },
]

const TODAY = new Date(2026, 9, 22)

describe('quickRules', () => {
  it('la fecha de hoy sale como AAAA-MM-DD', () => {
    expect(todayText(TODAY)).toBe('2026-10-22')
  })

  it('busca el consignatario por nombre, dirección o pueblo, con todas las palabras', () => {
    expect(matchConsignees(PLACES, 'farm').map((c) => c.publicId)).toEqual(['a', 'c'])
    expect(matchConsignees(PLACES, 'farmacia ponce').map((c) => c.publicId)).toEqual(['c'])
    expect(matchConsignees(PLACES, 'AV PRINCIPAL').map((c) => c.publicId)).toEqual(['b'])
    expect(matchConsignees(PLACES, '   ')).toEqual([])
    expect(matchConsignees(PLACES, 'zzz')).toEqual([])
  })

  it('la fila siguiente conserva la fecha y deja lo demás en blanco', () => {
    const prev = { ...emptyQuickRow('2026-10-23'), consigneeId: 'a', cod: '10', notes: 'x', pieces: '3' }
    expect(nextQuickRow(prev)).toEqual(emptyQuickRow('2026-10-23'))
  })

  it('sin consignatario no se guarda y el foco va al consignatario', () => {
    const errors = validateQuickRow(emptyQuickRow('2026-10-22'), TODAY)
    expect(errors).toContain('consignee')
    expect(firstErrorField(errors)).toBe('consignee')
  })

  it('con consignatario elegido, piezas y fecha válidas no hay errores', () => {
    expect(validateQuickRow({ ...emptyQuickRow('2026-10-22'), consigneeId: 'a' }, TODAY)).toEqual([])
  })

  it('valida piezas, peso, COD y fecha como «Nuevo envío»', () => {
    const base = { ...emptyQuickRow('2026-10-22'), consigneeId: 'a' }
    expect(validateQuickRow({ ...base, pieces: '0' }, TODAY)).toContain('pieces')
    expect(validateQuickRow({ ...base, weightKg: 'abc' }, TODAY)).toContain('weight')
    expect(validateQuickRow({ ...base, cod: 'x' }, TODAY)).toContain('cod')
    expect(validateQuickRow({ ...base, date: '2026-10-01' }, TODAY)).toContain('date')
    expect(firstErrorField(validateQuickRow({ ...base, pieces: '0', cod: 'x', date: '2026-10-01' }, TODAY))).toBe('date')   // el primero en el orden de la fila
  })

  it('un consignatario nuevo pide nombre, dirección y pueblo', () => {
    const row = { ...emptyQuickRow('2026-10-22'), newConsignee: { ...emptyConsignee(), name: 'Tienda Nueva' } }
    expect(validateQuickRow(row, TODAY)).toContain('consignee')
    expect(validateQuickRow({ ...row, newConsignee: { ...emptyConsignee(), name: 'Tienda Nueva', line1: 'Calle 5', city: 'Caguas' } }, TODAY)).toEqual([])
  })

  it('arma la solicitud: un paquete, el consignatario elegido, COD y factura solo si el cliente numera', () => {
    const row = { ...emptyQuickRow('2026-10-22'), consigneeId: 'a', description: 'Cajas', pieces: '2', weightKg: '3,5', cod: '25.5', invoice: 'F-9', notes: 'frágil' }
    expect(toDraft(row).packages).toEqual([{ description: 'Cajas', pieces: '2', weightKg: '3,5' }])
    const req = quickRequest(row, true)
    expect(req).toMatchObject({ consigneePublicId: 'a', codAmount: 25.5, clientInvoiceNumber: 'F-9', notes: 'frágil', requestedDate: '2026-10-22T00:00:00Z' })
    expect(req.packages).toEqual([{ description: 'Cajas', pieces: 2, weightKg: 3.5 }])
    expect(quickRequest(row, false).clientInvoiceNumber).toBeNull()
  })
})
