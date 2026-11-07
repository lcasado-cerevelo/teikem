// Lote 223 (P3) — SOLO PARA PRUEBAS: fetch simulado del API para las pruebas del pedido guiado (no lo importa ninguna pantalla). Cada prueba registra su
// `route(method, path, body, url)`; lo que no responde cae en los catálogos comunes (formas de pago, términos, un almacén) o en 404.
/** Permisos de un vendedor (rol Ventas). */
export const SELLER = ['sales.view', 'sales.create', 'clients.read', 'clients.create_basic', 'locations.read']

export interface FakeRequest {
  method: string
  path: string
  url: URL
  body: unknown
  /** Campos del multipart (lo que se agregó al FormData, ver `recordingFormData`). */
  form: Record<string, unknown> | null
}

export interface FakeApi {
  requests: FakeRequest[]
  route: ((r: FakeRequest) => Response | unknown | undefined) | null
  /** Formularios multipart armados y todavía no enviados (en orden). */
  forms?: Record<string, unknown>[]
}

/**
 * jsdom no sabe leer un multipart: con `vi.stubGlobal('FormData', recordingFormData(api))` cada FormData registra sus campos y el fetch simulado los pega a la
 * petición que los manda (como en las pruebas de importar).
 */
export function recordingFormData(api: FakeApi) {
  return class RecordingFormData {
    readonly fields: Record<string, unknown> = {}
    constructor() {
      ;(api.forms ??= []).push(this.fields)
    }
    append(field: string, value: unknown) {
      this.fields[field] = value
    }
    set(field: string, value: unknown) {
      this.fields[field] = value
    }
  }
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

export function problem(status: number, title: string, code?: string, errors?: Record<string, string[]>): Response {
  return json({ title, status, code, errors }, status)
}

function lookups(entity: string, rows: [string, string][]) {
  return rows.map(([code, label], i) => ({ id: i + 1, entity, code, label, labels: { es: label }, sortOrder: i, isSystem: true, isEnabled: true, isOverridden: false, isActive: true }))
}

export const PAYMENT_METHODS = lookups('SalesPaymentMethod', [
  ['COD_CHECK', 'Contra entrega con cheque'],
  ['COD_CASH', 'Contra entrega en efectivo'],
  ['CREDIT', 'Crédito'],
  ['CONSIGNMENT', 'Consignación'],
])
export const PAYMENT_TERMS = lookups('PaymentTerm', [
  ['CHEQUE', 'Cheque'],
  ['CASH', 'Efectivo'],
  ['NET15', 'Neto 15'],
  ['NET30', 'Neto 30'],
  ['CONSIGNMENT', 'Consignación'],
])

/** El `fetch` que recibe `createApiClient` en el `vi.mock` del cliente del API. */
export function fakeFetch(api: FakeApi) {
  return async (req: Request): Promise<Response> => {
    const url = new URL(req.url)
    let body: unknown
    let form: Record<string, unknown> | null = null
    const text = await req.clone().text()
    try {
      body = text ? JSON.parse(text) : undefined
    } catch {
      body = text
      form = api.forms?.shift() ?? null
    }
    const r: FakeRequest = { method: req.method, path: url.pathname, url, body, form }
    api.requests.push(r)
    const out = api.route?.(r)
    if (out instanceof Response) return out
    if (out !== undefined) return json(out)
    const p = url.pathname
    if (p === '/api/v1/catalogs/SalesPaymentMethod') return json(PAYMENT_METHODS)
    if (p === '/api/v1/catalogs/PaymentTerm') return json(PAYMENT_TERMS)
    if (p === '/api/v1/warehouses') return json([{ id: 1, publicId: 'wh-1', code: 'ALM-01', name: 'Principal', isActive: true }])
    if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return json([])
    return problem(404, 'no encontrado')
  }
}
