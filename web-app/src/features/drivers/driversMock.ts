// API simulado de las pruebas de Choferes (F-D1), con estado (choferes, tarifas, política, viajes) y los datos de ejemplo. No importa
// la pantalla: cada archivo de prueba lo carga dentro de `vi.mock('../../kernel/api/client', ...)` con `stubFetch`.
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

// ---------------------------------------------------------------- datos de ejemplo
export const ZONES = [
  { id: 1, code: 'R-01', name: 'Toa Baja · Bayamón', driverCount: 1, isActive: true },
  { id: 2, code: 'R-02', name: 'Caguas · Aguas Buenas', driverCount: 1, isActive: true },
]

type Rec = Record<string, unknown>

const LICENSES: Rec[] = [
  { id: 1, licenseClassCode: 'CDL_A', licenseClass: 'CDL-A', licenseNumber: 'L-100', issuedDate: '2025-01-01', expiryDate: '2028-01-01', expiryState: 'OK', daysToExpiry: 600, isSuperseded: false, isActive: true },
  { id: 2, licenseClassCode: 'CDL_B', licenseClass: 'CDL-B', licenseNumber: 'B-7', expiryDate: '2026-10-20', expiryState: 'EXPIRING', daysToExpiry: 8, isSuperseded: false, isActive: true },
  { id: 3, licenseClassCode: 'REGULAR', licenseClass: 'Regular', licenseNumber: 'R-1', expiryDate: '2026-09-01', expiryState: 'EXPIRED', daysToExpiry: -41, isSuperseded: false, isActive: true },
  { id: 4, licenseClassCode: 'CDL_A', licenseClass: 'CDL-A', licenseNumber: 'L-99', expiryDate: '2026-12-01', expiryState: 'OK', daysToExpiry: 50, isSuperseded: true, isActive: true },
]
const CERTIFICATIONS: Rec[] = [{ id: 1, certTypeCode: 'HAZMAT', certType: 'HazMat', certNumber: 'H-1', expiryState: 'NO_EXPIRY', isSuperseded: false, isActive: true }]
const DEVICES: Rec[] = [{ id: 1, platformCode: 'ANDROID', platform: 'Android', appVersion: '1.2.0', hasPushToken: true, lastSeenUtc: '2026-10-11T12:00:00', isActive: true }]

export const D1: Rec = {
  id: 1,
  publicId: 'd-1',
  code: '010',
  fullName: 'Ramón Díaz',
  dispatchZoneId: 1,
  zoneCode: 'R-01',
  area: 'Toa Baja · Bayamón',
  effectiveMaxStops: 25,
  hireDate: '2024-03-01',
  statusCode: 'ACTIVE',
  status: 'Activo',
  isTerminal: false,
  isActive: true,
  licenses: LICENSES,
  certifications: CERTIFICATIONS,
  devices: DEVICES,
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'AAA=',
}
export const D2: Rec = {
  id: 2,
  publicId: 'd-2',
  code: '001',
  fullName: 'José Pérez',
  dispatchZoneId: 2,
  zoneCode: 'R-02',
  area: 'Caguas · Aguas Buenas',
  maxStopsPerRoute: 20,
  effectiveMaxStops: 20,
  statusCode: 'UNAVAILABLE',
  status: 'No disponible',
  isTerminal: false,
  isActive: true,
  user: { userId: 5, email: 'ana@x.com', fullName: 'Ana Cruz' },
  licenses: [],
  certifications: [],
  devices: [],
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'BBB=',
}
export const D3: Rec = {
  id: 3,
  publicId: 'd-3',
  code: '814',
  fullName: 'Ana Cruz',
  effectiveMaxStops: 25,
  statusCode: 'ACTIVE',
  status: 'Activo',
  isTerminal: false,
  isActive: false,
  licenses: [],
  certifications: [],
  devices: [],
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'CCC=',
}
export const D4: Rec = {
  id: 4,
  publicId: 'd-4',
  code: '823',
  fullName: 'Libre Eliminado',
  effectiveMaxStops: 25,
  statusCode: 'INACTIVE',
  status: 'Inactivo',
  isTerminal: true,
  isActive: false,
  licenses: [],
  certifications: [],
  devices: [],
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'DDD=',
}
const ALL_DRIVERS = [D1, D2, D3, D4]

const listItem = (d: Rec) => ({
  id: d.id,
  publicId: d.publicId,
  code: d.code,
  fullName: d.fullName,
  dispatchZoneId: d.dispatchZoneId,
  zoneCode: d.zoneCode,
  area: d.area,
  maxStopsPerRoute: d.maxStopsPerRoute,
  effectiveMaxStops: d.effectiveMaxStops,
  statusCode: d.statusCode,
  status: d.status,
  isActive: d.isActive,
  hasUser: Boolean(d.user),
})

export const TRIP_TYPES = [
  { id: 4, name: 'Vagón del muelle' },
  { id: 5, name: 'Traslado especial' },
]

export const RATES = {
  driverPublicId: 'd-1',
  driverCode: '010',
  driverName: 'Ramón Díaz',
  readOnly: false,
  asOf: '2026-10-12',
  attemptLevels: 2,
  payoutFormulaCode: 'DELIVERY_PLUS_ATTEMPTS',
  deliveryRates: [
    { id: 11, serviceTypeCode: 'NEXT_DAY', serviceType: 'Next day', packageTypeCode: 'BOX', packageType: 'Box', rate: 6, effectiveFrom: '2026-01-01', isCurrent: true },
    { id: 12, serviceTypeCode: 'STANDARD', serviceType: 'Standard', packageTypeCode: 'ENVELOPE', packageType: 'Envelope', rate: 5.5, effectiveFrom: '2026-01-01', isCurrent: true },
  ],
  attemptRates: [
    { attemptNumber: 1, id: 21, rate: 0, effectiveFrom: '2026-01-01', isCurrent: true },
    { attemptNumber: 2, isCurrent: false },
  ],
  tripRates: [{ id: 31, specialServiceTypeId: 4, tripType: 'Vagón del muelle', tripTypeActive: true, rate: 75, effectiveFrom: '2026-01-01', isCurrent: true }],
}

export const RATES_HISTORY = {
  ...RATES,
  deliveryRates: [
    { id: 10, serviceTypeCode: 'NEXT_DAY', serviceType: 'Next day', packageTypeCode: 'BOX', packageType: 'Box', rate: 5, effectiveFrom: '2025-01-01', effectiveTo: '2026-01-01', isCurrent: false },
    ...RATES.deliveryRates,
  ],
  tripRates: [
    { id: 30, specialServiceTypeId: 4, tripType: 'Vagón del muelle', tripTypeActive: true, rate: 60, effectiveFrom: '2025-01-01', effectiveTo: '2026-01-01', isCurrent: false },
    ...RATES.tripRates,
  ],
}

export const TRIPS = [
  { id: 1, publicId: 't-1', driverPublicId: 'd-1', driverCode: '010', specialServiceTypeId: 4, tripType: 'Vagón del muelle', tripDate: '2026-10-10', amount: 75, rateMissing: false, statusCode: 'OPEN', status: 'Por liquidar', notes: 'Muelle 3', isActive: true, createdAtUtc: '2026-10-10T10:00:00' },
  {
    id: 2,
    publicId: 't-2',
    driverPublicId: 'd-1',
    driverCode: '010',
    specialServiceTypeId: 5,
    tripType: 'Traslado especial',
    tripDate: '2026-10-09',
    amount: 0,
    rateMissing: true,
    note: 'sin tarifa configurada',
    orderPublicId: 'o-1',
    orderNumber: 'ORD-0001',
    statusCode: 'OPEN',
    status: 'Por liquidar',
    isActive: true,
    createdAtUtc: '2026-10-09T10:00:00',
  },
]

export const POLICY = {
  attemptLevels: 2,
  payoutFormulaCode: 'DELIVERY_PLUS_ATTEMPTS',
  payoutFormula: 'Entrega + cada intento',
  formulas: [
    { code: 'DELIVERY_PLUS_ATTEMPTS', label: 'Entrega + cada intento' },
    { code: 'DELIVERY_INCLUDES_FIRST', label: 'La entrega incluye el 1er intento' },
    { code: 'FAILED_REPLACES_DELIVERY', label: 'Intento fallido reemplaza a entrega' },
  ],
  isDefault: true,
}

// ---------------------------------------------------------------- estado mutable del API simulado
export const state = {
  drivers: {} as Record<string, Rec>,
  policy: { ...POLICY } as Rec,
}

export function resetKit() {
  mock.calls = []
  mock.override = null
  state.drivers = Object.fromEntries(ALL_DRIVERS.map((d) => [String(d.publicId), { ...d }]))
  state.policy = { ...POLICY }
}

function previewFor(body: Rec | undefined) {
  const deliveryRate = body?.deliveryRate as number | null | undefined
  const rates = new Map(((body?.attemptRates as { number: number; rate: number }[] | undefined) ?? []).map((r) => [r.number, r.rate]))
  const lines = [
    { kind: 'DELIVERY', attemptNumber: 2, amount: deliveryRate ?? 0, note: deliveryRate == null ? 'sin tarifa configurada' : null },
    ...[1, 2].map((n) => ({ kind: 'ATTEMPT', attemptNumber: n, amount: rates.get(n) ?? 0, note: rates.has(n) ? null : 'sin tarifa configurada' })),
  ]
  return { formulaCode: state.policy.payoutFormulaCode, total: lines.reduce((s, l) => s + l.amount, 0), lines }
}

export function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  const q = url.searchParams

  // ---- choferes ----
  if (p === '/api/v1/drivers' && method === 'GET') {
    const inactive = q.get('includeInactive') === 'true'
    const search = (q.get('search') ?? '').toLowerCase()
    const statuses = q.getAll('status')
    const zone = q.get('dispatchZoneId')
    return Object.values(state.drivers)
      .filter((d) => (inactive || d.isActive) && (!statuses.length || statuses.includes(String(d.statusCode))) && (!zone || String(d.dispatchZoneId) === zone))
      .filter((d) => !search || `${d.code} ${d.fullName} ${d.zoneCode ?? ''} ${d.area ?? ''}`.toLowerCase().includes(search))
      .map(listItem)
  }
  if (p === '/api/v1/drivers' && method === 'POST') {
    const created: Rec = {
      id: 20,
      publicId: 'd-new',
      code: body?.code,
      fullName: body?.fullName,
      dispatchZoneId: body?.dispatchZoneId ?? undefined,
      effectiveMaxStops: 25,
      statusCode: 'ACTIVE',
      status: 'Activo',
      isTerminal: false,
      isActive: true,
      licenses: [],
      certifications: [],
      devices: [],
      createdAtUtc: '2026-10-12T10:00:00',
      rowVersion: 'NEW=',
    }
    state.drivers['d-new'] = created
    return created
  }
  let m = /^\/api\/v1\/drivers\/([^/]+)$/.exec(p)
  if (m) {
    const d = state.drivers[m[1]]
    if (!d) return problem(404, 'Chofer no encontrado.', 'not_found')
    if (method === 'GET') return d
    if (method === 'PATCH') {
      const patch: Rec = { ...d, rowVersion: 'ZZZ=' }
      if (body?.fullName !== undefined) patch.fullName = body.fullName
      if (body?.maxStopsPerRoute !== undefined) patch.maxStopsPerRoute = body.maxStopsPerRoute
      if (body?.clearMaxStopsPerRoute) delete patch.maxStopsPerRoute
      if (body?.dispatchZoneId !== undefined) {
        const z = ZONES.find((x) => x.id === body.dispatchZoneId)
        Object.assign(patch, { dispatchZoneId: z?.id, zoneCode: z?.code, area: z?.name })
      }
      if (body?.clearZone) Object.assign(patch, { dispatchZoneId: undefined, zoneCode: undefined, area: undefined })
      if (body?.clearUser) delete patch.user
      if (body?.userId !== undefined) patch.user = { userId: body.userId, email: 'carlos@x.com', fullName: 'Carlos Rivera' }
      state.drivers[m[1]] = patch
      return patch
    }
    if (method === 'DELETE') {
      state.drivers[m[1]] = { ...d, statusCode: 'INACTIVE', status: 'Inactivo', isTerminal: true, isActive: false }
      return new Response(null, { status: 204 })
    }
  }
  m = /^\/api\/v1\/drivers\/([^/]+)\/(deactivate|reactivate)$/.exec(p)
  if (m && method === 'POST') {
    const d = state.drivers[m[1]]
    if (d) state.drivers[m[1]] = { ...d, isActive: m[2] === 'reactivate' }
    return new Response(null, { status: 204 })
  }
  m = /^\/api\/v1\/drivers\/([^/]+)\/status$/.exec(p)
  if (m && method === 'POST') {
    const d = state.drivers[m[1]]
    const to = String(body?.toCode)
    state.drivers[m[1]] = { ...d, statusCode: to, status: to === 'UNAVAILABLE' ? 'No disponible' : 'Activo' }
    return state.drivers[m[1]]
  }
  // licencias, certificaciones, dispositivos
  m = /^\/api\/v1\/drivers\/([^/]+)\/(licenses|certifications)(?:\/(\d+)(?:\/(deactivate))?)?$/.exec(p)
  if (m && method !== 'GET') {
    if (m[4]) return { id: Number(m[3]), isActive: false }
    return { id: m[3] ? Number(m[3]) : 99, ...body, expiryState: 'OK', isSuperseded: false, isActive: true }
  }
  m = /^\/api\/v1\/drivers\/([^/]+)\/devices\/(\d+)\/deactivate$/.exec(p)
  if (m && method === 'POST') return { id: Number(m[2]), isActive: false }

  // ---- tarifas ----
  m = /^\/api\/v1\/drivers\/([^/]+)\/rates$/.exec(p)
  if (m && method === 'GET') {
    const d = state.drivers[m[1]]
    const base = q.get('includeHistory') === 'true' ? RATES_HISTORY : RATES
    return { ...base, driverPublicId: m[1], readOnly: Boolean(d?.isTerminal) }
  }
  m = /^\/api\/v1\/drivers\/([^/]+)\/(delivery-rates|trip-rates)(?:\/(\d+)(\/close)?)?$/.exec(p)
  if (m && method !== 'GET') return { id: m[3] ? Number(m[3]) : 90, ...body, isCurrent: true }
  m = /^\/api\/v1\/drivers\/([^/]+)\/attempt-rates\/(\d+)(\/close)?$/.exec(p)
  if (m && (method === 'PUT' || method === 'POST')) return { attemptNumber: Number(m[2]), id: 50, ...body, isCurrent: true }
  if (p === '/api/v1/driver-trip-types') return TRIP_TYPES

  // ---- política y vista previa ----
  if (p === '/api/v1/driver-pay-policy' && method === 'GET') return state.policy
  if (p === '/api/v1/driver-pay-policy' && method === 'PATCH') {
    const code = String(body?.payoutFormula)
    state.policy = { ...state.policy, payoutFormulaCode: code, payoutFormula: (POLICY.formulas.find((f) => f.code === code)?.label ?? code) as string, isDefault: false }
    return state.policy
  }
  if (p === '/api/v1/driver-pay-policy/attempt-levels' && method === 'POST') {
    state.policy = { ...state.policy, attemptLevels: Number(state.policy.attemptLevels) + 1 }
    return state.policy
  }
  if (p === '/api/v1/driver-pay-policy/preview' && method === 'POST') return previewFor(body)

  // ---- viajes ----
  m = /^\/api\/v1\/drivers\/([^/]+)\/trips$/.exec(p)
  if (m && method === 'GET') {
    const wanted = q.getAll('status')
    const from = q.get('from')
    const to = q.get('to')
    const cancelled = q.get('includeCancelled') === 'true' || wanted.includes('CANCELLED')
    return TRIPS.filter((t) => (cancelled || t.isActive) && (!wanted.length || wanted.includes(t.statusCode)) && (!from || t.tripDate >= from) && (!to || t.tripDate <= to))
  }
  if (m && method === 'POST') return { id: 77, publicId: 't-new', driverPublicId: m[1], tripType: 'Vagón del muelle', tripDate: body?.tripDate, amount: 75, rateMissing: false, statusCode: 'OPEN', status: 'Por liquidar', isActive: true, createdAtUtc: '2026-10-12T10:00:00' }
  m = /^\/api\/v1\/drivers\/([^/]+)\/trips\/([^/]+)\/cancel$/.exec(p)
  if (m && method === 'POST') return { ...TRIPS[0], statusCode: 'CANCELLED', status: 'Cancelado', isActive: false }

  // ---- catálogos, estatus, usuarios, zonas, contactos, campos personalizados ----
  if (p === '/api/v1/dispatch-zones') return ZONES
  if (p === '/api/v1/users') {
    return [
      { id: 1, fullName: 'Administrador', email: 'admin@x.com', userKind: 'INTERNAL', isActive: true, membershipStatus: 'ACTIVE', roles: [] },
      { id: 2, fullName: 'Carlos Rivera', email: 'carlos@x.com', userKind: 'INTERNAL', isActive: true, membershipStatus: 'ACTIVE', roles: [] },
      { id: 5, fullName: 'Ana Cruz', email: 'ana@x.com', userKind: 'INTERNAL', isActive: true, membershipStatus: 'ACTIVE', roles: [] },
      { id: 6, fullName: 'Portal Pedro', email: 'pedro@x.com', userKind: 'PORTAL', isActive: true, membershipStatus: 'ACTIVE', roles: [] },
    ]
  }
  if (p === '/api/v1/tenant/settings') return { id: 1, maxStopsPerRouteDefault: 25 }
  if (p === '/api/v1/catalogs/LicenseClass') return [{ code: 'CDL_A', label: 'CDL-A', isEnabled: true, sortOrder: 1 }, { code: 'CDL_B', label: 'CDL-B', isEnabled: true, sortOrder: 2 }, { code: 'REGULAR', label: 'Regular', isEnabled: true, sortOrder: 3 }]
  if (p === '/api/v1/catalogs/CertificationType') return [{ code: 'HAZMAT', label: 'HazMat', isEnabled: true, sortOrder: 1 }, { code: 'REEFER', label: 'Refrigerado', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/catalogs/ContactType') return [{ code: 'PHONE', label: 'Teléfono', isEnabled: true }, { code: 'MOBILE', label: 'Móvil', isEnabled: true }, { code: 'EMAIL', label: 'Correo', isEnabled: true }]
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'NEXT_DAY', label: 'Next day', isEnabled: true, sortOrder: 1 }, { code: 'STANDARD', label: 'Standard', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Box', isEnabled: true, sortOrder: 1 }, { code: 'ENVELOPE', label: 'Envelope', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/status/DriverTripStatus') {
    return [
      { code: 'OPEN', label: 'Por liquidar', colorHex: '#2563EB', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
      { code: 'SETTLED', label: 'Liquidado', colorHex: '#059669', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 2 },
      { code: 'CANCELLED', label: 'Cancelado', colorHex: '#6B7280', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 3 },
    ]
  }
  if (p.startsWith('/api/v1/status/history/')) return []
  if (p.startsWith('/api/v1/status/')) return []
  if (p.startsWith('/api/v1/contacts/DRIVER/') && method === 'GET') return []
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
