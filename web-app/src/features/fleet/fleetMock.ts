// API simulado de las pruebas de Flota (F-D2), con estado (vehículos, documentos, zonas) y los datos de ejemplo. No importa la
// pantalla: cada archivo de prueba lo carga dentro de `vi.mock('../../kernel/api/client', ...)` con `stubFetch`.
import { resetMaintenance, routeMaintenance } from './maintenanceMock'

export interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}

export const mock = {
  calls: [] as Call[],
  override: null as null | ((call: Call) => unknown),
}

export const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

export const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

type Rec = Record<string, unknown>

// ---------------------------------------------------------------- datos de ejemplo
const DOCS: Rec[] = [
  { id: 1, docTypeCode: 'REGISTRATION', docType: 'Registro', docNumber: 'R-100', issuedDate: '2026-01-01', expiryDate: '2027-06-01', expiryState: 'OK', daysToExpiry: 232, isSuperseded: false, isActive: true },
  { id: 2, docTypeCode: 'INSURANCE', docType: 'Seguro', docNumber: 'S-7', expiryDate: '2026-10-20', expiryState: 'EXPIRING', daysToExpiry: 8, isSuperseded: false, isActive: true },
  { id: 3, docTypeCode: 'INSPECTION', docType: 'Inspección', docNumber: 'I-3', expiryDate: '2026-09-01', expiryState: 'EXPIRED', daysToExpiry: -41, isSuperseded: false, isActive: true },
  { id: 4, docTypeCode: 'REGISTRATION', docType: 'Registro', docNumber: 'R-99', expiryDate: '2026-11-01', expiryState: 'OK', daysToExpiry: 20, isSuperseded: true, isActive: true },
]

export const V1: Rec = {
  id: 1,
  publicId: 'v-1',
  code: 'V-01',
  plateNumber: 'HGR-204',
  vehicleTypeCode: 'VAN',
  vehicleType: 'Van',
  ownershipCode: 'OWNED',
  ownership: 'Propio',
  fuelTypeCode: 'GASOLINE',
  fuelType: 'Gasolina',
  make: 'Ford',
  model: 'Transit',
  modelYear: 2022,
  vin: '1FTBW3XM5JKA12345',
  maxWeightKg: 1200,
  maxVolumeM3: 9,
  maxStops: 40,
  currentOdometerKm: 48210,
  statusCode: 'ACTIVE',
  status: 'Activo',
  isTerminal: false,
  isActive: true,
  documents: DOCS,
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'AAA=',
}
export const V2: Rec = {
  id: 2,
  publicId: 'v-2',
  code: 'V-02',
  plateNumber: 'HGP-118',
  vehicleTypeCode: 'TRUCK',
  vehicleType: 'Camión',
  ownershipCode: 'LEASED',
  ownership: 'Arrendado',
  fuelTypeCode: 'DIESEL',
  fuelType: 'Diésel',
  currentOdometerKm: 102340,
  statusCode: 'MAINTENANCE',
  status: 'En mant.',
  isTerminal: false,
  isActive: true,
  documents: [],
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'BBB=',
}
export const V3: Rec = {
  id: 3,
  publicId: 'v-3',
  code: 'V-03',
  plateNumber: 'HFS-902',
  vehicleTypeCode: 'VAN',
  vehicleType: 'Van',
  statusCode: 'ACTIVE',
  status: 'Activo',
  isTerminal: false,
  isActive: false,
  documents: [],
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'CCC=',
}
export const V4: Rec = {
  id: 4,
  publicId: 'v-4',
  code: 'V-04',
  plateNumber: 'OLD-001',
  vehicleTypeCode: 'TRUCK',
  vehicleType: 'Camión',
  statusCode: 'INACTIVE',
  status: 'Inactivo',
  isTerminal: true,
  isActive: false,
  documents: [],
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'DDD=',
}
const ALL_VEHICLES = [V1, V2, V3, V4]

export const ZONES = [
  { id: 1, code: 'R-01', name: 'Toa Baja · Bayamón', driverCount: 2, isActive: true },
  { id: 2, code: 'R-02', name: 'Caguas · Aguas Buenas', driverCount: 0, isActive: true },
  { id: 3, code: 'R-09', name: 'Zona vieja', driverCount: 0, isActive: false },
]

export const EXPIRING = [
  { rowKey: 'VD-3', ownerKind: 'VEHICLE', ownerPublicId: 'v-1', ownerCode: 'V-01', ownerName: 'Ford Transit · HGR-204', documentKind: 'INSPECTION', documentTypeCode: 'INSPECTION', documentType: 'Inspección', docNumber: 'I-3', expiryDate: '2026-09-01', daysToExpiry: -41, expiryState: 'EXPIRED' },
  { rowKey: 'DL-5', ownerKind: 'DRIVER', ownerPublicId: 'd-1', ownerCode: '010', ownerName: 'Ramón Díaz', documentKind: 'LICENSE', documentTypeCode: 'CDL_A', documentType: 'CDL-A', docNumber: 'L-100', expiryDate: '2026-10-20', daysToExpiry: 8, expiryState: 'EXPIRING' },
  { rowKey: 'VD-2', ownerKind: 'VEHICLE', ownerPublicId: 'v-1', ownerCode: 'V-01', ownerName: 'Ford Transit · HGR-204', documentKind: 'INSURANCE', documentTypeCode: 'INSURANCE', documentType: 'Seguro', docNumber: 'S-7', expiryDate: '2026-11-05', daysToExpiry: 24, expiryState: 'EXPIRING' },
  { rowKey: 'DC-9', ownerKind: 'DRIVER', ownerPublicId: 'd-2', ownerCode: '001', ownerName: 'José Pérez', documentKind: 'CERTIFICATION', documentTypeCode: 'HAZMAT', documentType: 'HazMat', docNumber: 'H-1', expiryDate: '2026-12-20', daysToExpiry: 69, expiryState: 'EXPIRING' },
]

export const AVAILABILITY = {
  date: '2026-10-12',
  drivers: [
    { id: 1, publicId: 'd-1', code: '010', fullName: 'Ramón Díaz', zoneCode: 'R-01', available: true, issues: [{ code: 'DOC_EXPIRING', message: 'Licencia CDL-A vence el 2026-10-20.', blocking: false }] },
    {
      id: 2,
      publicId: 'd-2',
      code: '001',
      fullName: 'José Pérez',
      available: false,
      issues: [
        { code: 'CERT_EXPIRED', message: 'Certificación HazMat vencida el 2026-09-01.', blocking: false },
        { code: 'DRIVER_STATUS', message: "Chofer en estatus 'No disponible'.", blocking: true },
        { code: 'NO_VALID_LICENSE', message: 'Licencia vencida el 2026-08-30.', blocking: true },
      ],
    },
  ],
  vehicles: [
    { id: 1, publicId: 'v-1', code: 'V-01', plateNumber: 'HGR-204', vehicleTypeCode: 'VAN', available: false, issues: [{ code: 'VEHICLE_DOC_EXPIRED', message: 'Inspección vencido el 2026-09-01.', blocking: true }] },
    { id: 2, publicId: 'v-2', code: 'V-02', plateNumber: 'HGP-118', vehicleTypeCode: 'TRUCK', available: false, issues: [{ code: 'WORK_ORDER_IN_PROGRESS', message: 'Orden de trabajo WO-501 en proceso.', blocking: true }, { code: 'VEHICLE_STATUS', message: "Vehículo en estatus 'En mant.'.", blocking: true }] },
    { id: 3, publicId: 'v-3', code: 'V-05', plateNumber: 'AAA-111', vehicleTypeCode: 'VAN', available: true, issues: [{ code: 'VEHICLE_NO_DOCUMENTS', message: 'El vehículo no tiene documentos registrados.', blocking: false }] },
  ],
}

// ---------------------------------------------------------------- estado mutable del API simulado
export const state = {
  vehicles: {} as Record<string, Rec>,
  zones: [] as Rec[],
}

export function resetKit() {
  mock.calls = []
  mock.override = null
  state.vehicles = Object.fromEntries(ALL_VEHICLES.map((v) => [String(v.publicId), { ...v, documents: (v.documents as Rec[]).map((d) => ({ ...d })) }]))
  state.zones = ZONES.map((z) => ({ ...z }))
  resetMaintenance()
}

const listItem = (v: Rec) => ({
  id: v.id,
  publicId: v.publicId,
  code: v.code,
  plateNumber: v.plateNumber,
  vehicleTypeCode: v.vehicleTypeCode,
  vehicleType: v.vehicleType,
  ownershipCode: v.ownershipCode,
  ownership: v.ownership,
  fuelTypeCode: v.fuelTypeCode,
  fuelType: v.fuelType,
  make: v.make,
  model: v.model,
  statusCode: v.statusCode,
  status: v.status,
  isActive: v.isActive,
})

export function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  const q = url.searchParams

  // ---- mantenimiento, órdenes de trabajo y combustible (F-D3) ----
  const maintenance = routeMaintenance({ method, url, body })
  if (maintenance !== undefined) return maintenance

  // ---- vehículos ----
  if (p === '/api/v1/vehicles' && method === 'GET') {
    const inactive = q.get('includeInactive') === 'true'
    const search = (q.get('search') ?? '').toLowerCase()
    const pick = (name: string) => q.getAll(name)
    return Object.values(state.vehicles)
      .filter((v) => inactive || v.isActive)
      .filter((v) => !pick('vehicleType').length || pick('vehicleType').includes(String(v.vehicleTypeCode)))
      .filter((v) => !pick('ownership').length || pick('ownership').includes(String(v.ownershipCode)))
      .filter((v) => !pick('fuelType').length || pick('fuelType').includes(String(v.fuelTypeCode)))
      .filter((v) => !pick('status').length || pick('status').includes(String(v.statusCode)))
      .filter((v) => !search || `${v.code} ${v.plateNumber} ${v.vehicleType ?? ''} ${v.make ?? ''}`.toLowerCase().includes(search))
      .map(listItem)
  }
  if (p === '/api/v1/vehicles' && method === 'POST') {
    const created: Rec = {
      id: 20,
      publicId: 'v-new',
      code: body?.code,
      plateNumber: body?.plateNumber ?? undefined,
      statusCode: 'ACTIVE',
      status: 'Activo',
      isTerminal: false,
      isActive: true,
      documents: [],
      createdAtUtc: '2026-10-12T10:00:00',
      rowVersion: 'NEW=',
    }
    state.vehicles['v-new'] = created
    return created
  }
  let m = /^\/api\/v1\/vehicles\/([^/]+)$/.exec(p)
  if (m) {
    const v = state.vehicles[m[1]]
    if (!v) return problem(404, 'Vehículo no encontrado.', 'not_found')
    if (method === 'GET') return v
    if (method === 'PATCH') {
      const patch: Rec = { ...v, rowVersion: 'ZZZ=' }
      for (const k of ['plateNumber', 'make', 'model', 'vin', 'modelYear', 'maxWeightKg', 'maxVolumeM3', 'maxStops', 'currentOdometerKm']) if (body?.[k] !== undefined) patch[k] = body[k]
      if (body?.vehicleType !== undefined) patch.vehicleTypeCode = body.vehicleType
      state.vehicles[m[1]] = patch
      return patch
    }
  }
  m = /^\/api\/v1\/vehicles\/([^/]+)\/(deactivate|reactivate)$/.exec(p)
  if (m && method === 'POST') {
    const v = state.vehicles[m[1]]
    if (v) state.vehicles[m[1]] = { ...v, isActive: m[2] === 'reactivate' }
    return new Response(null, { status: 204 })
  }
  m = /^\/api\/v1\/vehicles\/([^/]+)\/status$/.exec(p)
  if (m && method === 'POST') {
    const v = state.vehicles[m[1]]
    const to = String(body?.toCode)
    const label = { ACTIVE: 'Activo', MAINTENANCE: 'En mant.', INACTIVE: 'Inactivo' }[to] ?? to
    state.vehicles[m[1]] = { ...v, statusCode: to, status: label, isTerminal: to === 'INACTIVE', isActive: to === 'INACTIVE' ? false : v.isActive }
    return state.vehicles[m[1]]
  }
  m = /^\/api\/v1\/vehicles\/([^/]+)\/documents(?:\/(\d+)(?:\/(deactivate))?)?$/.exec(p)
  if (m && method !== 'GET') {
    if (m[3]) return { id: Number(m[2]), isActive: false }
    return { id: m[2] ? Number(m[2]) : 99, ...body, expiryState: 'OK', isSuperseded: false, isActive: true }
  }

  // ---- documentos por vencer y disponibilidad ----
  if (p === '/api/v1/fleet/expiring-documents') {
    const within = Number(q.get('withinDays') ?? 30)
    const kinds = q.getAll('docType')
    const entities = q.getAll('entity')
    const expired = q.get('includeExpired') !== 'false'
    return EXPIRING.filter((d) => d.daysToExpiry <= within && (expired || d.daysToExpiry >= 0))
      .filter((d) => !kinds.length || kinds.includes(d.documentKind) || kinds.includes(d.documentTypeCode))
      .filter((d) => !entities.length || entities.includes(d.ownerKind))
  }
  if (p === '/api/v1/fleet/availability') {
    const only = q.get('onlyAvailable') === 'true'
    return {
      ...AVAILABILITY,
      date: q.get('date') ?? AVAILABILITY.date,
      drivers: AVAILABILITY.drivers.filter((d) => !only || d.available),
      vehicles: AVAILABILITY.vehicles.filter((v) => !only || v.available),
    }
  }

  // ---- zonas ----
  if (p === '/api/v1/dispatch-zones' && method === 'GET') return state.zones.filter((z) => q.get('includeInactive') === 'true' || z.isActive)
  if (p === '/api/v1/dispatch-zones' && method === 'POST') {
    const z = { id: 30, code: String(body?.code), name: body?.name || undefined, driverCount: 0, isActive: true }
    state.zones.push(z)
    return z
  }
  m = /^\/api\/v1\/dispatch-zones\/(\d+)(?:\/(deactivate|reactivate))?$/.exec(p)
  if (m && method !== 'GET') {
    const i = state.zones.findIndex((z) => z.id === Number(m![1]))
    if (i < 0) return problem(404, 'Zona de despacho no encontrada.', 'not_found')
    if (m[2]) state.zones[i] = { ...state.zones[i], isActive: m[2] === 'reactivate' }
    else state.zones[i] = { ...state.zones[i], name: body?.name }
    return state.zones[i]
  }

  // ---- catálogos, estatus, contactos, campos personalizados ----
  if (p === '/api/v1/catalogs/VehicleType') return [{ code: 'VAN', label: 'Van', isEnabled: true, sortOrder: 1 }, { code: 'TRUCK', label: 'Camión', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/catalogs/Ownership') return [{ code: 'OWNED', label: 'Propio', isEnabled: true, sortOrder: 1 }, { code: 'LEASED', label: 'Arrendado', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/catalogs/FuelType') return [{ code: 'DIESEL', label: 'Diésel', isEnabled: true, sortOrder: 1 }, { code: 'GASOLINE', label: 'Gasolina', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/catalogs/VehicleDocType') {
    return [
      { code: 'REGISTRATION', label: 'Registro', isEnabled: true, sortOrder: 1 },
      { code: 'INSURANCE', label: 'Seguro', isEnabled: true, sortOrder: 2 },
      { code: 'INSPECTION', label: 'Inspección', isEnabled: true, sortOrder: 3 },
      { code: 'PERMIT', label: 'Permiso', isEnabled: true, sortOrder: 4 },
    ]
  }
  if (p === '/api/v1/catalogs/ContactType') return [{ code: 'PHONE', label: 'Teléfono', isEnabled: true }, { code: 'MOBILE', label: 'Móvil', isEnabled: true }, { code: 'EMAIL', label: 'Correo', isEnabled: true }]
  if (p === '/api/v1/tenant/settings') return { id: 1 }
  if (p.startsWith('/api/v1/status/history/')) return []
  if (p.startsWith('/api/v1/status/')) return []
  if (p.startsWith('/api/v1/contacts/VEHICLE/') && method === 'GET') return []
  if (p.startsWith('/api/v1/contacts/')) return { id: 77, ...body }
  if (p.startsWith('/api/v1/custom-fields/')) return []
  if (p.startsWith('/api/v1/catalogs/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

/** `fetch` simulado para `createApiClient`: registra cada llamada y responde con `mock.override` o con `route`. */
export async function stubFetch(req: Request): Promise<Response> {
  const text = req.method === 'GET' ? '' : await req.text()
  let body: Record<string, unknown> | undefined
  try {
    body = text ? (JSON.parse(text) as Record<string, unknown>) : undefined
  } catch {
    body = undefined
  }
  const call: Call = { method: req.method, url: new URL(req.url), body }
  mock.calls.push(call)
  const out = mock.override?.(call) ?? route(call)
  return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
