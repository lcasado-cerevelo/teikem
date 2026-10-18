// Lote 107: el cernidor es AL SOMETER la orden (contrato y tarifa vigentes, cliente y crédito). Un cliente necesita un contrato activo con tarifa para que se le puedan crear órdenes por API.
// Uso: crear el cliente con `contract: contractData()` y después `await withRates(request, headers, client)`.
import { expect, type APIRequestContext } from '@playwright/test'

const API_URL = process.env.API_URL ?? 'http://localhost:5000'

const day = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10)

/** Datos del contrato que se manda al crear el cliente (vigente desde hace 30 días). */
export function contractData(): { startDate: string; title: string } {
  return { startDate: day(-30), title: 'Contrato de prueba' }
}

/** Le pone tarifa STANDARD/BOX y activa el contrato del cliente recién creado con `contract: contractData()`. */
export async function withRates(request: APIRequestContext, headers: Record<string, string>, client: { currentContract?: { publicId?: string } | null }): Promise<void> {
  const contractId = client.currentContract?.publicId ?? ''
  expect(contractId, 'el cliente debe crearse con contract: contractData()').toBeTruthy()
  for (const packageType of ['BOX', 'ENVELOPE']) {
    const r = await request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType, rate: 10, effectiveFrom: day(-30) } })
    expect(r.ok(), await r.text()).toBeTruthy()
  }
  const s = await request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } })
  expect(s.ok(), await s.text()).toBeTruthy()
}
