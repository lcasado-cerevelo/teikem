// 2026-10-10 — Ajustar cantidad en línea (POST /inventory/adjustments/quantity, permiso warehouse.adjust): sin cola, el operario necesita saber ya si se pudo.
import { api, unwrap } from '../../kernel/api/client'
import { buildAdjustRequest, type AdjustInput } from './adjustLogic'

export async function adjustQuantity(input: AdjustInput): Promise<void> {
  await unwrap(api.POST('/api/v1/inventory/adjustments/quantity', { body: buildAdjustRequest(input) }))
}
