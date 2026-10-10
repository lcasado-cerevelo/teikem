// 2026-10-08 — Daños en línea: reportar un daño y buscar el recibo del que llegó dañado. Necesita señal (mueve inventario en el servidor).
import { api, unwrap } from '../../kernel/api/client'
import { enqueue } from '../../kernel/sync/outbox'
import type { components } from '../../kernel/api/schema'

export type DamageReportDto = components['schemas']['DamageReportDto']
export type DamageReportRequest = components['schemas']['DamageReportRequest']

/** 2026-10-10 (D2b): el reporte va a la cola de salida (se manda solo con señal). No se proyecta en los saldos locales: el servidor decide la posición
 *  de cuarentena y las reservas; la siguiente bajada de saldos lo refleja. Devuelve el id de la cola (para `flushNow`). */
export function queueDamage(body: DamageReportRequest): number {
  return enqueue({ kind: 'damage', body })
}

/** Busca un recibo del almacén por su número exacto (REC-00012). null si no existe. */
export async function findReceiptByNumber(warehousePublicId: string, number: string): Promise<{ publicId: string; number: string } | null> {
  const wanted = number.trim().toUpperCase()
  if (!wanted) return null
  const page = await unwrap(api.GET('/api/v1/receipts', { params: { query: { warehousePublicId, search: number.trim(), take: 20 } } }))
  const match = (page.items ?? []).find((r) => (r.number ?? '').toUpperCase() === wanted)
  return match?.publicId ? { publicId: match.publicId, number: match.number ?? number.trim() } : null
}
