// Lote F-D3 — reglas puras de Mantenimiento y combustible: mensajes EXACTOS del manual 04 (§5 programas, §6 órdenes de trabajo, §7 combustible),
// armado de los requests (alta y PATCH solo de lo cambiado), estado de vencimiento, acciones por estatus, filtros y paridad de textos es/en.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t, translate } from '../../kernel/i18n'
import enDict from '../../kernel/i18n/en.json'
import esDict from '../../kernel/i18n/es.json'
import type { FuelLog, MaintenanceSchedule, MaintenanceTask, WorkOrderDetail } from './maintenanceApi'
import {
  buildCloseRequest,
  buildDueQuery,
  buildFuelCreate,
  buildFuelPatch,
  buildFuelQuery,
  buildScheduleCreate,
  buildSchedulePatch,
  buildTaskRequest,
  buildWorkOrderCreate,
  buildWorkOrderPatch,
  buildWorkOrderQuery,
  closeNeedsOdometer,
  closeSchema,
  closeValuesOf,
  dueChipInfo,
  dueNextText,
  dueRemainingText,
  emptyFuelValues,
  emptyScheduleValues,
  emptyTaskValues,
  emptyWorkOrderCreateValues,
  fuelInstant,
  fuelSchema,
  fuelValuesOf,
  incompleteTasks,
  nextDay,
  scheduleSchema,
  scheduleValuesOf,
  schedulesForVehicle,
  taskSchema,
  taskTotals,
  taskValuesOf,
  workOrderActions,
  workOrderCreateSchema,
  workOrderEditSchema,
  workOrderEditValuesOf,
  type FuelValues,
  type ScheduleValues,
  type TaskValues,
  type WorkOrderCreateValues,
} from './maintenanceRules'

beforeAll(() => setLang('es'))

const TODAY = '2026-10-12'

/** Mensajes por campo (el primero de cada uno). */
function issuesOf(result: { success: boolean; error?: { issues: { path: PropertyKey[]; message: string }[] } }): Record<string, string> {
  return result.success ? {} : Object.fromEntries((result.error?.issues ?? []).map((i) => [String(i.path[0]), i.message]))
}

// ---------------------------------------------------------------- programas
const schedule = (over: Partial<MaintenanceSchedule> = {}): MaintenanceSchedule => ({
  id: 3,
  name: 'Cambio de aceite',
  vehiclePublicId: 'v-1',
  vehicleCode: 'V-01',
  triggerCode: 'MILEAGE',
  intervalKm: 5000,
  lastServiceKm: 45500,
  lastServiceDate: '2026-04-22',
  isActive: true,
  ...over,
})
const sValues = (over: Partial<ScheduleValues> = {}): ScheduleValues => ({ ...scheduleValuesOf(schedule()), ...over })
const sIssues = (v: ScheduleValues, original?: ScheduleValues) => issuesOf(scheduleSchema(t, { today: TODAY, original }).safeParse(v))

describe('scheduleSchema — mensajes exactos del manual 04 §5', () => {
  it('un programa válido por kilometraje, por tiempo o por ambos no tiene errores', () => {
    expect(sIssues(sValues())).toEqual({})
    expect(sIssues(sValues({ trigger: 'TIME', intervalKm: null, intervalDays: 90 }))).toEqual({})
    expect(sIssues(sValues({ trigger: 'BOTH', intervalDays: 90 }))).toEqual({})
  })

  it('nombre obligatorio y de hasta 150 caracteres', () => {
    expect(sIssues(sValues({ name: '  ' })).name).toBe('El nombre del programa es obligatorio.')
    expect(sIssues(sValues({ name: 'x'.repeat(151) })).name).toBe('El nombre del programa admite como máximo 150 caracteres.')
    expect(sIssues(sValues({ name: 'x'.repeat(150) })).name).toBeUndefined()
  })

  it('el programa es de un vehículo O de un tipo: sin ninguno, el mensaje de «no ambos»', () => {
    expect(sIssues(sValues({ vehiclePublicId: '' })).vehiclePublicId).toBe('Indique el vehículo o el tipo de vehículo del programa, no ambos.')
    expect(sIssues(sValues({ target: 'type', vehicleType: '' })).vehicleType).toBe('Indique el vehículo o el tipo de vehículo del programa, no ambos.')
    expect(sIssues(sValues({ target: 'type', vehicleType: 'TRUCK', lastServiceKm: null, lastServiceDate: '' }))).toEqual({})
  })

  it('el disparador es obligatorio', () => {
    expect(sIssues(sValues({ trigger: '' })).trigger).toBe('El disparador del programa es obligatorio.')
  })

  it('kilometraje exige km > 0; tiempo exige días > 0; ambos exigen los dos', () => {
    expect(sIssues(sValues({ intervalKm: null })).intervalKm).toBe('Un programa por kilometraje exige un intervalo en km mayor que 0.')
    expect(sIssues(sValues({ intervalKm: 0 })).intervalKm).toBe('Un programa por kilometraje exige un intervalo en km mayor que 0.')
    expect(sIssues(sValues({ trigger: 'TIME', intervalDays: null })).intervalDays).toBe('Un programa por tiempo exige un intervalo en días mayor que 0.')
    const both = sIssues(sValues({ trigger: 'BOTH', intervalKm: null, intervalDays: null }))
    expect(both.intervalKm).toBe('Un programa por kilometraje exige un intervalo en km mayor que 0.')
    expect(both.intervalDays).toBe('Un programa por tiempo exige un intervalo en días mayor que 0.')
  })

  it('un intervalo capturado siempre es mayor que 0, aunque el disparador no lo use', () => {
    expect(sIssues(sValues({ trigger: 'TIME', intervalKm: -5, intervalDays: 90 })).intervalKm).toBe('El intervalo en km debe ser mayor que 0.')
    expect(sIssues(sValues({ intervalDays: -1 })).intervalDays).toBe('El intervalo en días debe ser mayor que 0.')
  })

  it('los días: entero y hasta 36500 (sin separador de miles)', () => {
    expect(sIssues(sValues({ trigger: 'TIME', intervalDays: 36501 })).intervalDays).toBe('El intervalo en días admite como máximo 36500.')
    expect(sIssues(sValues({ trigger: 'TIME', intervalDays: 36500 })).intervalDays).toBeUndefined()
    expect(sIssues(sValues({ trigger: 'TIME', intervalDays: 2.5 })).intervalDays).toBe('El intervalo en días debe ser un número entero.')
  })

  it('el último servicio: lectura no negativa, fecha no futura y precisión DECIMAL(12,1)', () => {
    expect(sIssues(sValues({ lastServiceKm: -1 })).lastServiceKm).toBe('La lectura del último servicio no puede ser negativa.')
    expect(sIssues(sValues({ lastServiceDate: '2026-10-13' })).lastServiceDate).toBe('La fecha del último servicio no puede ser futura.')
    expect(sIssues(sValues({ lastServiceDate: TODAY })).lastServiceDate).toBeUndefined()
    expect(sIssues(sValues({ intervalKm: 5000.55 })).intervalKm).toBe('El valor admite como máximo 1 decimales y debe ser menor que 100,000,000,000.')
    expect(sIssues(sValues({ lastServiceKm: 5000.25 })).lastServiceKm).toBe('El valor admite como máximo 1 decimales y debe ser menor que 100,000,000,000.')
  })

  it('en un programa por tipo el último servicio no se valida (no se manda)', () => {
    expect(sIssues(sValues({ target: 'type', vehicleType: 'TRUCK', lastServiceKm: -5, lastServiceDate: '2099-01-01' }))).toEqual({})
  })

  it('en la edición, el último servicio guardado no se puede dejar vacío (el PATCH lee null como «no cambiar»)', () => {
    const original = sValues()
    expect(sIssues(sValues({ lastServiceKm: null }), original).lastServiceKm).toBe('Este valor no se puede dejar vacío una vez guardado; escriba otro.')
    expect(sIssues(sValues({ lastServiceDate: '' }), original).lastServiceDate).toBe('Este valor no se puede dejar vacío una vez guardado; escriba otro.')
    expect(sIssues(sValues({ lastServiceKm: 46000 }), original)).toEqual({})
  })
})

describe('programas — alta y PATCH mínimo', () => {
  it('alta por vehículo: manda el vehículo y el último servicio, nunca el tipo', () => {
    expect(buildScheduleCreate({ ...emptyScheduleValues('v-1'), name: '  Aceite ', intervalKm: 5000, lastServiceKm: 45500, lastServiceDate: '2026-04-22' })).toEqual({
      name: 'Aceite',
      vehiclePublicId: 'v-1',
      trigger: 'MILEAGE',
      intervalKm: 5000,
      intervalDays: null,
      lastServiceKm: 45500,
      lastServiceDate: '2026-04-22',
    })
  })

  it('alta por tipo: manda el tipo y no el último servicio ni el vehículo', () => {
    const req = buildScheduleCreate({ ...emptyScheduleValues(), name: 'Revisión', target: 'type', vehicleType: 'TRUCK', trigger: 'TIME', intervalDays: 90, lastServiceKm: 5, lastServiceDate: '2026-01-01' })
    expect(req).toEqual({ name: 'Revisión', vehicleType: 'TRUCK', trigger: 'TIME', intervalKm: null, intervalDays: 90 })
    expect('vehiclePublicId' in req).toBe(false)
  })

  it('PATCH: sin cambios no hay nada que mandar; solo viaja lo cambiado', () => {
    const s = schedule()
    expect(buildSchedulePatch(sValues(), s)).toEqual({ request: {}, changed: false })
    expect(buildSchedulePatch(sValues({ name: ' Nuevo nombre ', intervalKm: 6000 }), s)).toEqual({ request: { name: 'Nuevo nombre', intervalKm: 6000 }, changed: true })
  })

  it('PATCH: un intervalo vaciado va con clearIntervalKm / clearIntervalDays', () => {
    const s = schedule({ triggerCode: 'BOTH', intervalKm: 5000, intervalDays: 90 })
    const plan = buildSchedulePatch(sValues({ trigger: 'TIME', intervalKm: null, intervalDays: 90 }), s)
    expect(plan.request).toEqual({ trigger: 'TIME', clearIntervalKm: true })
    expect(buildSchedulePatch(sValues({ trigger: 'BOTH', intervalKm: 5000, intervalDays: null }), s).request).toEqual({ clearIntervalDays: true })
  })

  it('PATCH: cambiar de objetivo manda el nuevo (vehículo o tipo), nunca los dos', () => {
    const s = schedule()
    expect(buildSchedulePatch(sValues({ target: 'type', vehicleType: 'TRUCK' }), s).request).toEqual({ vehicleType: 'TRUCK' })
    expect(buildSchedulePatch(sValues({ vehiclePublicId: 'v-2' }), s).request).toEqual({ vehiclePublicId: 'v-2' })
    const byType = schedule({ vehiclePublicId: undefined, vehicleCode: undefined, vehicleTypeCode: 'TRUCK', lastServiceKm: undefined, lastServiceDate: undefined })
    expect(buildSchedulePatch({ ...scheduleValuesOf(byType), target: 'vehicle', vehiclePublicId: 'v-1' }, byType).request).toEqual({ vehiclePublicId: 'v-1' })
  })

  it('PATCH: el último servicio solo se manda si cambió y tiene valor', () => {
    const s = schedule()
    expect(buildSchedulePatch(sValues({ lastServiceKm: 47000, lastServiceDate: '2026-05-01' }), s).request).toEqual({ lastServiceKm: 47000, lastServiceDate: '2026-05-01' })
  })

  it('los valores de un programa por tipo', () => {
    const v = scheduleValuesOf(schedule({ vehiclePublicId: undefined, vehicleTypeCode: 'TRUCK' }))
    expect(v.target).toBe('type')
    expect(v.vehicleType).toBe('TRUCK')
  })
})

// ---------------------------------------------------------------- vencimiento
describe('estado de vencimiento (panel «Mantenimiento preventivo»)', () => {
  it('el chip de cada estado y su gravedad', () => {
    expect(dueChipInfo('OK')).toEqual({ tone: 'deliv', key: 'fleet.maintenance.state.OK', severity: 0 })
    expect(dueChipInfo('DUE_SOON').tone).toBe('cap')
    expect(dueChipInfo('OVERDUE')).toEqual({ tone: 'fail', key: 'fleet.maintenance.state.OVERDUE', severity: 3 })
    expect(dueChipInfo('NO_BASELINE').severity).toBe(1)
    expect(dueChipInfo(undefined).key).toBe('fleet.maintenance.state.OK')
    expect(translate('es', dueChipInfo('OK').key)).toBe('Al día')
    expect(translate('es', dueChipInfo('DUE_SOON').key)).toBe('Por vencer')
    expect(translate('es', dueChipInfo('OVERDUE').key)).toBe('Vencido')
    expect(translate('es', dueChipInfo('NO_BASELINE').key)).toBe('Sin historial')
  })

  const fmt = { number: (n: number) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','), date: (iso: string) => iso.split('-').reverse().join('/') }

  it('«Próximo»: el kilometraje, la fecha o los dos', () => {
    expect(dueNextText({ nextDueKm: 50500 }, fmt)).toBe('50,500 km')
    expect(dueNextText({ nextDueDate: '2026-10-02' }, fmt)).toBe('02/10/2026')
    expect(dueNextText({ nextDueKm: 48500, nextDueDate: '2026-10-19' }, fmt)).toBe('48,500 km · 19/10/2026')
    expect(dueNextText({}, fmt)).toBe('—')
  })

  it('lo que falta o lo pasado, en texto claro', () => {
    expect(dueRemainingText({ kmRemaining: 2290 }, t, fmt)).toBe('Faltan 2,290 km')
    expect(dueRemainingText({ kmRemaining: -120 }, t, fmt)).toBe('Pasado por 120 km')
    expect(dueRemainingText({ daysRemaining: 7 }, t, fmt)).toBe('Faltan 7 días')
    expect(dueRemainingText({ daysRemaining: 1 }, t, fmt)).toBe('Falta 1 día')
    expect(dueRemainingText({ daysRemaining: 0 }, t, fmt)).toBe('Vence hoy')
    expect(dueRemainingText({ daysRemaining: -10 }, t, fmt)).toBe('Vencido hace 10 días')
    expect(dueRemainingText({ daysRemaining: -1 }, t, fmt)).toBe('Vencido hace 1 día')
    expect(dueRemainingText({ kmRemaining: 290, daysRemaining: 7 }, t, fmt)).toBe('Faltan 290 km · Faltan 7 días')
    expect(dueRemainingText({}, t, fmt)).toBe('')
  })

  it('la consulta: estado y vehículo solo si se eligieron', () => {
    expect(buildDueQuery({ state: '', vehicle: '' })).toEqual({})
    expect(buildDueQuery({ state: 'OVERDUE', vehicle: 'v-1' })).toEqual({ status: ['OVERDUE'], vehiclePublicId: 'v-1' })
  })
})

// ---------------------------------------------------------------- órdenes de trabajo
const cValues = (over: Partial<WorkOrderCreateValues> = {}): WorkOrderCreateValues => emptyWorkOrderCreateValues({ vehiclePublicId: 'v-1', maintenanceType: 'CORRECTIVE', ...over })
const cIssues = (v: WorkOrderCreateValues) => issuesOf(workOrderCreateSchema(t).safeParse(v))

describe('workOrderCreateSchema — mensajes exactos del manual 04 §6.1', () => {
  it('una orden válida no tiene errores', () => {
    expect(cIssues(cValues())).toEqual({})
  })

  it('el vehículo y el tipo son obligatorios (sin programa)', () => {
    expect(cIssues(emptyWorkOrderCreateValues()).vehiclePublicId).toBe('El vehículo de la orden de trabajo es obligatorio.')
    expect(cIssues(emptyWorkOrderCreateValues({ vehiclePublicId: 'v-1' })).maintenanceType).toBe('El tipo de mantenimiento es obligatorio.')
  })

  it('con programa el tipo es PREVENTIVE: una orden correctiva no se asocia a un programa preventivo', () => {
    expect(cIssues(cValues({ maintenanceType: '', scheduleId: '3' }))).toEqual({})
    expect(cIssues(cValues({ maintenanceType: 'PREVENTIVE', scheduleId: '3' }))).toEqual({})
    expect(cIssues(cValues({ maintenanceType: 'CORRECTIVE', scheduleId: '3' })).maintenanceType).toBe('Una orden correctiva no se asocia a un programa preventivo.')
  })

  it('costos y odómetro: no negativos y con la precisión de la columna', () => {
    expect(cIssues(cValues({ laborCost: -1 })).laborCost).toBe('Los costos no pueden ser negativos.')
    expect(cIssues(cValues({ partsCost: -0.01 })).partsCost).toBe('Los costos no pueden ser negativos.')
    expect(cIssues(cValues({ odometerKm: -3 })).odometerKm).toBe('El odómetro no puede ser negativo.')
    expect(cIssues(cValues({ laborCost: 1.23456 })).laborCost).toBe('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000,000,000.')
    expect(cIssues(cValues({ odometerKm: 10.25 })).odometerKm).toBe('El valor admite como máximo 1 decimales y debe ser menor que 100,000,000,000.')
  })

  it('proveedor de hasta 150 caracteres', () => {
    expect(cIssues(cValues({ vendor: 'x'.repeat(151) })).vendor).toBe('El proveedor admite como máximo 150 caracteres.')
    expect(cIssues(cValues({ vendor: 'x'.repeat(150) })).vendor).toBeUndefined()
  })

  it('el request: con programa manda scheduleId y PREVENTIVE; lo vacío no se manda', () => {
    expect(buildWorkOrderCreate(cValues())).toEqual({ vehiclePublicId: 'v-1', maintenanceType: 'CORRECTIVE' })
    expect(buildWorkOrderCreate(cValues({ maintenanceType: '', scheduleId: '3', scheduledDate: '2026-10-15', vendor: ' Taller Díaz ', odometerKm: 48210, laborCost: 0, partsCost: 25.5, notes: ' ok ' }))).toEqual({
      vehiclePublicId: 'v-1',
      maintenanceType: 'PREVENTIVE',
      scheduleId: 3,
      scheduledDate: '2026-10-15',
      odometerKm: 48210,
      vendor: 'Taller Díaz',
      laborCost: 0,
      partsCost: 25.5,
      notes: 'ok',
    })
  })

  it('programas elegibles: los activos del vehículo o de su tipo', () => {
    const list: MaintenanceSchedule[] = [
      schedule({ id: 1 }),
      schedule({ id: 2, vehiclePublicId: 'v-2' }),
      schedule({ id: 3, vehiclePublicId: undefined, vehicleCode: undefined, vehicleTypeCode: 'TRUCK' }),
      schedule({ id: 4, vehiclePublicId: undefined, vehicleCode: undefined, vehicleTypeCode: 'VAN' }),
      schedule({ id: 5, isActive: false }),
    ]
    expect(schedulesForVehicle(list, { publicId: 'v-1', vehicleTypeCode: 'VAN' }).map((s) => s.id)).toEqual([1, 4])
    expect(schedulesForVehicle(list, { publicId: 'v-9', vehicleTypeCode: 'TRUCK' }).map((s) => s.id)).toEqual([3])
    expect(schedulesForVehicle(list, undefined)).toEqual([])
  })
})

const order = (over: Partial<WorkOrderDetail> = {}): WorkOrderDetail => ({
  id: 102,
  publicId: 'wo-2',
  number: 'OT-00002',
  vehiclePublicId: 'v-1',
  vehicleCode: 'V-01',
  statusCode: 'OPEN',
  status: 'Abierta',
  isTerminal: false,
  scheduledDate: '2026-10-12',
  odometerKm: 48000,
  vendor: 'Taller Díaz',
  laborCost: 80,
  partsCost: 210,
  totalCost: 290,
  notes: 'Frenos',
  costsFromTasks: false,
  canEdit: true,
  tasks: [],
  rowVersion: 'R2',
  ...over,
})
const eIssues = (v: ReturnType<typeof workOrderEditValuesOf>, w: WorkOrderDetail) => issuesOf(workOrderEditSchema(t, { original: workOrderEditValuesOf(w), costsFromTasks: w.costsFromTasks === true }).safeParse(v))

describe('ficha de la orden — edición (manual 04 §6.3)', () => {
  it('PATCH mínimo con rowVersion; sin cambios no hay nada que mandar', () => {
    const w = order()
    expect(buildWorkOrderPatch(workOrderEditValuesOf(w), w)).toEqual({ request: { rowVersion: 'R2' }, changed: false })
    const v = { ...workOrderEditValuesOf(w), vendor: ' AutoZone ', odometerKm: 48100, notes: '' }
    expect(buildWorkOrderPatch(v, w)).toEqual({ request: { rowVersion: 'R2', odometerKm: 48100, vendor: 'AutoZone', notes: '' }, changed: true })
  })

  it('con tareas activas los costos del encabezado nunca se mandan (serían 400)', () => {
    const w = order({ costsFromTasks: true })
    const v = { ...workOrderEditValuesOf(w), laborCost: 999, partsCost: 999, vendor: 'Otro' }
    expect(buildWorkOrderPatch(v, w).request).toEqual({ rowVersion: 'R2', vendor: 'Otro' })
    expect(eIssues(v, w)).toEqual({})
  })

  it('sin tareas los costos se validan y se mandan si cambian', () => {
    const w = order()
    expect(eIssues({ ...workOrderEditValuesOf(w), laborCost: -2 }, w).laborCost).toBe('Los costos no pueden ser negativos.')
    expect(buildWorkOrderPatch({ ...workOrderEditValuesOf(w), laborCost: 100 }, w).request).toEqual({ rowVersion: 'R2', laborCost: 100 })
  })

  it('un valor guardado (fecha, odómetro, costos) no se puede dejar vacío; los textos sí', () => {
    const w = order()
    const base = workOrderEditValuesOf(w)
    expect(eIssues({ ...base, scheduledDate: '' }, w).scheduledDate).toBe('Este valor no se puede dejar vacío una vez guardado; escriba otro.')
    expect(eIssues({ ...base, odometerKm: null }, w).odometerKm).toBe('Este valor no se puede dejar vacío una vez guardado; escriba otro.')
    expect(eIssues({ ...base, laborCost: null }, w).laborCost).toBe('Este valor no se puede dejar vacío una vez guardado; escriba otro.')
    expect(eIssues({ ...base, vendor: '', notes: '' }, w)).toEqual({})
    expect(eIssues({ ...base, vendor: 'x'.repeat(151) }, w).vendor).toBe('El proveedor admite como máximo 150 caracteres.')
  })
})

describe('acciones por estatus (manual 04 §6.4 y §10.3)', () => {
  it('OPEN: iniciar, cerrar o cancelar; IN_PROGRESS: cerrar o cancelar; terminal: ninguna', () => {
    expect(workOrderActions({ statusCode: 'OPEN', isTerminal: false })).toEqual(['start', 'close', 'cancel'])
    expect(workOrderActions({ statusCode: 'IN_PROGRESS', isTerminal: false })).toEqual(['close', 'cancel'])
    expect(workOrderActions({ statusCode: 'CLOSED', isTerminal: true })).toEqual([])
    expect(workOrderActions({ statusCode: 'CANCELLED', isTerminal: true })).toEqual([])
    expect(workOrderActions({ statusCode: 'OPEN', isTerminal: true })).toEqual([])
  })

  it('cerrar exige odómetro cuando el programa es por kilometraje o ambos', () => {
    expect(closeNeedsOdometer({ scheduleTriggerCode: 'MILEAGE' })).toBe(true)
    expect(closeNeedsOdometer({ scheduleTriggerCode: 'BOTH' })).toBe(true)
    expect(closeNeedsOdometer({ scheduleTriggerCode: 'TIME' })).toBe(false)
    expect(closeNeedsOdometer({ scheduleTriggerCode: null })).toBe(false)
  })
})

describe('cerrar la orden', () => {
  const cl = (v: ReturnType<typeof closeValuesOf>, needsOdometer: boolean) => issuesOf(closeSchema(t, { today: TODAY, needsOdometer }).safeParse(v))

  it('fecha de cierre no futura', () => {
    expect(cl({ completedDate: '2026-10-13', odometerKm: null, comment: '' }, false).completedDate).toBe('La fecha de cierre no puede ser futura.')
    expect(cl({ completedDate: TODAY, odometerKm: null, comment: '' }, false)).toEqual({})
  })

  it('odómetro obligatorio en programas por kilometraje; no negativo y con precisión', () => {
    expect(cl({ completedDate: TODAY, odometerKm: null, comment: '' }, true).odometerKm).toBe('Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.')
    expect(cl({ completedDate: TODAY, odometerKm: -1, comment: '' }, false).odometerKm).toBe('El odómetro no puede ser negativo.')
    expect(cl({ completedDate: TODAY, odometerKm: 5200, comment: '' }, true)).toEqual({})
  })

  it('el comentario admite hasta 500 caracteres', () => {
    expect(cl({ completedDate: TODAY, odometerKm: null, comment: 'x'.repeat(501) }, false).comment).toBe('El comentario admite como máximo 500 caracteres.')
  })

  it('valores de partida (hoy y el odómetro ya capturado) y request', () => {
    expect(closeValuesOf({ odometerKm: 48000 }, TODAY)).toEqual({ completedDate: TODAY, odometerKm: 48000, comment: '' })
    expect(buildCloseRequest({ completedDate: TODAY, odometerKm: 5200, comment: ' ok ' }, 'R2')).toEqual({ toCode: 'CLOSED', completedDate: TODAY, odometerKm: 5200, comment: 'ok', rowVersion: 'R2' })
    expect(buildCloseRequest({ completedDate: '', odometerKm: null, comment: '' }, null)).toEqual({ toCode: 'CLOSED', rowVersion: null })
  })
})

describe('tareas (manual 04 §6.2)', () => {
  const tv = (over: Partial<TaskValues> = {}): TaskValues => ({ ...emptyTaskValues(), description: 'Cambio de aceite', ...over })
  const tIssues = (v: TaskValues) => issuesOf(taskSchema(t).safeParse(v))

  it('descripción obligatoria y de hasta 250 caracteres', () => {
    expect(tIssues(tv({ description: '   ' })).description).toBe('La descripción de la tarea es obligatoria.')
    expect(tIssues(tv({ description: 'x'.repeat(251) })).description).toBe('La descripción de la tarea admite como máximo 250 caracteres.')
    expect(tIssues(tv({ description: 'x'.repeat(250) }))).toEqual({})
  })

  it('costos no negativos y con precisión', () => {
    expect(tIssues(tv({ partCost: -1 })).partCost).toBe('Los costos no pueden ser negativos.')
    expect(tIssues(tv({ laborCost: -1 })).laborCost).toBe('Los costos no pueden ser negativos.')
    expect(tIssues(tv({ laborCost: 0.12345 })).laborCost).toBe('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000,000,000.')
  })

  it('alta con lo capturado; edición solo con lo cambiado', () => {
    expect(buildTaskRequest(tv({ partCost: 40, laborCost: 20 }))).toEqual({ description: 'Cambio de aceite', partCost: 40, laborCost: 20 })
    expect(buildTaskRequest(tv())).toEqual({ description: 'Cambio de aceite' })
    const x: MaintenanceTask = { id: 1, description: 'Cambio de aceite', partCost: 40, laborCost: 20, isCompleted: false, isActive: true }
    expect(buildTaskRequest(taskValuesOf(x), taskValuesOf(x))).toEqual({})
    expect(buildTaskRequest(tv({ description: ' Filtro ', partCost: 40, laborCost: 25 }), taskValuesOf(x))).toEqual({ description: 'Filtro', laborCost: 25 })
  })

  it('totales de las tareas activas y cuántas faltan por completar', () => {
    const list: MaintenanceTask[] = [
      { id: 1, description: 'a', partCost: 40, laborCost: 20, isCompleted: true, isActive: true },
      { id: 2, description: 'b', partCost: null, laborCost: 15.5, isCompleted: false, isActive: true },
      { id: 3, description: 'c', partCost: 999, laborCost: 999, isCompleted: false, isActive: false },
    ]
    expect(taskTotals(list)).toEqual({ labor: 35.5, parts: 40, total: 75.5 })
    expect(incompleteTasks(list)).toBe(1)
    expect(taskTotals(undefined)).toEqual({ labor: 0, parts: 0, total: 0 })
    expect(incompleteTasks(null)).toBe(0)
  })
})

describe('lista de órdenes', () => {
  it('la consulta lleva solo lo filtrado (fechas inclusivas, estados y tipo en arreglo)', () => {
    expect(buildWorkOrderQuery({ statuses: [], type: '', vehicle: '', from: '', to: '' })).toEqual({})
    expect(buildWorkOrderQuery({ statuses: ['OPEN', 'IN_PROGRESS'], type: 'CORRECTIVE', vehicle: 'v-1', from: '2026-10-01', to: '2026-10-31' })).toEqual({
      status: ['OPEN', 'IN_PROGRESS'],
      maintenanceType: ['CORRECTIVE'],
      vehiclePublicId: 'v-1',
      from: '2026-10-01',
      to: '2026-10-31',
    })
  })
})

// ---------------------------------------------------------------- combustible
const NOW = Date.parse('2026-10-12T15:00:00Z') // 11:00 en Puerto Rico
const fValues = (over: Partial<FuelValues> = {}): FuelValues => ({
  vehiclePublicId: 'v-1',
  date: '2026-10-12',
  time: '09:30',
  liters: 40,
  totalCost: 60,
  odometerKm: 5200,
  driverPublicId: '',
  station: '',
  ...over,
})
const fIssues = (v: FuelValues, creating = true) => issuesOf(fuelSchema(t, { creating, now: () => NOW }).safeParse(v))

describe('fuelSchema — mensajes exactos del manual 04 §7', () => {
  it('una carga válida no tiene errores', () => {
    expect(fIssues(fValues())).toEqual({})
  })

  it('el vehículo es obligatorio al registrar; al editar no se valida (no cambia)', () => {
    expect(fIssues(fValues({ vehiclePublicId: '' })).vehiclePublicId).toBe('El vehículo de la carga es obligatorio.')
    expect(fIssues(fValues({ vehiclePublicId: '' }), false)).toEqual({})
  })

  it('fecha obligatoria y no futura (5 minutos de tolerancia, hora de la compañía)', () => {
    expect(fIssues(fValues({ date: '' })).date).toBe('La fecha de la carga es obligatoria.')
    expect(fIssues(fValues({ time: '' })).time).toBe('La hora de la carga es obligatoria.')
    expect(fIssues(fValues({ date: '2026-10-13' })).date).toBe('La fecha de la carga no puede ser futura.')
    expect(fIssues(fValues({ time: '11:10' })).date).toBe('La fecha de la carga no puede ser futura.') // 10 min adelante
    expect(fIssues(fValues({ time: '11:03' })).date).toBeUndefined() // dentro de la tolerancia
  })

  it('los litros deben ser mayores que 0 y con la precisión de la columna', () => {
    expect(fIssues(fValues({ liters: null })).liters).toBe('Los litros deben ser mayores que 0.')
    expect(fIssues(fValues({ liters: 0 })).liters).toBe('Los litros deben ser mayores que 0.')
    expect(fIssues(fValues({ liters: -3 })).liters).toBe('Los litros deben ser mayores que 0.')
    expect(fIssues(fValues({ liters: 10.1234 })).liters).toBe('El valor admite como máximo 3 decimales y debe ser menor que 10,000,000.')
  })

  it('el costo total es obligatorio (la pantalla no lo asume en 0) y no negativo', () => {
    expect(fIssues(fValues({ totalCost: null })).totalCost).toBe('Indique el costo total de la carga.')
    expect(fIssues(fValues({ totalCost: -1 })).totalCost).toBe('El costo total no puede ser negativo.')
    expect(fIssues(fValues({ totalCost: 0 }))).toEqual({})
  })

  it('odómetro opcional, no negativo y con precisión; estación de hasta 150 caracteres', () => {
    expect(fIssues(fValues({ odometerKm: null }))).toEqual({})
    expect(fIssues(fValues({ odometerKm: -1 })).odometerKm).toBe('El odómetro no puede ser negativo.')
    expect(fIssues(fValues({ station: 'x'.repeat(151) })).station).toBe('La estación admite como máximo 150 caracteres.')
  })
})

describe('combustible — armado de requests', () => {
  it('la fecha y la hora de la compañía pasan a UTC (Puerto Rico = UTC-4)', () => {
    expect(fuelInstant({ date: '2026-10-12', time: '09:30' })).toBe('2026-10-12T13:30:00.000Z')
    expect(fuelInstant({ date: '', time: '09:30' })).toBeNull()
  })

  it('alta: lo vacío opcional no se manda', () => {
    expect(buildFuelCreate(fValues({ station: ' Puma ', driverPublicId: 'd-1' }))).toEqual({
      vehiclePublicId: 'v-1',
      fillDateUtc: '2026-10-12T13:30:00.000Z',
      liters: 40,
      totalCost: 60,
      odometerKm: 5200,
      driverPublicId: 'd-1',
      station: 'Puma',
    })
    expect(buildFuelCreate(fValues({ odometerKm: null }))).toEqual({ vehiclePublicId: 'v-1', fillDateUtc: '2026-10-12T13:30:00.000Z', liters: 40, totalCost: 60 })
  })

  const log: FuelLog = { id: 9, vehiclePublicId: 'v-1', vehicleCode: 'V-01', fillDateUtc: '2026-10-12T13:30:00', liters: 40, totalCost: 60, odometerKm: 5200, driverPublicId: 'd-1', driverName: 'Ramón', station: 'Puma', isActive: true }

  it('los valores de una carga existente se leen en la hora de la compañía', () => {
    expect(fuelValuesOf(log)).toEqual({ vehiclePublicId: 'v-1', date: '2026-10-12', time: '09:30', liters: 40, totalCost: 60, odometerKm: 5200, driverPublicId: 'd-1', station: 'Puma' })
  })

  it('PATCH: sin cambios no manda nada; solo viaja lo cambiado', () => {
    expect(buildFuelPatch(fuelValuesOf(log), log)).toEqual({ request: {}, changed: false })
    expect(buildFuelPatch({ ...fuelValuesOf(log), liters: 41.5, time: '10:00' }, log)).toEqual({ request: { fillDateUtc: '2026-10-12T14:00:00.000Z', liters: 41.5 }, changed: true })
  })

  it('PATCH: odómetro y chofer vaciados van con clearOdometer / clearDriver; la estación vacía va como texto vacío', () => {
    const v = { ...fuelValuesOf(log), odometerKm: null, driverPublicId: '', station: '' }
    expect(buildFuelPatch(v, log).request).toEqual({ clearOdometer: true, clearDriver: true, station: '' })
    expect(buildFuelPatch({ ...fuelValuesOf(log), odometerKm: 5300, driverPublicId: 'd-2' }, log).request).toEqual({ odometerKm: 5300, driverPublicId: 'd-2' })
  })

  it('nunca manda el vehículo al editar', () => {
    const plan = buildFuelPatch({ ...fuelValuesOf(log), vehiclePublicId: 'v-2' }, log)
    expect(plan.changed).toBe(false)
    expect('vehiclePublicId' in plan.request).toBe(false)
  })

  it('valores de una carga nueva: hoy y la hora actual de la compañía', () => {
    const v = emptyFuelValues(new Date('2026-10-12T15:07:00Z'), 'v-2')
    expect(v).toMatchObject({ vehiclePublicId: 'v-2', date: '2026-10-12', time: '11:07', liters: null, totalCost: null, odometerKm: null })
  })
})

describe('combustible — filtros', () => {
  it('«hasta» es exclusivo: la medianoche local del día siguiente; «desde», la del primer día', () => {
    expect(buildFuelQuery({ vehicle: '', driver: '', from: '', to: '' })).toEqual({})
    expect(buildFuelQuery({ vehicle: 'v-1', driver: 'd-1', from: '2026-10-01', to: '2026-10-31' })).toEqual({
      vehiclePublicId: 'v-1',
      driverPublicId: 'd-1',
      fromUtc: '2026-10-01T04:00:00.000Z',
      toUtc: '2026-11-01T04:00:00.000Z',
    })
  })

  it('el día siguiente cruza fin de mes y de año', () => {
    expect(nextDay('2026-10-31')).toBe('2026-11-01')
    expect(nextDay('2026-12-31')).toBe('2027-01-01')
    expect(nextDay('2028-02-28')).toBe('2028-02-29')
  })
})

// ---------------------------------------------------------------- textos
describe('textos', () => {
  function keysOf(obj: unknown, prefix = ''): string[] {
    if (obj === null || typeof obj !== 'object') return [prefix]
    return Object.entries(obj as Record<string, unknown>).flatMap(([k, v]) => keysOf(v, prefix ? `${prefix}.${k}` : k))
  }

  it('fleet.maintenance.*, fleet.workOrders.* y fleet.fuel.* tienen las mismas claves en español e inglés', () => {
    for (const section of ['maintenance', 'workOrders', 'fuel'] as const) {
      expect(keysOf(enDict.fleet[section]).sort()).toEqual(keysOf(esDict.fleet[section]).sort())
    }
  })

  it('las claves que usan las reglas existen', () => {
    for (const key of ['fleet.maintenance.errors.nameRequired', 'fleet.workOrders.errors.closeOdometerRequired', 'fleet.fuel.errors.litersPositive']) {
      expect(translate('es', key)).not.toBe(key)
      expect(translate('en', key)).not.toBe(key)
    }
  })
})
