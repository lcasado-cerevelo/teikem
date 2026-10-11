// Lote A4 — utilidades de las pruebas de pantalla del conteo por producto (no es un archivo de prueba: no termina en .test).
// Un aparato registrado con sesión, productos en el catálogo local y un `fetch` falso por ruta: lo que no tiene respuesta
// se comporta "sin señal" (la cola de salida se queda pendiente y se puede revisar con listOutbox()).
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession } from '../kernel/auth/session'
import { getDb } from '../kernel/db/database'

export async function setupDevice(): Promise<void> {
  setApiBaseUrl('http://api.test')
  await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
}

export function insertProduct(id: number, publicId: string, sku: string, name: string, barcode: string, tracking: 'NONE' | 'LOT' | 'SERIAL'): void {
  getDb().runSync('INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (?, ?, ?, ?, ?, ?, 1)', [
    id,
    publicId,
    sku,
    name,
    barcode,
    tracking,
  ])
}

export interface FetchCall {
  method: string
  path: string
  search: string
  body: unknown
  /** Cabecera Idempotency-Key de la petición (las de la cola de salida la llevan). */
  idempotencyKey: string | null
}

export type Route = (call: FetchCall) => Response | null

export function json(status: number, data: unknown): Response {
  return new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Sustituye fetch: cada llamada pasa por las rutas en orden; la primera que responde gana; ninguna → falla de red. */
export function mockFetch(routes: Route[]): FetchCall[] {
  const calls: FetchCall[] = []
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const req = input as Request
    const url = new URL(req.url)
    const text = req.method === 'GET' || req.method === 'HEAD' ? '' : await req.text()
    const call: FetchCall = { method: req.method, path: url.pathname, search: url.search, body: text ? JSON.parse(text) : null, idempotencyKey: req.headers.get('Idempotency-Key') }
    calls.push(call)
    for (const route of routes) {
      const response = route(call)
      if (response) return response
    }
    throw new TypeError('Network request failed')
  })
  return calls
}
