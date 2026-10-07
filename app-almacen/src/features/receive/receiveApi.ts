// Lote 16 — única llamada en línea de Recibir: la pista "Sugerida: {bin}" del paso de posición destino (recibo directo).
// Es solo una ayuda: sin señal, sin permiso o con error no se muestra nada y la captura sigue igual (D3-A: nada se llena
// solo; quien recibe escanea dónde puso la mercancía).
import { api, unwrap } from '../../kernel/api/client'

/** Una posición que el sistema propone para recibir (con el espacio libre que le queda, si tiene cupo). */
export interface TargetSuggestion {
  binCode: string
  freeQty: number | null
  reason: string
}

/** Hasta `take` posiciones sugeridas, en el orden del servidor (2026-10-07: el listado marcable del recibo directo). Sin señal o con error, vacío. */
export async function fetchTargetSuggestions(
  productPublicId: string,
  warehousePublicId: string,
  quantity: number,
  take = 10,
): Promise<TargetSuggestion[]> {
  try {
    const rows = await unwrap(
      api.GET('/api/v1/warehouse-tasks/putaway-suggestions', {
        params: { query: { productPublicId, warehousePublicId, quantity: quantity > 0 ? quantity : undefined, take } },
      }),
    )
    return rows.filter((r) => r.binCode).map((r) => ({ binCode: r.binCode ?? '', freeQty: r.freeQty ?? null, reason: r.reason ?? '' }))
  } catch {
    return []
  }
}

export async function fetchTargetSuggestion(
  productPublicId: string,
  warehousePublicId: string,
  quantity: number,
): Promise<string | null> {
  try {
    const rows = await unwrap(
      api.GET('/api/v1/warehouse-tasks/putaway-suggestions', {
        params: { query: { productPublicId, warehousePublicId, quantity: quantity > 0 ? quantity : undefined, take: 1 } },
      }),
    )
    return rows[0]?.binCode || null
  } catch {
    return null
  }
}
