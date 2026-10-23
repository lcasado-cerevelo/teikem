// Lote F-E2a — reglas puras de la Sala de despacho: acciones por permiso Y estatus, secuencia ▲ ▼, avisos y bloqueantes, lote de despacho,
// optimización (comparar solo si hay corrida anterior), errores por orden, fechas, formularios con los mensajes del servidor y paridad es/en.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t, translate } from '../../kernel/i18n'
import enDict from '../../kernel/i18n/en.json'
import esDict from '../../kernel/i18n/es.json'
import {
  addDays,
  attendedStops,
  batchSummary,
  blockingIssues,
  buildHeaderPatch,
  buildPlanDayRequest,
  buildReassignRequest,
  buildTripCreate,
  buildUnassignedQuery,
  durationText,
  emptyTripCreateValues,
  hasUnassignedFilters,
  isSelectable,
  maxStopsSource,
  showOverStopLimit,
  tripAwaitsPins,
  PIN_POLL_MAX_UPDATES,
  moveStop,
  NO_ACTIONS,
  optimizeDelta,
  orderedStops,
  orderProblems,
  percent,
  planDateIssue,
  planDateRange,
  planDaySchema,
  problemMessage,
  reassignSchema,
  selectableIds,
  selectedWithWarnings,
  sortedIssues,
  stopIds,
  stopsLabel,
  tripActionsOf,
  tripCreateSchema,
  unassignedMatches,
  warningIssues,
  type DispatchableTrip,
  type RouteStop,
  type TripDetail,
  type TripIssue,
} from './dispatchRules'

beforeAll(() => setLang('es'))

const ALL = { plan: true, optimize: true, dispatch: true, close: true }
const NONE = { plan: false, optimize: false, dispatch: false, close: false }
const draft = (over: Partial<TripDetail> = {}): TripDetail => ({ statusCode: 'DRAFT', isEditable: true, canEditHeader: true, ...over })

describe('acciones por permiso Y estatus', () => {
  it('una ruta abierta (borrador o planificada) se edita, optimiza, elimina y cambia de chofer; no se inicia ni se cierra', () => {
    for (const statusCode of ['DRAFT', 'PLANNED']) {
      expect(tripActionsOf(draft({ statusCode }), ALL)).toEqual({ edit: true, editHeader: true, optimize: true, remove: true, start: false, close: false })
    }
  })

  it('despachada: solo «Iniciar ruta» (y el chofer o vehículo solo si el servidor deja editar la cabecera)', () => {
    expect(tripActionsOf({ statusCode: 'DISPATCHED', isEditable: false, canEditHeader: false }, ALL)).toEqual({ ...NO_ACTIONS, start: true })
    expect(tripActionsOf({ statusCode: 'DISPATCHED', isEditable: false, canEditHeader: true }, ALL)).toEqual({ ...NO_ACTIONS, editHeader: true, start: true })
  })

  it('en curso: solo «Cerrar ruta»; completada y cancelada: nada', () => {
    expect(tripActionsOf({ statusCode: 'IN_PROGRESS', isEditable: false, canEditHeader: false }, ALL)).toEqual({ ...NO_ACTIONS, close: true })
    expect(tripActionsOf({ statusCode: 'COMPLETED', isEditable: false, canEditHeader: false }, ALL)).toEqual(NO_ACTIONS)
    expect(tripActionsOf({ statusCode: 'CANCELLED', isEditable: false, canEditHeader: false }, ALL)).toEqual(NO_ACTIONS)
  })

  it('cada acción exige SU permiso: sin ninguno no hay acciones; con uno solo, solo las suyas', () => {
    expect(tripActionsOf(draft(), NONE)).toEqual(NO_ACTIONS)
    expect(tripActionsOf(draft(), { ...NONE, plan: true })).toEqual({ ...NO_ACTIONS, edit: true, editHeader: true, remove: true })
    expect(tripActionsOf(draft(), { ...NONE, optimize: true })).toEqual({ ...NO_ACTIONS, optimize: true })
    expect(tripActionsOf({ statusCode: 'DISPATCHED', isEditable: false }, { ...NONE, dispatch: true })).toEqual({ ...NO_ACTIONS, start: true })
    expect(tripActionsOf({ statusCode: 'DISPATCHED', isEditable: false }, { ...NONE, close: true })).toEqual(NO_ACTIONS)
    expect(tripActionsOf({ statusCode: 'IN_PROGRESS', isEditable: false }, { ...NONE, close: true })).toEqual({ ...NO_ACTIONS, close: true })
  })

  it('sin ruta no hay acciones', () => {
    expect(tripActionsOf(undefined, ALL)).toEqual(NO_ACTIONS)
    expect(tripActionsOf(null, ALL)).toEqual(NO_ACTIONS)
  })
})

describe('secuencia manual ▲ ▼', () => {
  const stops: RouteStop[] = [
    { id: 30, sequence: 3 },
    { id: 10, sequence: 1 },
    { id: 20, sequence: 2 },
  ]

  it('los ids salen en el orden de la secuencia, no en el del arreglo', () => {
    expect(stopIds(stops)).toEqual([10, 20, 30])
    expect(orderedStops(stops).map((s) => s.id)).toEqual([10, 20, 30])
    expect(stopIds(null)).toEqual([])
  })

  it('subir y bajar intercambian con el vecino y devuelven un arreglo nuevo', () => {
    const ids = [10, 20, 30]
    expect(moveStop(ids, 1, -1)).toEqual([20, 10, 30])
    expect(moveStop(ids, 1, 1)).toEqual([10, 30, 20])
    expect(ids).toEqual([10, 20, 30])
  })

  it('subir la primera o bajar la última no hace nada (null)', () => {
    expect(moveStop([10, 20, 30], 0, -1)).toBeNull()
    expect(moveStop([10, 20, 30], 2, 1)).toBeNull()
    expect(moveStop([10], 0, 1)).toBeNull()
    expect(moveStop([], 0, 1)).toBeNull()
    expect(moveStop([10, 20], 5, -1)).toBeNull()
  })
})

describe('avisos, bloqueantes y despacho', () => {
  const issues: TripIssue[] = [
    { code: 'LATE_WINDOWS', message: 'tarde', blocking: false },
    { code: 'NO_VEHICLE', message: 'sin vehículo', blocking: true },
    { code: 'NO_PLANNED_START', message: 'sin salida', blocking: false },
    { code: 'NO_DRIVER', message: 'sin chofer', blocking: true },
  ]

  it('primero los que bloquean y dentro de cada grupo el orden del servidor', () => {
    expect(sortedIssues(issues).map((i) => i.code)).toEqual(['NO_VEHICLE', 'NO_DRIVER', 'LATE_WINDOWS', 'NO_PLANNED_START'])
    expect(blockingIssues(issues)).toHaveLength(2)
    expect(warningIssues(issues)).toHaveLength(2)
    expect(sortedIssues(undefined)).toEqual([])
  })

  const trip = (publicId: string, canDispatch: boolean, list: TripIssue[] = []): DispatchableTrip => ({ trip: { publicId, code: publicId }, canDispatch, issues: list })

  it('una ruta se puede marcar solo si el servidor dice que se puede despachar y no trae bloqueantes', () => {
    expect(isSelectable(trip('a', true))).toBe(true)
    expect(isSelectable(trip('a', true, [{ code: 'LATE_WINDOWS', blocking: false }]))).toBe(true)
    expect(isSelectable(trip('a', false, [{ code: 'NO_DRIVER', blocking: true }]))).toBe(false)
    expect(isSelectable(trip('a', true, [{ code: 'NO_DRIVER', blocking: true }]))).toBe(false)
    expect(selectableIds([trip('a', true), trip('b', false), trip('c', true)])).toEqual(['a', 'c'])
  })

  it('las rutas elegidas con avisos piden confirmación; las limpias no', () => {
    const items = [trip('a', true), trip('b', true, [{ code: 'OVER_WEIGHT', blocking: false }])]
    expect(selectedWithWarnings(items, ['a']).map((i) => i.trip?.publicId)).toEqual([])
    expect(selectedWithWarnings(items, ['a', 'b']).map((i) => i.trip?.publicId)).toEqual(['b'])
  })

  it('resumen del lote: despachadas y falladas', () => {
    expect(batchSummary([{ dispatched: true }, { dispatched: false, error: 'x' }, { dispatched: true }])).toEqual({ requested: 3, dispatched: 2, failed: 1 })
    expect(batchSummary([])).toEqual({ requested: 0, dispatched: 0, failed: 0 })
  })

  it('el aviso de máximo de paradas solo sale mientras la ruta se arma (borrador o planificada)', () => {
    expect(showOverStopLimit(true, 'DRAFT')).toBe(true)
    expect(showOverStopLimit(true, 'planned')).toBe(true)
    for (const code of ['DISPATCHED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', null, undefined]) expect(showOverStopLimit(true, code)).toBe(false)
    expect(showOverStopLimit(false, 'DRAFT')).toBe(false)
    expect(showOverStopLimit(undefined, 'DRAFT')).toBe(false)
  })

  it('máximo de paradas: override del chofer o default de la compañía; sin datos del chofer no se afirma nada', () => {
    expect(maxStopsSource(25, true)).toBe('driver')
    expect(maxStopsSource(null, true)).toBe('tenant')
    expect(maxStopsSource(undefined, false)).toBeNull()
  })
})

describe('progreso y optimización', () => {
  it('paradas atendidas = completadas o falladas', () => {
    expect(attendedStops([{ statusCode: 'COMPLETED' }, { statusCode: 'FAILED' }, { statusCode: 'PENDING' }, { statusCode: 'completed' }])).toBe(3)
    expect(attendedStops(undefined)).toBe(0)
    expect(percent(1, 4)).toBe(25)
    expect(percent(0, 0)).toBe(0)
    expect(percent(9, 4)).toBe(100)
  })

  it('compara con la corrida anterior SOLO si existe (la primera vez no inventa un porcentaje)', () => {
    expect(optimizeDelta([])).toBeNull()
    expect(optimizeDelta([{ statusCode: 'OK', totalDistanceKm: 40, totalDurationMin: 90 }])).toBeNull()
    expect(
      optimizeDelta([
        { statusCode: 'OK', totalDistanceKm: 44, totalDurationMin: 82 },
        { statusCode: 'OK', totalDistanceKm: 50, totalDurationMin: 90 },
      ]),
    ).toEqual({ distancePct: -12, durationMin: -8 })
  })

  it('las corridas con error o sin distancia no cuentan como anterior', () => {
    expect(
      optimizeDelta([
        { statusCode: 'OK', totalDistanceKm: 44, totalDurationMin: 82 },
        { statusCode: 'ERROR', totalDistanceKm: null },
        { statusCode: 'OK', totalDistanceKm: 50, totalDurationMin: 90 },
      ]),
    ).toEqual({ distancePct: -12, durationMin: -8 })
    expect(optimizeDelta([{ statusCode: 'OK', totalDistanceKm: 44 }, { statusCode: 'PENDING' }])).toBeNull()
    expect(optimizeDelta([{ statusCode: 'OK', totalDistanceKm: 44 }, { statusCode: 'OK', totalDistanceKm: 0 }])).toEqual({ distancePct: null, durationMin: null })
  })

  it('sin coordenadas el servidor no calcula distancia: se compara solo la duración', () => {
    expect(optimizeDelta([{ statusCode: 'OK', totalDurationMin: 25 }, { statusCode: 'OK', totalDurationMin: 30 }])).toEqual({ distancePct: null, durationMin: -5 })
    expect(optimizeDelta([{ statusCode: 'OK' }, { statusCode: 'OK' }])).toEqual({ distancePct: null, durationMin: null })
  })

  it('duración legible', () => {
    expect(durationText(80)).toBe('1 h 20 min')
    expect(durationText(45)).toBe('45 min')
    expect(durationText(120)).toBe('2 h')
    expect(durationText(null)).toBeNull()
  })
})

describe('errores del servidor', () => {
  it('422 al agregar: una fila por orden y motivo', () => {
    expect(orderProblems({ 'OT-1': ['La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.'], 'OT-2': ['a', 'b'] })).toEqual([
      { number: 'OT-1', reason: 'La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.' },
      { number: 'OT-2', reason: 'a' },
      { number: 'OT-2', reason: 'b' },
    ])
    expect(orderProblems(null)).toEqual([])
  })

  it('un 400 de validación muestra el error del campo; lo demás, el título tal cual', () => {
    expect(problemMessage({ title: 'Uno o más errores', code: 'validation', errors: { planDate: ['Indique la fecha de la ruta.'] } })).toBe('Indique la fecha de la ruta.')
    expect(problemMessage({ title: 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', code: 'conflict', errors: {} })).toBe('El registro fue modificado por otro usuario; recargue e intente de nuevo.')
    expect(problemMessage({ title: 'La ruta 2026-0001 no se puede despachar: x.', code: 'status_rule', errors: { NO_DRIVER: ['x'] } })).toBe('La ruta 2026-0001 no se puede despachar: x.')
    expect(problemMessage({ title: 'T', code: 'validation', errors: {} })).toBe('T')
  })

  it('«1 parada» / «N paradas»', () => {
    expect(stopsLabel(t, 1)).toBe('1 parada')
    expect(stopsLabel(t, 0)).toBe('0 paradas')
    expect(stopsLabel(t, 14)).toBe('14 paradas')
  })
})

describe('fechas y formularios (mensajes del servidor)', () => {
  const today = '2026-10-12'

  it('de ayer a hoy + 60 días', () => {
    expect(addDays('2026-10-12', 1)).toBe('2026-10-13')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28')
    expect(planDateRange(today)).toEqual({ min: '2026-10-11', max: '2026-12-11' })
  })

  it('fecha vacía o fuera de rango: el mensaje exacto del manual 05 §1', () => {
    expect(planDateIssue('', today, t)).toBe('Indique la fecha de la ruta.')
    expect(planDateIssue('2026-10-10', today, t)).toBe('La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.')
    expect(planDateIssue('2026-12-12', today, t)).toBe('La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.')
    expect(planDateIssue('2026-10-11', today, t)).toBeNull()
    expect(planDateIssue('2026-12-11', today, t)).toBeNull()
  })

  it('«Nueva ruta»: valida la fecha y la hora, y no manda lo vacío', () => {
    const schema = tripCreateSchema(t, today)
    expect(schema.safeParse(emptyTripCreateValues('2026-10-12')).success).toBe(true)
    const bad = schema.safeParse({ ...emptyTripCreateValues('2026-10-01'), plannedStartUtc: '8' })
    expect(bad.success).toBe(false)
    expect(bad.error?.issues.map((i) => i.message)).toEqual(['La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.', 'Indique la hora de salida con hora y minutos.'])
    expect(buildTripCreate(emptyTripCreateValues('2026-10-12'))).toEqual({ planDate: '2026-10-12' })
    expect(buildTripCreate({ ...emptyTripCreateValues('2026-10-12'), kind: 'PICKUP' })).toEqual({ planDate: '2026-10-12', kind: 'PICKUP' })
    expect(buildTripCreate({ planDate: '2026-10-12', dispatchZoneId: '3', driverPublicId: 'd-1', vehiclePublicId: 'v-1', plannedStartUtc: '08:30', kind: 'DELIVERY' })).toEqual({
      planDate: '2026-10-12',
      dispatchZoneId: 3,
      driverPublicId: 'd-1',
      vehiclePublicId: 'v-1',
      plannedStartUtc: '2026-10-12T12:30:00.000Z',
    })
  })

  it('cambiar chofer o vehículo: el valor nuevo o clear*, siempre con rowVersion', () => {
    expect(buildHeaderPatch({ driver: 'd-2' }, 'rv')).toEqual({ driverPublicId: 'd-2', rowVersion: 'rv' })
    expect(buildHeaderPatch({ driver: '' }, 'rv')).toEqual({ clearDriver: true, rowVersion: 'rv' })
    expect(buildHeaderPatch({ vehicle: 'v-2' }, null)).toEqual({ vehiclePublicId: 'v-2', rowVersion: null })
    expect(buildHeaderPatch({ vehicle: '' }, undefined)).toEqual({ clearVehicle: true, rowVersion: null })
  })

  it('«Planificar el día»: sin zonas = todas (null); más de 50 zonas no pasa', () => {
    const schema = planDaySchema(t, today)
    expect(schema.safeParse({ planDate: today, zoneIds: [], createEmptyTrips: false }).success).toBe(true)
    const many = schema.safeParse({ planDate: today, zoneIds: Array.from({ length: 51 }, (_, i) => String(i)), createEmptyTrips: false })
    expect(many.error?.issues[0].message).toBe('Máximo 50 zonas por planificación.')
    expect(buildPlanDayRequest({ planDate: today, zoneIds: [], createEmptyTrips: true })).toEqual({ planDate: today, dispatchZoneIds: null, createEmptyTrips: true })
    expect(buildPlanDayRequest({ planDate: today, zoneIds: ['2', '5'], createEmptyTrips: false })).toEqual({ planDate: today, dispatchZoneIds: [2, 5], createEmptyTrips: false })
  })

  it('«Reasignar por zona»: al menos una zona y un chofer', () => {
    const schema = reassignSchema(t, today)
    const res = schema.safeParse({ planDate: today, zoneIds: [], driverPublicId: '' })
    expect(res.error?.issues.map((i) => i.message)).toEqual(['Indique al menos una zona.', 'Indique el chofer.'])
    expect(schema.safeParse({ planDate: today, zoneIds: ['1'], driverPublicId: 'd' }).success).toBe(true)
    const many = schema.safeParse({ planDate: today, zoneIds: Array.from({ length: 51 }, (_, i) => String(i)), driverPublicId: 'd' })
    expect(many.error?.issues[0].message).toBe('Máximo 50 zonas por reasignación.')
    expect(buildReassignRequest({ planDate: today, zoneIds: ['1', '2'], driverPublicId: 'd' })).toEqual({ planDate: today, dispatchZoneIds: [1, 2], driverPublicId: 'd' })
  })
})

describe('filtros de «Órdenes sin asignar»', () => {
  it('al servidor: CP, pueblo, cliente y zona (o «sin zona»), con Take 500', () => {
    expect(buildUnassignedQuery({ zone: '', postalCode: '', city: '' }, null)).toEqual({ Skip: 0, Take: 500 })
    expect(buildUnassignedQuery({ zone: '7', postalCode: ' 009 ', city: ' Bayamón ' }, 'c-1')).toEqual({ DispatchZoneId: 7, PostalCode: '009', City: 'Bayamón', ClientPublicId: 'c-1', Skip: 0, Take: 500 })
    expect(buildUnassignedQuery({ zone: 'none', postalCode: '', city: '' }, null)).toEqual({ NoZone: true, Skip: 0, Take: 500 })
    expect(hasUnassignedFilters({ zone: '', postalCode: ' ', city: '' }, null)).toBe(false)
    expect(hasUnassignedFilters({ zone: 'none', postalCode: '', city: '' }, null)).toBe(true)
    expect(hasUnassignedFilters({ zone: '', postalCode: '', city: '' }, 'c')).toBe(true)
  })

  it('el buscador libre mira número, empaque, factura, cliente, consignatario, pueblo, CP y zona sin acentos', () => {
    const o = { orderNumber: 'OT-0001', packBatchNumber: 'PKG-9', clientInvoiceNumber: 'F-77', clientName: 'Advance', consigneeName: 'Taller Díaz', city: 'Bayamón', postalCode: '00959', zoneCode: 'R-01' }
    for (const q of ['0001', 'pkg-9', 'f-77', 'advance', 'diaz', 'bayamon', '00959', 'r-01', 'taller bayamon']) expect(unassignedMatches(o, q)).toBe(true)
    expect(unassignedMatches(o, 'caguas')).toBe(false)
  })
})

describe('textos', () => {
  function keysOf(obj: unknown, prefix = ''): string[] {
    if (obj === null || typeof obj !== 'object') return [prefix]
    return Object.entries(obj as Record<string, unknown>).flatMap(([k, v]) => keysOf(v, prefix ? `${prefix}.${k}` : k))
  }

  it('dispatch.* tiene las mismas claves en español e inglés', () => {
    expect(keysOf(enDict.dispatch).sort()).toEqual(keysOf(esDict.dispatch).sort())
  })

  it('las claves que usan las reglas existen en los dos idiomas', () => {
    for (const key of ['dispatch.errors.planDateRequired', 'dispatch.errors.planDateRange', 'dispatch.errors.zonesRequired', 'dispatch.errors.driverRequired', 'dispatch.stopsMany']) {
      expect(translate('es', key)).not.toBe(key)
      expect(translate('en', key)).not.toBe(key)
    }
  })
})

describe('espera de pines (2026-10-22)', () => {
  const withPoint = { point: { lat: 18.2, lng: -66.1 } }
  const noPoint = { point: null }
  it('espera mientras alguna parada de una ruta abierta no tenga ubicación', () => {
    expect(tripAwaitsPins({ statusCode: 'DRAFT', stops: [withPoint, noPoint] })).toBe(true)
    expect(tripAwaitsPins({ statusCode: 'DISPATCHED', stops: [noPoint] })).toBe(true)
  })
  it('no espera si todas tienen pin, si no hay paradas o si la ruta está cerrada', () => {
    expect(tripAwaitsPins({ statusCode: 'DRAFT', stops: [withPoint] })).toBe(false)
    expect(tripAwaitsPins({ statusCode: 'DRAFT', stops: [] })).toBe(false)
    expect(tripAwaitsPins({ statusCode: 'COMPLETED', stops: [noPoint] })).toBe(false)
    expect(tripAwaitsPins({ statusCode: 'CANCELLED', stops: [noPoint] })).toBe(false)
    expect(tripAwaitsPins(undefined)).toBe(false)
  })
  it('deja de preguntar después de un rato (Google no la encontró o se llegó al tope mensual)', () => {
    expect(tripAwaitsPins({ statusCode: 'DRAFT', stops: [noPoint] }, PIN_POLL_MAX_UPDATES - 1)).toBe(true)
    expect(tripAwaitsPins({ statusCode: 'DRAFT', stops: [noPoint] }, PIN_POLL_MAX_UPDATES)).toBe(false)
  })
})
