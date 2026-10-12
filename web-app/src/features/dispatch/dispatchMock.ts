// API simulado de las pruebas de la Sala de despacho (F-E2a). No importa la pantalla: la prueba lo carga dentro de
// `vi.mock('../../kernel/api/client', ...)` con `stubFetch`. Lleva un estado mínimo (rutas, órdenes sin asignar) que las escrituras modifican;
// lo que no cubre el estado se programa con `mock.override` (devuelve algo distinto de undefined para contestar una llamada).
import { tenantToday } from '../../kernel/api/tenantZone'

export interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
  headers: Headers
}

type Rec = Record<string, unknown>

export const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

export const stop = (id: number, sequence: number, orderNumber: string, over: Rec = {}): Rec => ({
  id,
  sequence,
  orderPublicId: `o-${orderNumber}`,
  orderNumber,
  consigneeName: `Consignatario ${orderNumber}`,
  city: 'Bayamón',
  postalCode: '00959',
  plannedArrivalUtc: '2026-10-12T13:00:00',
  isApproximate: false,
  lateForWindow: false,
  statusCode: 'PENDING',
  ...over,
})

export function tripDetail(over: Rec = {}): Rec {
  const stops = (over.stops as Rec[] | undefined) ?? []
  return {
    id: 1,
    publicId: 't-1',
    code: '2026-0001',
    planDate: tenantToday(),
    dispatchZoneId: 1,
    zoneCode: 'R-01',
    zoneName: 'Toa Baja',
    driverPublicId: 'd-1',
    driverCode: 'D-01',
    driverName: 'Ana Pérez',
    vehiclePublicId: 'v-1',
    vehicleCode: 'V-01',
    vehiclePlate: 'ABC123',
    statusCode: 'DRAFT',
    status: 'Borrador',
    isEditable: true,
    canEditHeader: true,
    isTerminal: false,
    stopCount: stops.length,
    effectiveMaxStops: 25,
    overStopLimit: false,
    totalWeightKg: 0,
    totalVolumeM3: 0,
    totalDistanceKm: null,
    totalDurationMin: null,
    plannedStartUtc: `${tenantToday()}T12:00:00`,
    plannedEndUtc: null,
    stops,
    issues: [],
    lastRunUnassigned: [],
    isActive: true,
    rowVersion: 'rv-1',
    ...over,
  }
}

const listItem = (d: Rec): Rec => ({
  id: d.id, publicId: d.publicId, code: d.code, planDate: d.planDate, dispatchZoneId: d.dispatchZoneId, zoneCode: d.zoneCode, zoneName: d.zoneName,
  driverPublicId: d.driverPublicId, driverCode: d.driverCode, driverName: d.driverName, vehiclePublicId: d.vehiclePublicId, vehicleCode: d.vehicleCode,
  statusCode: d.statusCode, status: d.status, isEditable: d.isEditable, stopCount: d.stopCount, effectiveMaxStops: d.effectiveMaxStops,
  overStopLimit: d.overStopLimit, isActive: d.isActive,
})

export const ZONES = [
  { id: 1, code: 'R-01', name: 'Toa Baja', driverCount: 1, isActive: true },
  { id: 2, code: 'R-02', name: 'Caguas', driverCount: 1, isActive: true },
]

export const DRIVERS = [
  { id: 1, publicId: 'd-1', code: 'D-01', fullName: 'Ana Pérez', dispatchZoneId: 1, zoneCode: 'R-01', area: 'Toa Baja', maxStopsPerRoute: null, effectiveMaxStops: 25, isActive: true },
  { id: 2, publicId: 'd-2', code: 'D-02', fullName: 'Luis Soto', dispatchZoneId: 2, zoneCode: 'R-02', area: null, maxStopsPerRoute: 12, effectiveMaxStops: 12, isActive: true },
  { id: 3, publicId: 'd-3', code: 'D-03', fullName: 'Rosa Vega', dispatchZoneId: null, zoneCode: null, area: null, maxStopsPerRoute: null, effectiveMaxStops: 25, isActive: true },
]

export const CLIENTS = [{ id: 1, publicId: 'c-1', code: 'ADV', name: 'Advance Solutions', isActive: true }]

export const AVAILABILITY = {
  date: '',
  drivers: [
    { id: 1, publicId: 'd-1', code: 'D-01', fullName: 'Ana Pérez', zoneCode: 'R-01', available: true, issues: [] },
    { id: 2, publicId: 'd-2', code: 'D-02', fullName: 'Luis Soto', zoneCode: 'R-02', available: false, issues: [{ code: 'LICENSE_EXPIRED', message: 'La licencia está vencida.', blocking: true }] },
    { id: 3, publicId: 'd-3', code: 'D-03', fullName: 'Rosa Vega', zoneCode: null, available: true, issues: [] },
  ],
  vehicles: [
    { id: 1, publicId: 'v-1', code: 'V-01', plateNumber: 'ABC123', available: true, issues: [] },
    { id: 2, publicId: 'v-2', code: 'V-02', plateNumber: 'XYZ789', available: true, issues: [] },
  ],
}

export const U1: Rec = { id: 11, publicId: 'u-1', orderNumber: 'OT-0001', clientName: 'Advance Solutions', consigneeName: 'Taller Díaz', city: 'Bayamón', postalCode: '00959', dispatchZoneId: 1, zoneCode: 'R-01', zoneAmbiguous: false }
export const U2: Rec = { id: 12, publicId: 'u-2', orderNumber: 'OT-0002', clientName: 'Advance Solutions', consigneeName: 'Ferretería Sol', city: 'Caguas', postalCode: '00725', dispatchZoneId: 2, zoneCode: 'R-02', zoneAmbiguous: false }
export const U3: Rec = { id: 13, publicId: 'u-3', orderNumber: 'OT-0003', clientName: 'Advance Solutions', consigneeName: 'Casa Roca', city: 'Vieques', postalCode: '00765', zoneAmbiguous: false }

export const mock = {
  calls: [] as Call[],
  override: null as null | ((call: Call) => unknown),
  /** Detalles de las rutas del día (la lista sale de aquí). */
  trips: [] as Rec[],
  unassigned: [] as Rec[],
  drivers: DRIVERS as Rec[],
  dispatchable: [] as Rec[],
  monitor: [] as Rec[],
  runs: [] as Rec[],
  nextId: 100,
}

export const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
export const callsMatching = (method: string, re: RegExp) => mock.calls.filter((c) => c.method === method && re.test(c.url.pathname))

export function resetMock(trips: Rec[] = [], unassigned: Rec[] = [U1, U2, U3]) {
  mock.calls = []
  mock.override = null
  mock.trips = trips
  mock.unassigned = unassigned
  mock.drivers = DRIVERS
  mock.dispatchable = []
  mock.monitor = []
  mock.runs = []
  mock.nextId = 100
}

const fold = (s: unknown) => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
const find = (id: string) => mock.trips.find((x) => x.publicId === id)
const touch = (d: Rec) => {
  d.stopCount = (d.stops as Rec[]).length
  d.rowVersion = `rv-${mock.nextId++}`
  return d
}

function route(call: Call): unknown {
  const { method, url, body } = call
  const p = url.pathname
  const q = url.searchParams
  if (method === 'GET' && p === '/api/v1/trips') return mock.trips.filter((d) => d.isActive !== false).map(listItem)
  if (method === 'GET' && p === '/api/v1/trips/monitor') return { date: q.get('date'), totals: {}, trips: mock.monitor }
  if (method === 'GET' && p === '/api/v1/trips/dispatchable') return mock.dispatchable
  if (method === 'GET' && p === '/api/v1/trips/unassigned-orders') {
    let items = mock.unassigned
    if (q.get('DispatchZoneId')) items = items.filter((o) => String(o.dispatchZoneId) === q.get('DispatchZoneId'))
    if (q.get('NoZone') === 'true') items = items.filter((o) => o.dispatchZoneId == null)
    if (q.get('PostalCode')) items = items.filter((o) => String(o.postalCode).startsWith(q.get('PostalCode')!))
    if (q.get('City')) items = items.filter((o) => fold(o.city).includes(fold(q.get('City'))))
    return { total: items.length, skip: 0, take: Number(q.get('Take') ?? 100), items }
  }
  if (method === 'GET' && p === '/api/v1/dispatch-zones') return ZONES
  if (method === 'GET' && p === '/api/v1/drivers') return mock.drivers
  if (method === 'GET' && p === '/api/v1/fleet/availability') return { ...AVAILABILITY, date: q.get('date') }
  if (method === 'GET' && p === '/api/v1/clients') return CLIENTS
  if (method === 'GET' && p.startsWith('/api/v1/status/')) return []
  if (method === 'POST' && p === '/api/v1/trips') {
    const d = tripDetail({ id: mock.nextId, publicId: `t-new-${mock.nextId}`, code: `2026-${String(mock.nextId).padStart(4, '0')}`, planDate: body?.planDate, dispatchZoneId: body?.dispatchZoneId ?? null, driverPublicId: body?.driverPublicId ?? null, vehiclePublicId: body?.vehiclePublicId ?? null, stops: [] })
    mock.nextId++
    mock.trips.push(d)
    return d
  }
  if (method === 'POST' && p === '/api/v1/trips/dispatch') {
    const ids = (body?.tripPublicIds as string[]) ?? []
    return {
      requested: ids.length,
      dispatched: ids.length,
      items: ids.map((id) => {
        const d = find(id)
        if (d) {
          d.statusCode = 'DISPATCHED'
          d.status = 'Despachada'
          d.isEditable = false
        }
        return { tripPublicId: id, code: d?.code, dispatched: true, error: null, issues: [] }
      }),
    }
  }
  const sub = /^\/api\/v1\/trips\/([^/]+)\/(orders|optimize|start|complete|optimization-runs|route\/sequence)$/.exec(p)
  const orderOf = /^\/api\/v1\/trips\/([^/]+)\/orders\/([^/]+)$/.exec(p)
  const whole = /^\/api\/v1\/trips\/([^/]+)$/.exec(p)
  if (sub) {
    const d = find(sub[1])
    if (!d) return problem(404, 'Ruta no encontrada.', 'not_found')
    const stops = d.stops as Rec[]
    if (sub[2] === 'optimization-runs') return mock.runs
    if (sub[2] === 'orders' && method === 'POST') {
      for (const id of (body?.orderPublicIds as string[]) ?? []) {
        const u = mock.unassigned.find((o) => o.publicId === id)
        if (u) {
          stops.push(stop(mock.nextId++, stops.length + 1, String(u.orderNumber), { orderPublicId: id, consigneeName: u.consigneeName }))
          mock.unassigned = mock.unassigned.filter((o) => o.publicId !== id)
        }
      }
      return touch(d)
    }
    if (sub[2] === 'route/sequence' && method === 'PUT') {
      const ids = (body?.routeStopIds as number[]) ?? []
      d.stops = ids.map((id, i) => ({ ...stops.find((s) => s.id === id), sequence: i + 1 }))
      return touch(d)
    }
    if (sub[2] === 'optimize' && method === 'POST') {
      d.statusCode = 'PLANNED'
      d.status = 'Planificada'
      touch(d)
      return { runId: 1, engineCode: 'HEURISTIC', statusCode: 'OK', routeVersion: 2, assignedCount: stops.length, unassignedCount: 0, unassigned: [], trip: d }
    }
    if (sub[2] === 'start' && method === 'POST') {
      d.statusCode = 'IN_PROGRESS'
      d.status = 'En curso'
      return touch(d)
    }
    if (sub[2] === 'complete' && method === 'POST') {
      d.statusCode = 'COMPLETED'
      d.status = 'Completada'
      d.isTerminal = true
      return touch(d)
    }
  }
  if (orderOf && method === 'DELETE') {
    const d = find(orderOf[1])
    if (!d) return problem(404, 'Ruta no encontrada.', 'not_found')
    d.stops = (d.stops as Rec[]).filter((s) => s.orderPublicId !== orderOf[2]).map((s, i) => ({ ...s, sequence: i + 1 }))
    return touch(d)
  }
  if (whole && method === 'GET') return find(whole[1]) ?? problem(404, 'Ruta no encontrada.', 'not_found')
  if (whole && method === 'PATCH') {
    const d = find(whole[1])
    if (!d) return problem(404, 'Ruta no encontrada.', 'not_found')
    if (body?.driverPublicId) {
      const dr = DRIVERS.find((x) => x.publicId === body.driverPublicId)
      d.driverPublicId = dr?.publicId
      d.driverCode = dr?.code
      d.driverName = dr?.fullName
    }
    if (body?.clearDriver) d.driverPublicId = d.driverCode = d.driverName = null
    if (body?.vehiclePublicId) {
      const v = AVAILABILITY.vehicles.find((x) => x.publicId === body.vehiclePublicId)
      d.vehiclePublicId = v?.publicId
      d.vehicleCode = v?.code
      d.vehiclePlate = v?.plateNumber
    }
    if (body?.clearVehicle) d.vehiclePublicId = d.vehicleCode = d.vehiclePlate = null
    return touch(d)
  }
  if (whole && method === 'DELETE') {
    const d = find(whole[1])
    if (d) d.isActive = false
    return new Response(null, { status: 204 })
  }
  return problem(404, `Sin simular: ${method} ${p}`, 'not_found')
}

export async function stubFetch(req: Request): Promise<Response> {
  const text = req.method === 'GET' ? '' : await req.text()
  let body: Record<string, unknown> | undefined
  try {
    body = text ? (JSON.parse(text) as Record<string, unknown>) : undefined
  } catch {
    body = undefined
  }
  const call: Call = { method: req.method, url: new URL(req.url), body, headers: req.headers }
  mock.calls.push(call)
  const out = mock.override?.(call) ?? route(call)
  return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
