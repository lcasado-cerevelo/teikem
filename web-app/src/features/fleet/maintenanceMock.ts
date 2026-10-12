// API simulado de las pruebas de Mantenimiento y combustible (F-D3), con estado: programas, órdenes de trabajo (con tareas, transiciones y las
// reglas de cierre del servidor), cargas de combustible (con km/L, costo/km y resumen calculados como el servidor) y catálogos. Lo carga
// `fleetMock.route` al principio: devuelve `undefined` si la ruta no es de este lote.
import type { Call } from './fleetMock'

type Rec = Record<string, unknown>

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

// ---------------------------------------------------------------- datos de ejemplo
const SCHEDULES: Rec[] = [
  { id: 1, name: 'Cambio de aceite', vehiclePublicId: 'v-1', vehicleCode: 'V-01', triggerCode: 'MILEAGE', trigger: 'Kilometraje', intervalKm: 5000, lastServiceKm: 45500, isActive: true },
  { id: 2, name: 'Revisión general', vehicleTypeCode: 'TRUCK', vehicleType: 'Camión', triggerCode: 'TIME', trigger: 'Tiempo', intervalDays: 90, isActive: true },
  { id: 3, name: 'Rotación de gomas', vehiclePublicId: 'v-1', vehicleCode: 'V-01', triggerCode: 'BOTH', trigger: 'Ambos', intervalKm: 10000, intervalDays: 180, isActive: false },
  { id: 4, name: 'Frenos', vehiclePublicId: 'v-1', vehicleCode: 'V-01', triggerCode: 'BOTH', trigger: 'Ambos', intervalKm: 10000, intervalDays: 180, lastServiceKm: 38500, lastServiceDate: '2026-04-22', isActive: true },
]

export const DUE: Rec[] = [
  { scheduleId: 1, scheduleName: 'Cambio de aceite', vehiclePublicId: 'v-1', vehicleCode: 'V-01', triggerCode: 'MILEAGE', intervalKm: 5000, lastServiceKm: 45500, currentOdometerKm: 48210, nextDueKm: 50500, kmRemaining: 2290, state: 'OK' },
  { scheduleId: 4, scheduleName: 'Frenos', vehiclePublicId: 'v-1', vehicleCode: 'V-01', triggerCode: 'BOTH', intervalKm: 10000, intervalDays: 180, lastServiceKm: 38500, lastServiceDate: '2026-04-22', currentOdometerKm: 48210, nextDueKm: 48500, kmRemaining: 290, nextDueDate: '2026-10-19', daysRemaining: 7, state: 'DUE_SOON' },
  { scheduleId: 2, scheduleName: 'Revisión general', vehiclePublicId: 'v-2', vehicleCode: 'V-02', triggerCode: 'TIME', intervalDays: 90, lastServiceDate: '2026-07-04', lastWorkOrderNumber: 'OT-00001', nextDueDate: '2026-10-02', daysRemaining: -10, state: 'OVERDUE' },
  { scheduleId: 2, scheduleName: 'Revisión general', vehiclePublicId: 'v-3', vehicleCode: 'V-03', triggerCode: 'TIME', intervalDays: 90, state: 'NO_BASELINE' },
]

const task = (id: number, description: string, partCost: number | null, laborCost: number | null, isCompleted: boolean): Rec => ({ id, description, partCost, laborCost, isCompleted, isActive: true })

const ORDERS: Rec[] = [
  {
    id: 101,
    publicId: 'wo-1',
    number: 'OT-00001',
    vehiclePublicId: 'v-2',
    vehicleCode: 'V-02',
    maintenanceTypeCode: 'PREVENTIVE',
    maintenanceType: 'Preventivo',
    scheduleId: 2,
    scheduleName: 'Revisión general',
    scheduleTriggerCode: 'TIME',
    statusCode: 'CLOSED',
    status: 'Cerrada',
    isTerminal: true,
    completedDate: '2026-09-27',
    vendor: 'Taller Díaz',
    laborCost: 120,
    partsCost: 340,
    notes: 'Cambio de aceite, filtros, rotación de gomas',
    canEdit: false,
    costsFromTasks: false,
    tasks: [],
    isActive: true,
    rowVersion: 'R1',
  },
  {
    id: 102,
    publicId: 'wo-2',
    number: 'OT-00002',
    vehiclePublicId: 'v-1',
    vehicleCode: 'V-01',
    maintenanceTypeCode: 'CORRECTIVE',
    maintenanceType: 'Correctivo',
    statusCode: 'OPEN',
    status: 'Abierta',
    isTerminal: false,
    scheduledDate: '2026-10-12',
    vendor: 'Taller Díaz',
    canEdit: true,
    costsFromTasks: true,
    tasks: [task(1, 'Cambio de pastillas de freno', 210, 80, false)],
    isActive: true,
    rowVersion: 'R2',
  },
  {
    id: 103,
    publicId: 'wo-3',
    number: 'OT-00003',
    vehiclePublicId: 'v-1',
    vehicleCode: 'V-01',
    maintenanceTypeCode: 'PREVENTIVE',
    maintenanceType: 'Preventivo',
    scheduleId: 1,
    scheduleName: 'Cambio de aceite',
    scheduleTriggerCode: 'MILEAGE',
    statusCode: 'IN_PROGRESS',
    status: 'En proceso',
    isTerminal: false,
    scheduledDate: '2026-10-10',
    vendor: 'AutoZone PR',
    laborCost: 60,
    partsCost: 0,
    canEdit: true,
    costsFromTasks: true,
    tasks: [task(2, 'Cambio de aceite y filtro', 0, 60, true)],
    isActive: true,
    rowVersion: 'R3',
  },
  {
    id: 104,
    publicId: 'wo-4',
    number: 'OT-00004',
    vehiclePublicId: 'v-2',
    vehicleCode: 'V-02',
    maintenanceTypeCode: 'CORRECTIVE',
    maintenanceType: 'Correctivo',
    statusCode: 'CANCELLED',
    status: 'Cancelada',
    isTerminal: true,
    scheduledDate: '2026-09-01',
    canEdit: false,
    costsFromTasks: false,
    tasks: [],
    isActive: true,
    rowVersion: 'R4',
  },
]

const fuelLog = (id: number, vehicle: 'v-1' | 'v-2', fillDateUtc: string, liters: number, totalCost: number, odometerKm: number | null, extra: Rec = {}): Rec => ({
  id,
  vehiclePublicId: vehicle,
  vehicleCode: vehicle === 'v-1' ? 'V-01' : 'V-02',
  fillDateUtc,
  liters,
  totalCost,
  odometerKm,
  isActive: true,
  ...extra,
})

const FUEL: Rec[] = [
  fuelLog(1, 'v-1', '2026-09-27T14:00:00', 37, 166.5, 47420, { station: 'Puma Bayamón', driverPublicId: 'd-1', driverName: 'Ramón Díaz' }),
  fuelLog(2, 'v-1', '2026-10-04T14:00:00', 36.5, 164.7, 47780),
  fuelLog(3, 'v-1', '2026-10-11T14:00:00', 38.2, 172.4, 48210),
  fuelLog(4, 'v-2', '2026-10-09T14:00:00', 95, 420.8, 102340),
]

export const DRIVERS_LIST: Rec[] = [
  { id: 1, publicId: 'd-1', code: '010', fullName: 'Ramón Díaz', isActive: true },
  { id: 2, publicId: 'd-2', code: '001', fullName: 'José Pérez', isActive: true },
]

const WO_STATUSES = [
  { code: 'OPEN', label: 'Abierta', colorHex: '#8a8f98', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
  { code: 'IN_PROGRESS', label: 'En proceso', colorHex: '#d9a441', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 2 },
  { code: 'CLOSED', label: 'Cerrada', colorHex: '#3fb27f', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 3 },
  { code: 'CANCELLED', label: 'Cancelada', colorHex: '#d9534f', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 4 },
]
const STATUS_LABEL: Record<string, string> = { OPEN: 'Abierta', IN_PROGRESS: 'En proceso', CLOSED: 'Cerrada', CANCELLED: 'Cancelada' }

// ---------------------------------------------------------------- estado mutable
export const maintenanceState = {
  schedules: [] as Rec[],
  orders: [] as Rec[],
  fuel: [] as Rec[],
  nextOrder: 5,
  nextTask: 10,
  nextFuel: 10,
}

const clone = (r: Rec): Rec => ({ ...r, tasks: Array.isArray(r.tasks) ? (r.tasks as Rec[]).map((x) => ({ ...x })) : undefined })

export function resetMaintenance() {
  maintenanceState.schedules = SCHEDULES.map((s) => ({ ...s }))
  maintenanceState.orders = ORDERS.map(clone)
  maintenanceState.fuel = FUEL.map((r) => ({ ...r }))
  maintenanceState.nextOrder = 5
  maintenanceState.nextTask = 10
  maintenanceState.nextFuel = 10
}

const num = (v: unknown) => (typeof v === 'number' ? v : 0)

/** Lo que calcula el servidor: con tareas activas, labor y partes son su suma; el total es labor + partes. */
function recompute(o: Rec) {
  const active = (o.tasks as Rec[]).filter((x) => x.isActive !== false)
  o.costsFromTasks = active.length > 0
  if (active.length > 0) {
    o.laborCost = active.reduce((s, x) => s + num(x.laborCost), 0)
    o.partsCost = active.reduce((s, x) => s + num(x.partCost), 0)
  }
  o.totalCost = num(o.laborCost) + num(o.partsCost)
}

function detail(o: Rec): Rec {
  recompute(o)
  return { ...o, tasks: (o.tasks as Rec[]).filter((x) => x.isActive !== false).map((x) => ({ ...x })) }
}

function listItem(o: Rec): Rec {
  recompute(o)
  const { tasks: _tasks, canEdit: _canEdit, costsFromTasks: _costs, notes: _notes, scheduleTriggerCode: _trigger, ...rest } = o
  return rest
}

/** km/L y costo/km como `FuelEfficiency`: cada carga con odómetro contra la anterior con odómetro del mismo vehículo (las cargas sin odómetro suman al tramo). */
function efficiency(vehicle: string): Map<number, { distanceKm: number | null; kmPerLiter: number | null; costPerKm: number | null }> {
  const series = maintenanceState.fuel
    .filter((r) => r.isActive !== false && r.vehiclePublicId === vehicle)
    .sort((a, b) => String(a.fillDateUtc).localeCompare(String(b.fillDateUtc)))
  const out = new Map<number, { distanceKm: number | null; kmPerLiter: number | null; costPerKm: number | null }>()
  let prevOdo: number | null = null
  let liters = 0
  let cost = 0
  for (const r of series) {
    liters += num(r.liters)
    cost += num(r.totalCost)
    if (r.odometerKm == null) {
      out.set(r.id as number, { distanceKm: null, kmPerLiter: null, costPerKm: null })
      continue
    }
    if (prevOdo == null) out.set(r.id as number, { distanceKm: null, kmPerLiter: null, costPerKm: null })
    else {
      const distance = num(r.odometerKm) - prevOdo
      out.set(r.id as number, { distanceKm: distance, kmPerLiter: distance / liters, costPerKm: cost / distance })
    }
    prevOdo = num(r.odometerKm)
    liters = 0
    cost = 0
  }
  return out
}

function fuelItem(r: Rec): Rec {
  const e = efficiency(String(r.vehiclePublicId)).get(r.id as number)
  return { ...r, distanceKm: e?.distanceKm ?? null, kmPerLiter: e?.kmPerLiter ?? null, costPerKm: e?.costPerKm ?? null }
}

function fuelPage(call: Call): Rec {
  const q = call.url.searchParams
  const skip = Number(q.get('skip') ?? 0)
  const take = Number(q.get('take') ?? 100)
  const vehicle = q.get('vehiclePublicId')
  const driver = q.get('driverPublicId')
  const from = q.get('fromUtc')
  const to = q.get('toUtc')
  const matches = maintenanceState.fuel
    .filter((r) => r.isActive !== false || q.get('includeInactive') === 'true')
    .filter((r) => !vehicle || r.vehiclePublicId === vehicle)
    .filter((r) => !driver || r.driverPublicId === driver)
    .filter((r) => !from || String(r.fillDateUtc) >= from.replace('Z', ''))
    .filter((r) => !to || String(r.fillDateUtc) < to.replace('Z', ''))
    .sort((a, b) => String(b.fillDateUtc).localeCompare(String(a.fillDateUtc)) || (b.id as number) - (a.id as number))
  const items = matches.slice(skip, skip + take).map(fuelItem)
  const byVehicle = new Map<string, Rec[]>()
  for (const r of matches.filter((x) => x.isActive !== false)) byVehicle.set(String(r.vehiclePublicId), [...(byVehicle.get(String(r.vehiclePublicId)) ?? []), r])
  const summary = [...byVehicle.entries()].map(([id, rows]) => {
    const eff = efficiency(id)
    const measured = rows.map((r) => eff.get(r.id as number)).filter((e) => e?.distanceKm != null)
    const distance = measured.reduce((s, e) => s + (e?.distanceKm ?? 0), 0)
    const litersAll = rows.reduce((s, r) => s + num(r.liters), 0)
    const costAll = rows.reduce((s, r) => s + num(r.totalCost), 0)
    return {
      vehiclePublicId: id,
      vehicleCode: rows[0].vehicleCode,
      fills: rows.length,
      totalLiters: litersAll,
      totalCost: costAll,
      distanceKm: measured.length ? distance : null,
      kmPerLiter: measured.length ? distance / litersAll : null,
      costPerKm: measured.length ? costAll / distance : null,
    }
  })
  return { total: matches.length, skip, take, items, summary }
}

/** Lectura monótona por fecha (ni menor que una anterior ni mayor que una posterior), como `FuelEfficiency.ValidateOdometer`. */
function odometerError(vehicle: string, fillDateUtc: string, odometer: number | null, selfId: number | null): string | null {
  if (odometer == null) return null
  const others = maintenanceState.fuel.filter((r) => r.isActive !== false && r.vehiclePublicId === vehicle && r.odometerKm != null && r.id !== selfId)
  const before = others.filter((r) => String(r.fillDateUtc) < fillDateUtc.replace('Z', '')).sort((a, b) => String(b.fillDateUtc).localeCompare(String(a.fillDateUtc)))[0]
  if (before && odometer < num(before.odometerKm)) {
    return `La lectura de odómetro (${odometer} km) es menor que la de una carga anterior del mismo vehículo (${before.odometerKm} km el ${String(before.fillDateUtc).slice(0, 10)}).`
  }
  const after = others.filter((r) => String(r.fillDateUtc) > fillDateUtc.replace('Z', '')).sort((a, b) => String(a.fillDateUtc).localeCompare(String(b.fillDateUtc)))[0]
  if (after && odometer > num(after.odometerKm)) {
    return `La lectura de odómetro (${odometer} km) es mayor que la de una carga posterior del mismo vehículo (${after.odometerKm} km el ${String(after.fillDateUtc).slice(0, 10)}).`
  }
  return null
}

const usesKm = (trigger: unknown) => trigger === 'MILEAGE' || trigger === 'BOTH'

// ---------------------------------------------------------------- rutas
export function routeMaintenance(call: Call): unknown {
  const { method, url, body } = call
  const p = url.pathname
  const q = url.searchParams

  // ---- catálogos ----
  if (p === '/api/v1/catalogs/MaintenanceType') {
    return [
      { code: 'PREVENTIVE', label: 'Preventivo', isEnabled: true, sortOrder: 1 },
      { code: 'CORRECTIVE', label: 'Correctivo', isEnabled: true, sortOrder: 2 },
    ]
  }
  if (p === '/api/v1/catalogs/MaintenanceTrigger') {
    return [
      { code: 'MILEAGE', label: 'Kilometraje', isEnabled: true, sortOrder: 1 },
      { code: 'TIME', label: 'Tiempo', isEnabled: true, sortOrder: 2 },
      { code: 'BOTH', label: 'Ambos', isEnabled: true, sortOrder: 3 },
    ]
  }
  if (p === '/api/v1/status/WorkOrderStatus') return WO_STATUSES
  if (p === '/api/v1/drivers' && method === 'GET') return DRIVERS_LIST

  // ---- programas ----
  if (p === '/api/v1/maintenance-schedules/due') {
    const states = q.getAll('status')
    const vehicle = q.get('vehiclePublicId')
    return DUE.filter((d) => !states.length || states.includes(String(d.state))).filter((d) => !vehicle || d.vehiclePublicId === vehicle)
  }
  if (p === '/api/v1/maintenance-schedules' && method === 'GET') return maintenanceState.schedules.filter((s) => s.isActive !== false || q.get('includeInactive') === 'true')
  if (p === '/api/v1/maintenance-schedules' && method === 'POST') {
    const created: Rec = { id: 30, vehicleCode: body?.vehiclePublicId === 'v-1' ? 'V-01' : undefined, triggerCode: body?.trigger, isActive: true, ...body }
    maintenanceState.schedules.push(created)
    return created
  }
  let m = /^\/api\/v1\/maintenance-schedules\/(\d+)(?:\/(deactivate|reactivate))?$/.exec(p)
  if (m && method !== 'GET') {
    const i = maintenanceState.schedules.findIndex((s) => s.id === Number(m![1]))
    if (i < 0) return problem(404, 'Programa de mantenimiento no encontrado.', 'not_found')
    if (m[2]) {
      maintenanceState.schedules[i] = { ...maintenanceState.schedules[i], isActive: m[2] === 'reactivate' }
      return new Response(null, { status: 204 })
    }
    const next: Rec = { ...maintenanceState.schedules[i] }
    for (const [k, v] of Object.entries(body ?? {})) if (v !== null && !k.startsWith('clear')) next[k] = v
    if (body?.clearIntervalKm) next.intervalKm = undefined
    if (body?.clearIntervalDays) next.intervalDays = undefined
    if (body?.trigger) next.triggerCode = body.trigger
    maintenanceState.schedules[i] = next
    return next
  }

  // ---- órdenes de trabajo ----
  if (p === '/api/v1/maintenance-work-orders' && method === 'GET') {
    const statuses = q.getAll('status')
    const types = q.getAll('maintenanceType')
    const vehicle = q.get('vehiclePublicId')
    const from = q.get('from')
    const to = q.get('to')
    const skip = Number(q.get('skip') ?? 0)
    const take = Number(q.get('take') ?? 100)
    return maintenanceState.orders
      .filter((o) => !statuses.length || statuses.includes(String(o.statusCode)))
      .filter((o) => !types.length || types.includes(String(o.maintenanceTypeCode)))
      .filter((o) => !vehicle || o.vehiclePublicId === vehicle)
      .filter((o) => !from || String(o.scheduledDate ?? o.completedDate ?? '9999') >= from)
      .filter((o) => !to || String(o.scheduledDate ?? o.completedDate ?? '0000') <= to)
      .sort((a, b) => (b.id as number) - (a.id as number))
      .slice(skip, skip + take)
      .map(listItem)
  }
  if (p === '/api/v1/maintenance-work-orders' && method === 'POST') {
    if (!body?.vehiclePublicId) return problem(400, 'Datos inválidos.', 'validation', { vehiclePublicId: ['El vehículo de la orden de trabajo es obligatorio.'] })
    const n = maintenanceState.nextOrder++
    const type = String(body?.maintenanceType ?? 'PREVENTIVE')
    const schedule = body?.scheduleId ? maintenanceState.schedules.find((s) => s.id === body.scheduleId) : undefined
    const created: Rec = {
      id: 100 + n,
      publicId: `wo-new-${n}`,
      number: `OT-${String(n).padStart(5, '0')}`,
      vehiclePublicId: body.vehiclePublicId,
      vehicleCode: body.vehiclePublicId === 'v-1' ? 'V-01' : body.vehiclePublicId === 'v-2' ? 'V-02' : 'V-03',
      maintenanceTypeCode: type,
      maintenanceType: type === 'PREVENTIVE' ? 'Preventivo' : 'Correctivo',
      scheduleId: body.scheduleId ?? undefined,
      scheduleName: schedule?.name,
      scheduleTriggerCode: schedule?.triggerCode,
      statusCode: 'OPEN',
      status: 'Abierta',
      isTerminal: false,
      scheduledDate: body.scheduledDate,
      odometerKm: body.odometerKm,
      vendor: body.vendor,
      laborCost: body.laborCost,
      partsCost: body.partsCost,
      notes: body.notes,
      canEdit: true,
      costsFromTasks: false,
      tasks: [],
      isActive: true,
      rowVersion: `N${n}`,
    }
    maintenanceState.orders.push(created)
    return detail(created)
  }
  m = /^\/api\/v1\/maintenance-work-orders\/([^/]+)(?:\/(status|tasks)(?:\/(\d+)(?:\/(deactivate))?)?)?$/.exec(p)
  if (m) {
    const o = maintenanceState.orders.find((x) => x.publicId === m![1])
    if (!o) return problem(404, 'Orden de trabajo no encontrada.', 'not_found')
    const tasks = o.tasks as Rec[]
    const blocked = o.canEdit !== true
    const editBlocked = () => problem(422, "El estatus actual no permite la acción 'EDIT_WORK_ORDER'.", 'status_rule')
    if (!m[2]) {
      if (method === 'GET') return detail(o)
      if (method === 'PATCH') {
        if (blocked) return editBlocked()
        if (body?.rowVersion && body.rowVersion !== o.rowVersion) return problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict')
        if ((body?.laborCost !== undefined || body?.partsCost !== undefined) && tasks.some((x) => x.isActive !== false)) {
          return problem(400, 'La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas.', 'validation', {
            [body?.laborCost !== undefined ? 'laborCost' : 'partsCost']: ['La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas.'],
          })
        }
        for (const [k, v] of Object.entries(body ?? {})) if (k !== 'rowVersion' && v !== null && v !== undefined) o[k] = v === '' ? undefined : v
        o.rowVersion = `${String(o.rowVersion)}+`
        return detail(o)
      }
    }
    if (m[2] === 'status' && method === 'POST') {
      const to = String(body?.toCode)
      if (body?.rowVersion && body.rowVersion !== o.rowVersion) return problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict')
      if (to === 'CLOSED') {
        const pending = tasks.filter((x) => x.isActive !== false && !x.isCompleted).length
        if (pending > 0) return problem(422, `La orden tiene ${pending} tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.`, 'status_rule')
        const odometer = body?.odometerKm ?? o.odometerKm
        if (usesKm(o.scheduleTriggerCode) && odometer == null) {
          return problem(400, 'Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.', 'validation', { odometerKm: ['Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.'] })
        }
        o.completedDate = body?.completedDate ?? '2026-10-12'
        o.odometerKm = odometer
      }
      o.statusCode = to
      o.status = STATUS_LABEL[to] ?? to
      o.isTerminal = to === 'CLOSED' || to === 'CANCELLED'
      o.canEdit = !o.isTerminal
      o.rowVersion = `${String(o.rowVersion)}+`
      return detail(o)
    }
    if (m[2] === 'tasks' && !m[3] && method === 'POST') {
      if (blocked) return editBlocked()
      tasks.push(task(maintenanceState.nextTask++, String(body?.description ?? ''), (body?.partCost as number) ?? null, (body?.laborCost as number) ?? null, body?.isCompleted === true))
      o.rowVersion = `${String(o.rowVersion)}+`
      return detail(o)
    }
    if (m[2] === 'tasks' && m[3]) {
      const t = tasks.find((x) => x.id === Number(m![3]))
      if (!t) return problem(404, 'Tarea no encontrada.', 'not_found')
      if (blocked) return editBlocked()
      if (m[4]) t.isActive = false
      else for (const [k, v] of Object.entries(body ?? {})) if (v !== null && v !== undefined) t[k] = v
      o.rowVersion = `${String(o.rowVersion)}+`
      return detail(o)
    }
  }

  // ---- combustible ----
  if (p === '/api/v1/fuel-logs' && method === 'GET') return fuelPage(call)
  if (p === '/api/v1/fuel-logs' && method === 'POST') {
    const odoError = odometerError(String(body?.vehiclePublicId), String(body?.fillDateUtc), (body?.odometerKm as number | undefined) ?? null, null)
    if (odoError) return problem(400, 'Datos inválidos.', 'validation', { odometerKm: [odoError] })
    const id = maintenanceState.nextFuel++
    const created: Rec = {
      id,
      vehiclePublicId: body?.vehiclePublicId,
      vehicleCode: body?.vehiclePublicId === 'v-1' ? 'V-01' : 'V-02',
      driverPublicId: body?.driverPublicId,
      driverName: DRIVERS_LIST.find((d) => d.publicId === body?.driverPublicId)?.fullName,
      fillDateUtc: String(body?.fillDateUtc).replace('Z', ''),
      liters: body?.liters,
      totalCost: body?.totalCost,
      odometerKm: body?.odometerKm ?? null,
      station: body?.station,
      isActive: true,
    }
    maintenanceState.fuel.push(created)
    return fuelItem(created)
  }
  m = /^\/api\/v1\/fuel-logs\/(\d+)(?:\/(deactivate))?$/.exec(p)
  if (m && method !== 'GET') {
    const i = maintenanceState.fuel.findIndex((r) => r.id === Number(m![1]))
    if (i < 0) return problem(404, 'Carga de combustible no encontrada.', 'not_found')
    const cur = maintenanceState.fuel[i]
    if (m[2]) {
      maintenanceState.fuel[i] = { ...cur, isActive: false }
      return fuelItem(maintenanceState.fuel[i])
    }
    if (cur.isActive === false) return problem(409, 'La carga de combustible está inactiva; no se puede corregir.', 'conflict')
    const next: Rec = { ...cur }
    if (body?.fillDateUtc) next.fillDateUtc = String(body.fillDateUtc).replace('Z', '')
    if (body?.liters !== undefined) next.liters = body.liters
    if (body?.totalCost !== undefined) next.totalCost = body.totalCost
    if (body?.clearOdometer) next.odometerKm = null
    else if (body?.odometerKm !== undefined) next.odometerKm = body.odometerKm
    if (body?.clearDriver) {
      next.driverPublicId = undefined
      next.driverName = undefined
    } else if (body?.driverPublicId) {
      next.driverPublicId = body.driverPublicId
      next.driverName = DRIVERS_LIST.find((d) => d.publicId === body.driverPublicId)?.fullName
    }
    if (body?.station !== undefined) next.station = body.station || undefined
    const odoError = odometerError(String(next.vehiclePublicId), String(next.fillDateUtc), (next.odometerKm as number | null) ?? null, cur.id as number)
    if (odoError) return problem(400, 'Datos inválidos.', 'validation', { odometerKm: [odoError] })
    maintenanceState.fuel[i] = next
    return fuelItem(next)
  }
  return undefined
}
