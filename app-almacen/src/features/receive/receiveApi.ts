// Lote 16 — única llamada en línea de Recibir: la pista "Sugerida: {bin}" del paso de posición destino (recibo directo).
// Es solo una ayuda: sin señal, sin permiso o con error no se muestra nada y la captura sigue igual (D3-A: nada se llena
// solo; quien recibe escanea dónde puso la mercancía).
import { api, unwrap } from '../../kernel/api/client'

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
