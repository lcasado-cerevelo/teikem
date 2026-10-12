// Lote F-D3 — Mantenimiento y combustible de Flota. Lógica pura: esquemas zod espejo del servidor (MaintenanceScheduleService,
// MaintenanceWorkOrderService, FuelLogService, MaintenanceDue; manual 04 §5–§7) con SUS mensajes, armado de los requests (alta y PATCH solo
// de lo cambiado), estado de vencimiento, acciones de una orden por estatus y filtros. Sin React ni red.
import { z } from 'zod'
import { utcFromZonedInput, zonedInputFromUtc } from '../../kernel/api/tenantZone'
import { TRANSITION_COMMENT_MAX } from '../../kernel/catalogs'
import { decimalIssue, type TFn } from './fleetRules'
import type {
  DueQuery,
  FuelLog,
  FuelLogCreateRequest,
  FuelLogPatchRequest,
  FuelQuery,
  MaintenanceDue,
  MaintenanceSchedule,
  MaintenanceScheduleRequest,
  MaintenanceTask,
  MaintenanceTaskRequest,
  WorkOrderCreateRequest,
  WorkOrderDetail,
  WorkOrderPatchRequest,
  WorkOrderQuery,
  WorkOrderStatusRequest,
} from './maintenanceApi'

// ---- topes (= columnas de MaintenanceSchedule / MaintenanceWorkOrder / MaintenanceTask / FuelLog; los mismos del servidor) ----
export const SCHEDULE_NAME_MAX = 150
export const MAX_INTERVAL_DAYS = 36500
export const VENDOR_MAX = 150
export const TASK_DESCRIPTION_MAX = 250
export const STATION_MAX = 150
/** Tolerancia del servidor para «fecha futura» en una carga (el reloj del equipo puede ir unos minutos adelantado). */
export const FUTURE_TOLERANCE_MS = 5 * 60_000

/** Disparadores del catálogo MaintenanceTrigger. */
export const Triggers = { Mileage: 'MILEAGE', Time: 'TIME', Both: 'BOTH' } as const
export const TRIGGER_CODES = [Triggers.Mileage, Triggers.Time, Triggers.Both] as const
/** Tipos del catálogo MaintenanceType. */
export const MaintenanceTypes = { Preventive: 'PREVENTIVE', Corrective: 'CORRECTIVE' } as const
/** Estatus del dominio WorkOrderStatus. */
export const WorkOrderStatuses = { Open: 'OPEN', InProgress: 'IN_PROGRESS', Closed: 'CLOSED', Cancelled: 'CANCELLED' } as const
/** Las órdenes «abiertas» de la tarjeta: las que todavía no se cierran ni se cancelan. */
export const OPEN_WORK_ORDER_STATUSES = [WorkOrderStatuses.Open, WorkOrderStatuses.InProgress] as const

export function usesKm(trigger: string | null | undefined): boolean {
  const code = (trigger ?? '').toUpperCase()
  return code === Triggers.Mileage || code === Triggers.Both
}
export function usesDays(trigger: string | null | undefined): boolean {
  const code = (trigger ?? '').toUpperCase()
  return code === Triggers.Time || code === Triggers.Both
}

// ---------------------------------------------------------------- vencimiento (panel «Mantenimiento preventivo»)

export const DueStates = { Ok: 'OK', DueSoon: 'DUE_SOON', Overdue: 'OVERDUE', NoBaseline: 'NO_BASELINE' } as const
export type DueState = (typeof DueStates)[keyof typeof DueStates]
export const DUE_STATE_ORDER: readonly DueState[] = [DueStates.Overdue, DueStates.DueSoon, DueStates.NoBaseline, DueStates.Ok]

export interface DueChipInfo {
  tone: 'deliv' | 'cap' | 'fail' | 'neutral'
  /** Clave de texto (`fleet.maintenance.state.*`). */
  key: string
  /** Gravedad para ordenar: Vencido 3, Por vencer 2, Sin historial 1, Al día 0 (como `MaintenanceDue.Severity`). */
  severity: number
}

/** Chip de la columna «Estado» (maqueta: Al día en verde, Por vencer en `cap`, Vencido en rojo). */
export function dueChipInfo(state: string | null | undefined): DueChipInfo {
  switch ((state ?? '').toUpperCase()) {
    case DueStates.Overdue:
      return { tone: 'fail', key: 'fleet.maintenance.state.OVERDUE', severity: 3 }
    case DueStates.DueSoon:
      return { tone: 'cap', key: 'fleet.maintenance.state.DUE_SOON', severity: 2 }
    case DueStates.NoBaseline:
      return { tone: 'neutral', key: 'fleet.maintenance.state.NO_BASELINE', severity: 1 }
    default:
      return { tone: 'deliv', key: 'fleet.maintenance.state.OK', severity: 0 }
  }
}

export interface DueFormat {
  number: (n: number) => string
  date: (iso: string) => string
}

/** «Próximo» de un programa: el kilometraje, la fecha, o los dos (disparador «Ambos»). */
export function dueNextText(row: Pick<MaintenanceDue, 'nextDueKm' | 'nextDueDate'>, fmt: DueFormat): string {
  const parts: string[] = []
  if (row.nextDueKm != null) parts.push(`${fmt.number(row.nextDueKm)} km`)
  if (row.nextDueDate) parts.push(fmt.date(row.nextDueDate))
  return parts.length ? parts.join(' · ') : '—'
}

/** Lo que falta (o lo pasado) en texto claro: «Faltan 1,200 km», «Vence hoy», «Vencido hace 3 días». */
export function dueRemainingText(row: Pick<MaintenanceDue, 'kmRemaining' | 'daysRemaining'>, t: TFn, fmt: Pick<DueFormat, 'number'>): string {
  const parts: string[] = []
  if (row.kmRemaining != null) {
    parts.push(row.kmRemaining >= 0 ? t('fleet.maintenance.left.kmLeft', { km: fmt.number(row.kmRemaining) }) : t('fleet.maintenance.left.kmOver', { km: fmt.number(Math.abs(row.kmRemaining)) }))
  }
  const d = row.daysRemaining
  if (d != null) {
    if (d === 0) parts.push(t('fleet.maintenance.left.today'))
    else if (d > 0) parts.push(t(d === 1 ? 'fleet.maintenance.left.daysLeftOne' : 'fleet.maintenance.left.daysLeft', { days: d }))
    else parts.push(t(d === -1 ? 'fleet.maintenance.left.daysOverOne' : 'fleet.maintenance.left.daysOver', { days: Math.abs(d) }))
  }
  return parts.join(' · ')
}

/** Consulta de `GET /maintenance-schedules/due`: estado y vehículo opcionales. */
export function buildDueQuery(f: { state: string; vehicle: string }): DueQuery {
  return { ...(f.state ? { status: [f.state] } : {}), ...(f.vehicle ? { vehiclePublicId: f.vehicle } : {}) }
}

// ---------------------------------------------------------------- números y fechas

const text = (s: string) => s.trim()

/** Día siguiente de un 'YYYY-MM-DD' (calendario puro, sin zona). */
export function nextDay(day: string): string {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + 1)).toISOString().slice(0, 10)
}

function decimalMessage(t: TFn, value: number | null, precision: number, scale: number): string | null {
  const issue = decimalIssue(value, precision, scale)
  return issue ? t('fleet.errors.decimals', issue) : null
}

// ---------------------------------------------------------------- programas de mantenimiento

export type ScheduleTarget = 'vehicle' | 'type'

export interface ScheduleValues {
  name: string
  /** «Aplica a»: un vehículo o un tipo de vehículo (nunca los dos). */
  target: ScheduleTarget
  vehiclePublicId: string
  /** Código del catálogo VehicleType. */
  vehicleType: string
  /** MILEAGE, TIME o BOTH. */
  trigger: string
  intervalKm: number | null
  intervalDays: number | null
  /** Solo en programas de un vehículo. */
  lastServiceKm: number | null
  /** 'YYYY-MM-DD' o ''. */
  lastServiceDate: string
}

export const emptyScheduleValues = (vehiclePublicId = ''): ScheduleValues => ({
  name: '',
  target: 'vehicle',
  vehiclePublicId,
  vehicleType: '',
  trigger: Triggers.Mileage,
  intervalKm: null,
  intervalDays: null,
  lastServiceKm: null,
  lastServiceDate: '',
})

export function scheduleValuesOf(s: MaintenanceSchedule): ScheduleValues {
  return {
    name: s.name ?? '',
    target: s.vehiclePublicId ? 'vehicle' : 'type',
    vehiclePublicId: s.vehiclePublicId ?? '',
    vehicleType: s.vehicleTypeCode ?? '',
    trigger: (s.triggerCode ?? Triggers.Mileage).toUpperCase(),
    intervalKm: s.intervalKm ?? null,
    intervalDays: s.intervalDays ?? null,
    lastServiceKm: s.lastServiceKm ?? null,
    lastServiceDate: s.lastServiceDate ?? '',
  }
}

export interface ScheduleSchemaOptions {
  /** 'YYYY-MM-DD' de hoy en la zona de la compañía (el último servicio no puede ser futuro). */
  today: string
  /** Valores guardados (solo edición): el servidor lee `null` como «no cambiar», así que el último servicio guardado no se puede vaciar. */
  original?: ScheduleValues
}

export function scheduleSchema(t: TFn, opts: ScheduleSchemaOptions) {
  return z
    .object({
      name: z.string(),
      target: z.enum(['vehicle', 'type']),
      vehiclePublicId: z.string(),
      vehicleType: z.string(),
      trigger: z.string(),
      intervalKm: z.number().nullable(),
      intervalDays: z.number().nullable(),
      lastServiceKm: z.number().nullable(),
      lastServiceDate: z.string(),
    })
    .superRefine((v, ctx) => {
      const add = (path: keyof ScheduleValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      const name = text(v.name)
      if (!name) add('name', t('fleet.maintenance.errors.nameRequired'))
      else if (name.length > SCHEDULE_NAME_MAX) add('name', t('fleet.maintenance.errors.nameMax', { max: SCHEDULE_NAME_MAX }))

      if (v.target === 'vehicle' ? !v.vehiclePublicId : !v.vehicleType) add(v.target === 'vehicle' ? 'vehiclePublicId' : 'vehicleType', t('fleet.maintenance.errors.targetRequired'))

      if (!v.trigger) add('trigger', t('fleet.maintenance.errors.triggerRequired'))
      const km = usesKm(v.trigger)
      const days = usesDays(v.trigger)

      if (km && (v.intervalKm === null || v.intervalKm <= 0)) add('intervalKm', t('fleet.maintenance.errors.mileageInterval'))
      else if (v.intervalKm !== null && v.intervalKm <= 0) add('intervalKm', t('fleet.maintenance.errors.intervalKmPositive'))
      else {
        const m = decimalMessage(t, v.intervalKm, 12, 1)
        if (m) add('intervalKm', m)
      }

      if (days && (v.intervalDays === null || v.intervalDays <= 0)) add('intervalDays', t('fleet.maintenance.errors.timeInterval'))
      else if (v.intervalDays !== null && v.intervalDays <= 0) add('intervalDays', t('fleet.maintenance.errors.intervalDaysPositive'))
      else if (v.intervalDays !== null && !Number.isInteger(v.intervalDays)) add('intervalDays', t('fleet.maintenance.errors.intervalDaysInteger'))
      else if (v.intervalDays !== null && v.intervalDays > MAX_INTERVAL_DAYS) add('intervalDays', t('fleet.maintenance.errors.intervalDaysMax', { max: String(MAX_INTERVAL_DAYS) }))

      // el último servicio solo existe en programas de un vehículo
      if (v.target === 'vehicle') {
        if (v.lastServiceKm !== null && v.lastServiceKm < 0) add('lastServiceKm', t('fleet.maintenance.errors.lastServiceKmNegative'))
        else {
          const m = decimalMessage(t, v.lastServiceKm, 12, 1)
          if (m) add('lastServiceKm', m)
        }
        if (v.lastServiceDate && v.lastServiceDate > opts.today) add('lastServiceDate', t('fleet.maintenance.errors.lastServiceDateFuture'))
        const o = opts.original
        if (o && o.target === 'vehicle') {
          if (o.lastServiceKm !== null && v.lastServiceKm === null) add('lastServiceKm', t('fleet.errors.cannotClear'))
          if (o.lastServiceDate && !v.lastServiceDate) add('lastServiceDate', t('fleet.errors.cannotClear'))
        }
      }
    })
}

/** Alta: el objetivo es el vehículo O el tipo; el último servicio solo se manda con un vehículo. */
export function buildScheduleCreate(v: ScheduleValues): MaintenanceScheduleRequest {
  const vehicle = v.target === 'vehicle'
  return {
    name: text(v.name),
    ...(vehicle ? { vehiclePublicId: v.vehiclePublicId } : { vehicleType: v.vehicleType }),
    trigger: v.trigger,
    intervalKm: v.intervalKm,
    intervalDays: v.intervalDays,
    ...(vehicle ? { lastServiceKm: v.lastServiceKm, lastServiceDate: v.lastServiceDate || null } : {}),
  }
}

export interface SchedulePatchPlan {
  request: MaintenanceScheduleRequest
  /** false = no hay nada que mandar. */
  changed: boolean
}

/**
 * PATCH solo de lo cambiado (`null` = no cambiar). Un intervalo que se vacía va con `clearIntervalKm` / `clearIntervalDays`; el último
 * servicio no se puede vaciar (lo impide el esquema). Cambiar de objetivo manda el nuevo (vehículo o tipo), nunca los dos.
 */
export function buildSchedulePatch(v: ScheduleValues, schedule: MaintenanceSchedule): SchedulePatchPlan {
  const o = scheduleValuesOf(schedule)
  const request: MaintenanceScheduleRequest = {}
  let changed = false
  const set = <K extends keyof MaintenanceScheduleRequest>(key: K, value: MaintenanceScheduleRequest[K]) => {
    request[key] = value
    changed = true
  }
  if (text(v.name) !== o.name) set('name', text(v.name))
  if (v.target === 'vehicle') {
    if (o.target !== 'vehicle' || v.vehiclePublicId !== o.vehiclePublicId) set('vehiclePublicId', v.vehiclePublicId)
  } else if (o.target !== 'type' || v.vehicleType !== o.vehicleType) set('vehicleType', v.vehicleType)
  if (v.trigger !== o.trigger) set('trigger', v.trigger)

  if (v.intervalKm !== o.intervalKm) {
    if (v.intervalKm === null) set('clearIntervalKm', true)
    else set('intervalKm', v.intervalKm)
  }
  if (v.intervalDays !== o.intervalDays) {
    if (v.intervalDays === null) set('clearIntervalDays', true)
    else set('intervalDays', v.intervalDays)
  }
  if (v.target === 'vehicle') {
    if (v.lastServiceKm !== o.lastServiceKm && v.lastServiceKm !== null) set('lastServiceKm', v.lastServiceKm)
    if (v.lastServiceDate !== o.lastServiceDate && v.lastServiceDate) set('lastServiceDate', v.lastServiceDate)
  }
  return { request, changed }
}

/** Texto de «Aplica a»: el vehículo (código) o el tipo de vehículo. */
export function scheduleTargetText(s: Pick<MaintenanceSchedule, 'vehicleCode' | 'vehicleType' | 'vehicleTypeCode'>, typeLabel: (label: string) => string): string {
  return s.vehicleCode ? s.vehicleCode : typeLabel(s.vehicleType || s.vehicleTypeCode || '—')
}

// ---------------------------------------------------------------- órdenes de trabajo: alta

export interface WorkOrderCreateValues {
  vehiclePublicId: string
  /** MaintenanceType: PREVENTIVE / CORRECTIVE. Con programa, PREVENTIVE. */
  maintenanceType: string
  /** id del programa (texto del select); '' = sin programa. */
  scheduleId: string
  /** 'YYYY-MM-DD' o ''. */
  scheduledDate: string
  odometerKm: number | null
  vendor: string
  laborCost: number | null
  partsCost: number | null
  notes: string
}

export const emptyWorkOrderCreateValues = (init: Partial<WorkOrderCreateValues> = {}): WorkOrderCreateValues => ({
  vehiclePublicId: '',
  maintenanceType: '',
  scheduleId: '',
  scheduledDate: '',
  odometerKm: null,
  vendor: '',
  laborCost: null,
  partsCost: null,
  notes: '',
  ...init,
})

function costIssues(t: TFn, add: (path: 'laborCost' | 'partsCost', message: string) => void, v: { laborCost?: number | null; partsCost?: number | null }) {
  for (const field of ['laborCost', 'partsCost'] as const) {
    const value = v[field] ?? null
    if (value !== null && value < 0) add(field, t('fleet.workOrders.errors.costNegative'))
    else {
      const m = decimalMessage(t, value, 18, 4)
      if (m) add(field, m)
    }
  }
}

export function workOrderCreateSchema(t: TFn) {
  return z
    .object({
      vehiclePublicId: z.string(),
      maintenanceType: z.string(),
      scheduleId: z.string(),
      scheduledDate: z.string(),
      odometerKm: z.number().nullable(),
      vendor: z.string(),
      laborCost: z.number().nullable(),
      partsCost: z.number().nullable(),
      notes: z.string(),
    })
    .superRefine((v, ctx) => {
      const add = (path: keyof WorkOrderCreateValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (!v.vehiclePublicId) add('vehiclePublicId', t('fleet.workOrders.errors.vehicleRequired'))
      if (!v.maintenanceType && !v.scheduleId) add('maintenanceType', t('fleet.workOrders.errors.typeRequired'))
      else if (v.scheduleId && v.maintenanceType && v.maintenanceType.toUpperCase() !== MaintenanceTypes.Preventive) add('maintenanceType', t('fleet.workOrders.errors.correctiveWithSchedule'))
      if (text(v.vendor).length > VENDOR_MAX) add('vendor', t('fleet.workOrders.errors.vendorMax', { max: VENDOR_MAX }))
      if (v.odometerKm !== null && v.odometerKm < 0) add('odometerKm', t('fleet.errors.odometerNegative'))
      else {
        const m = decimalMessage(t, v.odometerKm, 12, 1)
        if (m) add('odometerKm', m)
      }
      costIssues(t, add, v)
    })
}

/** Alta de la orden: con programa el tipo es PREVENTIVE; el número OT-##### lo asigna el sistema. */
export function buildWorkOrderCreate(v: WorkOrderCreateValues): WorkOrderCreateRequest {
  const vendor = text(v.vendor)
  const notes = text(v.notes)
  return {
    vehiclePublicId: v.vehiclePublicId,
    maintenanceType: v.scheduleId ? MaintenanceTypes.Preventive : v.maintenanceType,
    ...(v.scheduleId ? { scheduleId: Number(v.scheduleId) } : {}),
    ...(v.scheduledDate ? { scheduledDate: v.scheduledDate } : {}),
    ...(v.odometerKm !== null ? { odometerKm: v.odometerKm } : {}),
    ...(vendor ? { vendor } : {}),
    ...(v.laborCost !== null ? { laborCost: v.laborCost } : {}),
    ...(v.partsCost !== null ? { partsCost: v.partsCost } : {}),
    ...(notes ? { notes } : {}),
  }
}

/** Programas que se pueden elegir para un vehículo: los activos de ese vehículo o de su tipo (como `ScheduleNotApplicable` del servidor). */
export function schedulesForVehicle(
  schedules: readonly MaintenanceSchedule[],
  vehicle: { publicId?: string; vehicleTypeCode?: string | null } | undefined,
): MaintenanceSchedule[] {
  if (!vehicle) return []
  return schedules.filter(
    (s) => s.isActive !== false && (s.vehiclePublicId ? s.vehiclePublicId === vehicle.publicId : !!s.vehicleTypeCode && s.vehicleTypeCode === vehicle.vehicleTypeCode),
  )
}

// ---------------------------------------------------------------- órdenes de trabajo: ficha

export interface WorkOrderEditValues {
  scheduledDate: string
  odometerKm: number | null
  vendor: string
  laborCost: number | null
  partsCost: number | null
  notes: string
}

export function workOrderEditValuesOf(w: WorkOrderDetail): WorkOrderEditValues {
  return {
    scheduledDate: w.scheduledDate ?? '',
    odometerKm: w.odometerKm ?? null,
    vendor: w.vendor ?? '',
    laborCost: w.laborCost ?? null,
    partsCost: w.partsCost ?? null,
    notes: w.notes ?? '',
  }
}

export function workOrderEditSchema(t: TFn, opts: { original: WorkOrderEditValues; costsFromTasks: boolean }) {
  return z
    .object({
      scheduledDate: z.string(),
      odometerKm: z.number().nullable(),
      vendor: z.string(),
      laborCost: z.number().nullable(),
      partsCost: z.number().nullable(),
      notes: z.string(),
    })
    .superRefine((v, ctx) => {
      const add = (path: keyof WorkOrderEditValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (text(v.vendor).length > VENDOR_MAX) add('vendor', t('fleet.workOrders.errors.vendorMax', { max: VENDOR_MAX }))
      if (v.odometerKm !== null && v.odometerKm < 0) add('odometerKm', t('fleet.errors.odometerNegative'))
      else {
        const m = decimalMessage(t, v.odometerKm, 12, 1)
        if (m) add('odometerKm', m)
      }
      if (!opts.costsFromTasks) costIssues(t, add, v)
      // el PATCH lee `null` como «no cambiar»: un valor guardado no se puede dejar vacío
      const o = opts.original
      if (o.scheduledDate && !v.scheduledDate) add('scheduledDate', t('fleet.errors.cannotClear'))
      if (o.odometerKm !== null && v.odometerKm === null) add('odometerKm', t('fleet.errors.cannotClear'))
      if (!opts.costsFromTasks) {
        if (o.laborCost !== null && v.laborCost === null) add('laborCost', t('fleet.errors.cannotClear'))
        if (o.partsCost !== null && v.partsCost === null) add('partsCost', t('fleet.errors.cannotClear'))
      }
    })
}

export interface WorkOrderPatchPlan {
  request: WorkOrderPatchRequest
  changed: boolean
}

/**
 * PATCH de la orden: solo lo cambiado + `rowVersion`. Los textos se mandan tal cual (`''` los borra); las fechas y los números solo se
 * cambian. Con tareas activas los costos los calcula el servidor: nunca se mandan (sería 400).
 */
export function buildWorkOrderPatch(v: WorkOrderEditValues, w: WorkOrderDetail): WorkOrderPatchPlan {
  const o = workOrderEditValuesOf(w)
  const request: WorkOrderPatchRequest = { rowVersion: w.rowVersion ?? null }
  let changed = false
  if (v.scheduledDate !== o.scheduledDate && v.scheduledDate) {
    request.scheduledDate = v.scheduledDate
    changed = true
  }
  if (v.odometerKm !== o.odometerKm && v.odometerKm !== null) {
    request.odometerKm = v.odometerKm
    changed = true
  }
  if (text(v.vendor) !== o.vendor) {
    request.vendor = text(v.vendor)
    changed = true
  }
  if (text(v.notes) !== o.notes) {
    request.notes = text(v.notes)
    changed = true
  }
  if (!w.costsFromTasks) {
    if (v.laborCost !== o.laborCost && v.laborCost !== null) {
      request.laborCost = v.laborCost
      changed = true
    }
    if (v.partsCost !== o.partsCost && v.partsCost !== null) {
      request.partsCost = v.partsCost
      changed = true
    }
  }
  return { request, changed }
}

export type WorkOrderAction = 'start' | 'close' | 'cancel'

/**
 * Acciones de la ficha por estatus (OPEN → IN_PROGRESS → CLOSED; CANCELLED terminal): Iniciar solo desde «Abierta»; Cerrar y Cancelar
 * mientras no sea terminal. Cerrar desde «Abierta» sin iniciar también lo permite el servidor.
 */
export function workOrderActions(w: Pick<WorkOrderDetail, 'statusCode' | 'isTerminal'>): WorkOrderAction[] {
  if (w.isTerminal) return []
  const code = (w.statusCode ?? '').toUpperCase()
  if (code === WorkOrderStatuses.Open) return ['start', 'close', 'cancel']
  if (code === WorkOrderStatuses.InProgress) return ['close', 'cancel']
  return []
}

/** ¿Cerrar esta orden exige la lectura del odómetro? (programa por kilometraje o ambos). */
export function closeNeedsOdometer(w: Pick<WorkOrderDetail, 'scheduleTriggerCode'>): boolean {
  return usesKm(w.scheduleTriggerCode)
}

/** Tareas activas todavía sin completar (el servidor no deja cerrar con alguna). */
export function incompleteTasks(tasks: readonly MaintenanceTask[] | null | undefined): number {
  return (tasks ?? []).filter((x) => x.isActive !== false && !x.isCompleted).length
}

/** Suma de las tareas activas: lo que el servidor guarda como labor, partes y total de la orden. */
export function taskTotals(tasks: readonly MaintenanceTask[] | null | undefined): { labor: number; parts: number; total: number } {
  const active = (tasks ?? []).filter((x) => x.isActive !== false)
  const labor = active.reduce((sum, x) => sum + (x.laborCost ?? 0), 0)
  const parts = active.reduce((sum, x) => sum + (x.partCost ?? 0), 0)
  return { labor, parts, total: labor + parts }
}

// ---- cierre ----
export interface CloseValues {
  completedDate: string
  odometerKm: number | null
  comment: string
}

export const closeValuesOf = (w: Pick<WorkOrderDetail, 'odometerKm'>, today: string): CloseValues => ({ completedDate: today, odometerKm: w.odometerKm ?? null, comment: '' })

export function closeSchema(t: TFn, opts: { today: string; needsOdometer: boolean }) {
  return z
    .object({ completedDate: z.string(), odometerKm: z.number().nullable(), comment: z.string() })
    .superRefine((v, ctx) => {
      const add = (path: keyof CloseValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (v.completedDate && v.completedDate > opts.today) add('completedDate', t('fleet.workOrders.errors.completedDateFuture'))
      if (v.odometerKm === null) {
        if (opts.needsOdometer) add('odometerKm', t('fleet.workOrders.errors.closeOdometerRequired'))
      } else if (v.odometerKm < 0) add('odometerKm', t('fleet.errors.odometerNegative'))
      else {
        const m = decimalMessage(t, v.odometerKm, 12, 1)
        if (m) add('odometerKm', m)
      }
      if (v.comment.length > TRANSITION_COMMENT_MAX) add('comment', t('status.commentTooLong', { max: TRANSITION_COMMENT_MAX }))
    })
}

export function buildCloseRequest(v: CloseValues, rowVersion: string | null | undefined): WorkOrderStatusRequest {
  const comment = text(v.comment)
  return {
    toCode: WorkOrderStatuses.Closed,
    ...(v.completedDate ? { completedDate: v.completedDate } : {}),
    ...(v.odometerKm !== null ? { odometerKm: v.odometerKm } : {}),
    ...(comment ? { comment } : {}),
    rowVersion: rowVersion ?? null,
  }
}

// ---- tareas ----
export interface TaskValues {
  description: string
  partCost: number | null
  laborCost: number | null
}

export const emptyTaskValues = (): TaskValues => ({ description: '', partCost: null, laborCost: null })
export const taskValuesOf = (x: MaintenanceTask): TaskValues => ({ description: x.description ?? '', partCost: x.partCost ?? null, laborCost: x.laborCost ?? null })

export function taskSchema(t: TFn) {
  return z
    .object({ description: z.string(), partCost: z.number().nullable(), laborCost: z.number().nullable() })
    .superRefine((v, ctx) => {
      const add = (path: keyof TaskValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      const d = text(v.description)
      if (!d) add('description', t('fleet.workOrders.errors.taskDescriptionRequired'))
      else if (d.length > TASK_DESCRIPTION_MAX) add('description', t('fleet.workOrders.errors.taskDescriptionMax', { max: TASK_DESCRIPTION_MAX }))
      for (const field of ['partCost', 'laborCost'] as const) {
        const value = v[field]
        if (value !== null && value < 0) add(field, t('fleet.workOrders.errors.costNegative'))
        else {
          const m = decimalMessage(t, value, 18, 4)
          if (m) add(field, m)
        }
      }
    })
}

/**
 * Tarea: alta con todo lo capturado; edición solo con lo cambiado (`null` = no cambiar, así que un costo capturado no se puede vaciar:
 * se escribe 0).
 */
export function buildTaskRequest(v: TaskValues, original?: TaskValues): MaintenanceTaskRequest {
  const req: MaintenanceTaskRequest = {}
  if (!original || text(v.description) !== original.description) req.description = text(v.description)
  if (v.partCost !== null && (!original || v.partCost !== original.partCost)) req.partCost = v.partCost
  if (v.laborCost !== null && (!original || v.laborCost !== original.laborCost)) req.laborCost = v.laborCost
  return req
}

export function hasTaskChanges(req: MaintenanceTaskRequest): boolean {
  return Object.keys(req).length > 0
}

// ---------------------------------------------------------------- órdenes de trabajo: lista

export interface WorkOrderFilters {
  /** Códigos de estatus; vacío = todos. */
  statuses: readonly string[]
  /** Código de tipo ('' = todos). */
  type: string
  /** publicId del vehículo ('' = todos). */
  vehicle: string
  /** Días 'YYYY-MM-DD' (fecha programada, o la de alta si no tiene). */
  from: string
  to: string
}

export const EMPTY_WORK_ORDER_FILTERS: WorkOrderFilters = { statuses: [], type: '', vehicle: '', from: '', to: '' }

export function buildWorkOrderQuery(f: WorkOrderFilters): WorkOrderQuery {
  return {
    ...(f.statuses.length ? { status: [...f.statuses] } : {}),
    ...(f.type ? { maintenanceType: [f.type] } : {}),
    ...(f.vehicle ? { vehiclePublicId: f.vehicle } : {}),
    ...(f.from ? { from: f.from } : {}),
    ...(f.to ? { to: f.to } : {}),
  }
}

// ---------------------------------------------------------------- combustible

export interface FuelValues {
  vehiclePublicId: string
  /** 'YYYY-MM-DD' en la zona de la compañía. */
  date: string
  /** 'HH:mm' en la zona de la compañía. */
  time: string
  liters: number | null
  totalCost: number | null
  odometerKm: number | null
  driverPublicId: string
  station: string
}

export function emptyFuelValues(now: Date = new Date(), vehiclePublicId = ''): FuelValues {
  const [date, time] = zonedInputFromUtc(now.toISOString()).split('T')
  return { vehiclePublicId, date, time, liters: null, totalCost: null, odometerKm: null, driverPublicId: '', station: '' }
}

export function fuelValuesOf(l: FuelLog): FuelValues {
  const [date, time] = zonedInputFromUtc(l.fillDateUtc).split('T')
  return {
    vehiclePublicId: l.vehiclePublicId ?? '',
    date: date ?? '',
    time: time ?? '',
    liters: l.liters ?? null,
    totalCost: l.totalCost ?? null,
    odometerKm: l.odometerKm ?? null,
    driverPublicId: l.driverPublicId ?? '',
    station: l.station ?? '',
  }
}

/** Instante UTC (ISO) de la fecha y hora capturadas en la zona de la compañía; null si faltan o no son válidas. */
export function fuelInstant(v: Pick<FuelValues, 'date' | 'time'>): string | null {
  if (!v.date || !v.time) return null
  return utcFromZonedInput(`${v.date}T${v.time}`)
}

export function fuelSchema(t: TFn, opts: { creating: boolean; now?: () => number; original?: FuelValues }) {
  const now = opts.now ?? Date.now
  return z
    .object({
      vehiclePublicId: z.string(),
      date: z.string(),
      time: z.string(),
      liters: z.number().nullable(),
      totalCost: z.number().nullable(),
      odometerKm: z.number().nullable(),
      driverPublicId: z.string(),
      station: z.string(),
    })
    .superRefine((v, ctx) => {
      const add = (path: keyof FuelValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (opts.creating && !v.vehiclePublicId) add('vehiclePublicId', t('fleet.fuel.errors.vehicleRequired'))
      if (!v.date) add('date', t('fleet.fuel.errors.dateRequired'))
      else if (!v.time) add('time', t('fleet.fuel.errors.timeRequired'))
      else {
        const instant = fuelInstant(v)
        if (!instant) add('date', t('fleet.fuel.errors.dateRequired'))
        else if (new Date(instant).getTime() > now() + FUTURE_TOLERANCE_MS) add('date', t('fleet.fuel.errors.dateFuture'))
      }
      if (v.liters === null || v.liters <= 0) add('liters', t('fleet.fuel.errors.litersPositive'))
      else {
        const m = decimalMessage(t, v.liters, 10, 3)
        if (m) add('liters', m)
      }
      if (v.totalCost === null) add('totalCost', t('fleet.fuel.errors.costRequired'))
      else if (v.totalCost < 0) add('totalCost', t('fleet.fuel.errors.costNegative'))
      else {
        const m = decimalMessage(t, v.totalCost, 18, 4)
        if (m) add('totalCost', m)
      }
      if (v.odometerKm !== null && v.odometerKm < 0) add('odometerKm', t('fleet.errors.odometerNegative'))
      else {
        const m = decimalMessage(t, v.odometerKm, 12, 1)
        if (m) add('odometerKm', m)
      }
      if (text(v.station).length > STATION_MAX) add('station', t('fleet.fuel.errors.stationMax', { max: STATION_MAX }))
    })
}

/** Alta de la carga: la fecha y la hora de la compañía pasan a UTC; lo opcional vacío no se manda. */
export function buildFuelCreate(v: FuelValues): FuelLogCreateRequest {
  const station = text(v.station)
  return {
    vehiclePublicId: v.vehiclePublicId,
    fillDateUtc: fuelInstant(v) ?? undefined,
    liters: v.liters ?? 0,
    totalCost: v.totalCost ?? 0,
    ...(v.odometerKm !== null ? { odometerKm: v.odometerKm } : {}),
    ...(v.driverPublicId ? { driverPublicId: v.driverPublicId } : {}),
    ...(station ? { station } : {}),
  }
}

export interface FuelPatchPlan {
  request: FuelLogPatchRequest
  changed: boolean
}

/**
 * PATCH de la carga: solo lo cambiado. El vehículo no se cambia. Odómetro y chofer vaciados van con `clearOdometer` / `clearDriver`
 * (`null` = no cambiar); la estación vacía va como `''` (la borra).
 */
export function buildFuelPatch(v: FuelValues, log: FuelLog): FuelPatchPlan {
  const o = fuelValuesOf(log)
  const request: FuelLogPatchRequest = {}
  let changed = false
  const set = <K extends keyof FuelLogPatchRequest>(key: K, value: FuelLogPatchRequest[K]) => {
    request[key] = value
    changed = true
  }
  if (v.date !== o.date || v.time !== o.time) {
    const instant = fuelInstant(v)
    if (instant) set('fillDateUtc', instant)
  }
  if (v.liters !== null && v.liters !== o.liters) set('liters', v.liters)
  if (v.totalCost !== null && v.totalCost !== o.totalCost) set('totalCost', v.totalCost)
  if (v.odometerKm !== o.odometerKm) {
    if (v.odometerKm === null) set('clearOdometer', true)
    else set('odometerKm', v.odometerKm)
  }
  if (v.driverPublicId !== o.driverPublicId) {
    if (!v.driverPublicId) set('clearDriver', true)
    else set('driverPublicId', v.driverPublicId)
  }
  if (text(v.station) !== o.station) set('station', text(v.station))
  return { request, changed }
}

export interface FuelFilters {
  vehicle: string
  driver: string
  /** Días 'YYYY-MM-DD' de la compañía (el último día entra completo). */
  from: string
  to: string
}

export const EMPTY_FUEL_FILTERS: FuelFilters = { vehicle: '', driver: '', from: '', to: '' }

/** Consulta de `GET /fuel-logs` SIN `skip`/`take`: «desde» es la medianoche local del primer día y «hasta» la del día SIGUIENTE al último (exclusivo). */
export function buildFuelQuery(f: FuelFilters): Omit<FuelQuery, 'skip' | 'take'> {
  const from = f.from ? utcFromZonedInput(`${f.from}T00:00`) : null
  const to = f.to ? utcFromZonedInput(`${nextDay(f.to)}T00:00`) : null
  return {
    ...(f.vehicle ? { vehiclePublicId: f.vehicle } : {}),
    ...(f.driver ? { driverPublicId: f.driver } : {}),
    ...(from ? { fromUtc: from } : {}),
    ...(to ? { toUtc: to } : {}),
  }
}
