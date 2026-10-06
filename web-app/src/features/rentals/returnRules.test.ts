// Lote F18 (Rentas F-R2) — reglas puras de la devolución, la lista de devoluciones, la cola de proceso y el resumen de la lista de
// rentas: mismos mensajes y criterios que el servidor (manual 11 §5, §6 y §10).
import { describe, expect, it } from 'vitest'
import { translate } from '../../kernel/i18n/i18n'
import type { StatusOption } from '../../kernel/catalogs'
import { EMPTY_RENTAL_FILTERS, summaryCardOf, summaryFilters, type RentalDto } from './rentalRules'
import {
  advanceOptions,
  canRegisterReturn,
  daysInProcess,
  initialReturnLines,
  isEarlyReturn,
  pendingReturnLines,
  processFiltersFromUrl,
  processListQuery,
  returnBody,
  returnFiltersFromUrl,
  returnIssues,
  returnListQuery,
  scrapConfirmed,
  suggestedAdvance,
  type ReturnHeaderDraft,
} from './returnRules'

const rental = (statusCode: string): RentalDto => ({
  rental: { statusCode, startDate: '2026-10-01', pickupDate: '2026-10-20' },
  lines: [
    { id: 1, serialNumber: 'SN-1', sku: 'CAMA', productName: 'Cama', fromBinCode: 'A-01', isActive: true, dispatchedAtUtc: '2026-10-02T10:00:00' },
    { id: 2, serialNumber: 'SN-2', sku: 'CAMA', productName: 'Cama', fromBinCode: 'A-02', isActive: true, dispatchedAtUtc: '2026-10-02T10:00:00', returnedAtUtc: '2026-10-03T10:00:00' },
    { id: 3, serialNumber: 'SN-3', sku: 'CAMA', productName: 'Cama', fromBinCode: 'A-03', isActive: true },
    { id: 4, serialNumber: 'SN-4', sku: 'CAMA', productName: 'Cama', fromBinCode: 'A-04', isActive: false, dispatchedAtUtc: '2026-10-02T10:00:00' },
  ],
})

const header = (patch: Partial<ReturnHeaderDraft> = {}): ReturnHeaderDraft => ({
  returnedOn: '2026-10-05',
  reason: 'EARLY_DAMAGE',
  notes: '',
  estimatedPickupCost: null,
  transportCurrency: '',
  toBinId: null,
  ...patch,
})
const es = (code: string, params?: Record<string, string | number>) => translate('es', `rentalReturns.errors.${code}`, params)

describe('Devolución: equipos pendientes y borrador', () => {
  it('solo los activos, despachados y sin devolver; se registra solo en una renta En renta con alguno', () => {
    expect(pendingReturnLines(rental('ON_RENT')).map((l) => l.serialNumber)).toEqual(['SN-1'])
    expect(canRegisterReturn(rental('ON_RENT'))).toBe(true)
    expect(canRegisterReturn(rental('SCHEDULED'))).toBe(false)
    expect(canRegisterReturn({ ...rental('ON_RENT'), lines: [] })).toBe(false)
  })

  it('borrador: incluidos, condición Buena, sin destino propio y con proceso (sí por defecto)', () => {
    expect(initialReturnLines(rental('ON_RENT'))).toEqual([
      { lineId: 1, serialNumber: 'SN-1', sku: 'CAMA', productName: 'Cama', fromBinCode: 'A-01', include: true, condition: 'GOOD', toBinId: null, requiresProcess: true, notes: '' },
    ])
  })
})

describe('Devolución: validación espejo del servidor (mismos mensajes)', () => {
  const lines = initialReturnLines(rental('ON_RENT'))
  const ctx = { today: '2026-10-05', startDate: '2026-10-01' }
  it('sin motivo, "Otro" sin notas, fecha futura o anterior al inicio, costo negativo y sin equipos', () => {
    const issues = returnIssues(header({ reason: '', returnedOn: '2026-10-06', estimatedPickupCost: -1 }), lines.map((l) => ({ ...l, include: false })), ctx)
    expect(issues.fields.reason).toEqual({ code: 'reasonRequired' })
    expect(issues.fields.returnedOn).toEqual({ code: 'returnedOnFuture' })
    expect(issues.fields.estimatedPickupCost).toEqual({ code: 'negativePickupCost' })
    expect(issues.fields.lines).toEqual({ code: 'noLines' })
    expect(returnIssues(header({ reason: 'OTHER' }), lines, ctx).fields.notes).toEqual({ code: 'otherNeedsNotes' })
    expect(returnIssues(header({ reason: 'other', notes: '  ' }), lines, ctx).fields.notes).toEqual({ code: 'otherNeedsNotes' })
    expect(returnIssues(header({ returnedOn: '2026-09-30' }), lines, ctx).fields.returnedOn).toEqual({ code: 'returnedOnBeforeStart', params: { date: '2026-10-01' } })
    expect(returnIssues(header({ notes: 'x'.repeat(1001) }), lines, ctx).fields.notes).toEqual({ code: 'notesTooLong' })
    expect(returnIssues(header(), [{ ...lines[0], notes: 'x'.repeat(501) }], ctx).lineNotes).toEqual({ 0: { code: 'lineNotesTooLong' } })
    // fecha vacía = hoy (lo decide el servidor) y todo bien
    expect(returnIssues(header({ returnedOn: '' }), lines, ctx)).toEqual({ fields: {}, lineNotes: {} })
  })

  it('los textos en español son los del servidor (RentalRules / RentalReturnService)', () => {
    expect(es('reasonRequired')).toBe('Indique el motivo de la devolución: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER.')
    expect(es('otherNeedsNotes')).toBe("Con el motivo 'Otro' describa la devolución en las notas.")
    expect(es('returnedOnFuture')).toBe('La fecha de devolución no puede ser futura.')
    expect(es('returnedOnBeforeStart', { date: '2026-10-01' })).toBe('La fecha de devolución no puede ser anterior al inicio de la renta (2026-10-01).')
    expect(es('negativePickupCost')).toBe('El costo de recogido estimado no puede ser negativo.')
    expect(es('noLines')).toBe('Indique al menos una serie que se devuelve.')
    expect(es('lineNotesTooLong')).toBe('Las notas del equipo admiten como máximo 500 caracteres.')
    expect(translate('es', 'rentalProcesses.errors.statusRequired')).toBe('Indique el estatus al que pasa el proceso.')
  })

  it('anticipada = antes del recogido vigente (vacía = hoy)', () => {
    expect(isEarlyReturn('2026-10-19', '2026-10-20')).toBe(true)
    expect(isEarlyReturn('2026-10-20', '2026-10-20')).toBe(false)
    expect(isEarlyReturn('', '2026-10-20', '2026-10-05')).toBe(true)
  })

  it('cuerpo: solo los incluidos por serie, destino común y propio, proceso, notas recortadas, moneda solo con costo', () => {
    const l = initialReturnLines({
      ...rental('ON_RENT'),
      lines: [
        { id: 1, serialNumber: 'SN-1', isActive: true, dispatchedAtUtc: 'x' },
        { id: 5, serialNumber: 'SN-5', isActive: true, dispatchedAtUtc: 'x' },
        { id: 6, serialNumber: 'SN-6', isActive: true, dispatchedAtUtc: 'x' },
      ],
    })
    l[1] = { ...l[1], condition: 'DAMAGED', toBinId: 77, requiresProcess: false, notes: ' golpe ' }
    l[2] = { ...l[2], include: false }
    expect(returnBody(header({ notes: ' pantalla ', toBinId: 12, transportCurrency: 'USD' }), l, 'RV')).toEqual({
      reason: 'EARLY_DAMAGE',
      returnedOn: '2026-10-05',
      notes: 'pantalla',
      toBinId: 12,
      estimatedPickupCost: null,
      transportCurrency: null,
      rowVersion: 'RV',
      lines: [
        { serialNumber: 'SN-1', condition: 'GOOD', toBinId: null, requiresProcess: true, notes: null },
        { serialNumber: 'SN-5', condition: 'DAMAGED', toBinId: 77, requiresProcess: false, notes: 'golpe' },
      ],
    })
    expect(returnBody(header({ returnedOn: '', estimatedPickupCost: 30, transportCurrency: 'USD' }), l, null)).toMatchObject({ returnedOn: null, estimatedPickupCost: 30, transportCurrency: 'USD' })
  })
})

describe('Listas: filtros de la URL y consulta al API', () => {
  it('devoluciones: motivo repetible, renta, cliente, fechas válidas, anticipada y búsqueda', () => {
    const f = returnFiltersFromUrl(new URLSearchParams('reason=early_damage,OTHER&reason=bad-code&rentalPublicId=r1&from=2026-10-01&to=mal&early=true&search=SN-1'))
    expect(f).toEqual({ reasons: ['EARLY_DAMAGE', 'OTHER'], clientPublicId: null, rentalPublicId: 'r1', range: { from: '2026-10-01', to: '' }, early: 'true', search: 'SN-1' })
    expect(returnListQuery(f)).toEqual({ reason: ['EARLY_DAMAGE', 'OTHER'], rentalPublicId: 'r1', from: '2026-10-01', early: true, search: 'SN-1' })
    expect(returnListQuery({ ...f, early: 'false', reasons: [], rentalPublicId: null, range: { from: '', to: '' } }, ' ')).toEqual({ early: false })
  })

  it('procesos: abiertos por defecto, terminados (open=false) o todos (sin open); estatus, almacén y búsqueda', () => {
    expect(processFiltersFromUrl(new URLSearchParams(''))).toEqual({ status: [], open: 'open', warehousePublicId: null, search: '' })
    const f = processFiltersFromUrl(new URLSearchParams('status=cleaning&open=all&warehousePublicId=w1&search=SN-9'))
    expect(f).toEqual({ status: ['CLEANING'], open: 'all', warehousePublicId: 'w1', search: 'SN-9' })
    expect(processListQuery(f)).toEqual({ status: ['CLEANING'], warehousePublicId: 'w1', search: 'SN-9' })
    expect(processListQuery({ ...f, open: 'open' })).toMatchObject({ open: true })
    expect(processListQuery(processFiltersFromUrl(new URLSearchParams('open=false')))).toEqual({ open: false })
  })
})

describe('Proceso: días, estatus para avanzar y baja', () => {
  const day = (iso: string | null | undefined) => (iso ? iso.slice(0, 10) : null)
  it('días en proceso: del inicio al fin o a hoy, nunca negativo', () => {
    expect(daysInProcess({ startedAtUtc: '2026-10-01T12:00:00' }, '2026-10-05', day)).toBe(4)
    expect(daysInProcess({ startedAtUtc: '2026-10-01T12:00:00', completedAtUtc: '2026-10-03T08:00:00' }, '2026-10-05', day)).toBe(2)
    expect(daysInProcess({ startedAtUtc: '2026-10-06T12:00:00' }, '2026-10-05', day)).toBe(0)
    expect(daysInProcess({}, '2026-10-05', day)).toBeNull()
  })

  const st = (code: string, stageKind: string, sortOrder: number, isEnabled = true): StatusOption => ({ code, label: code, color: null, stageKind, isInitial: code === 'PENDING', isEnabled, sortOrder, icon: null })
  const statuses = [
    st('PENDING', 'PIPELINE', 10),
    st('INSPECTION', 'PIPELINE', 20),
    st('CLEANING', 'PIPELINE', 30, false),
    st('TESTING', 'PIPELINE', 40),
    st('READY', 'TERMINAL', 50),
    st('REPAIR', 'LATERAL', 60),
    st('AWAITING_PARTS', 'LATERAL', 70),
    st('SCRAPPED', 'TERMINAL', 80),
  ]
  it('avanzar ofrece los habilitados no terminales salvo el actual; sugerido = siguiente paso habilitado', () => {
    expect(advanceOptions(statuses, 'INSPECTION').map((s) => s.code)).toEqual(['PENDING', 'TESTING', 'REPAIR', 'AWAITING_PARTS'])
    expect(suggestedAdvance(statuses, 'INSPECTION')).toBe('TESTING')
    expect(suggestedAdvance(statuses, 'TESTING')).toBe('')
    expect(suggestedAdvance(statuses, 'REPAIR')).toBe('')
  })

  it('la baja se confirma escribiendo la serie (sin distinguir mayúsculas)', () => {
    expect(scrapConfirmed(' sn-9 ', 'SN-9')).toBe(true)
    expect(scrapConfirmed('SN-', 'SN-9')).toBe(false)
    expect(scrapConfirmed('', null)).toBe(false)
  })
})

describe('Resumen de la lista de rentas', () => {
  it('cada tarjeta aplica su filtro y se reconoce como activa solo con ese filtro', () => {
    expect(summaryFilters('onRent')).toEqual({ ...EMPTY_RENTAL_FILTERS, status: ['ON_RENT'] })
    expect(summaryFilters('dueSoon')).toEqual({ ...EMPTY_RENTAL_FILTERS, dueWithinDays: '7' })
    expect(summaryFilters('overdue')).toEqual({ ...EMPTY_RENTAL_FILTERS, overdue: true })
    expect(summaryCardOf(summaryFilters('dueSoon'))).toBe('dueSoon')
    expect(summaryCardOf({ ...summaryFilters('overdue'), dueWithinDays: '7' })).toBeNull()
    expect(summaryCardOf({ ...summaryFilters('onRent'), search: 'x' })).toBeNull()
    expect(summaryCardOf(EMPTY_RENTAL_FILTERS)).toBeNull()
  })
})
