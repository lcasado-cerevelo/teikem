// API simulado de las pruebas de la Estación de escaneo (F-E1). No importa la pantalla: la prueba lo carga dentro de
// `vi.mock('../../kernel/api/client', ...)` con `stubFetch`. Los resultados del escaneo se programan por código en `mock.scans`.
export interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
  headers: Headers
}

type Rec = Record<string, unknown>

export const mock = {
  calls: [] as Call[],
  override: null as null | ((call: Call) => unknown),
  /** Resultado por código escaneado (lo no programado es NOT_FOUND / NO_MATCH). */
  scans: {} as Record<string, Rec | Response>,
  /** Órdenes sin asignar que devuelve `unassigned-orders` (se filtran por la consulta). */
  pending: [] as Rec[],
  /** Respuesta de `GET /orders/{id}` por publicId. */
  orders: {} as Record<string, Rec>,
}

export const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

export const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

export const ZONES = [
  { id: 1, code: 'R-01', name: 'Toa Baja · Bayamón', driverCount: 1, isActive: true },
  { id: 2, code: 'R-02', name: 'Caguas · Aguas Buenas', driverCount: 1, isActive: true },
]

export const CLIENTS = [
  { id: 1, publicId: 'c-1', code: 'ADV', name: 'Advance Solutions', isActive: true },
  { id: 2, publicId: 'c-2', code: 'TRU', name: 'Truss Puerto Rico', isActive: true },
]

export const P1: Rec = { id: 11, publicId: 'o-1', orderNumber: 'OT-0001', packBatchNumber: 'PKG-1', clientName: 'Advance Solutions', consigneeName: 'Taller Díaz', city: 'Bayamón', postalCode: '00959', dispatchZoneId: 1, zoneCode: 'R-01', zoneAmbiguous: false, status: 'Confirmada', statusCode: 'CONFIRMED' }
export const P2: Rec = { id: 12, publicId: 'o-2', orderNumber: 'OT-0002', clientName: 'Truss Puerto Rico', consigneeName: 'Ferretería Sol', city: 'Caguas', postalCode: '00725', dispatchZoneId: 2, zoneCode: 'R-02', zoneAmbiguous: false, status: 'Confirmada', statusCode: 'CONFIRMED' }
export const P3: Rec = { id: 13, publicId: 'o-3', orderNumber: 'OT-0003', clientName: 'Advance Solutions', consigneeName: 'Casa Roca', city: 'Vieques', postalCode: '00765', zoneAmbiguous: false, status: 'Confirmada', statusCode: 'CONFIRMED' }

export const assigned = (orderPublicId: string, orderNumber: string, tripCode = 'R-0001'): Rec => ({
  outcome: 'FOUND_ASSIGNED', voice: 'found', message: `Asignada a la ruta ${tripCode}.`, matchedBy: 'ORDER_NUMBER',
  orderPublicId, orderNumber, zoneCode: 'R-01', tripPublicId: 't-1', tripCode, reasonCode: null,
})

export const ORDER_DETAIL: Rec = {
  id: 11, publicId: 'o-1', orderNumber: 'OT-0001', clientName: 'Advance Solutions', serviceTypeLabel: 'Estándar', serviceType: 'STANDARD',
  status: 'PICKUP', statusLabel: 'Recogida', totalPieces: 3, packagesSummary: '3 cajas', codAmount: 125.5,
  delivery: { name: 'Taller Díaz', city: 'Bayamón', postalCode: '00959' }, assignedTripCode: 'R-0001',
}

export function resetMock() {
  mock.calls = []
  mock.override = null
  mock.scans = {}
  mock.pending = [P1, P2, P3]
  mock.orders = { 'o-1': ORDER_DETAIL }
}

const fold = (s: unknown) => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

function route(call: Call): unknown {
  const { method, url } = call
  const p = url.pathname
  const q = url.searchParams
  if (method === 'POST' && p === '/api/v1/scan/outbound') {
    const code = String(call.body?.code ?? '')
    return mock.scans[code] ?? { outcome: 'NOT_FOUND', voice: 'notfound', message: 'No se encontró la orden.', reasonCode: 'NO_MATCH' }
  }
  if (method === 'GET' && p === '/api/v1/trips/unassigned-orders') {
    let items = mock.pending
    if (q.get('DispatchZoneId')) items = items.filter((o) => String(o.dispatchZoneId) === q.get('DispatchZoneId'))
    if (q.get('NoZone') === 'true') items = items.filter((o) => o.dispatchZoneId == null)
    if (q.get('PostalCode')) items = items.filter((o) => String(o.postalCode).startsWith(q.get('PostalCode')!))
    if (q.get('City')) items = items.filter((o) => fold(o.city).includes(fold(q.get('City'))))
    if (q.get('ClientPublicId')) items = items.filter((o) => o.clientName === CLIENTS.find((c) => c.publicId === q.get('ClientPublicId'))?.name)
    return { total: items.length, skip: 0, take: Number(q.get('Take') ?? 100), items }
  }
  if (method === 'GET' && p === '/api/v1/dispatch-zones') return ZONES
  if (method === 'GET' && p === '/api/v1/clients') return CLIENTS
  if (method === 'GET' && p.startsWith('/api/v1/status/')) return []
  const order = /^\/api\/v1\/orders\/([^/]+)$/.exec(p)
  if (method === 'GET' && order) return mock.orders[order[1]] ?? problem(404, 'Orden no encontrada.', 'not_found')
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
