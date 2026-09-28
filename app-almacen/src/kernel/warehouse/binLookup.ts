// Lote 8A-app — resolver una posición escaneada a su id real. Necesita señal: las posiciones no se sincronizan en
// este lote (solo productos, órdenes de compra y avisos, docs/lote8A-app-decisiones.md), así que Acomodar, Despacho
// (al empacar) y Conteo hacen esta misma llamada cuando ya de todas formas necesitan conexión.
import { api, unwrap } from '../api/client'

export interface FoundBin {
  id: number
  code: string
}

/** Busca una posición del almacén por su código exacto (lo que se escaneó). null si no existe o no coincide. */
export async function findBinByCode(warehousePublicId: string, code: string): Promise<FoundBin | null> {
  const rows = await unwrap(
    api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId: warehousePublicId }, query: { search: code } } }),
  )
  const match = rows.find((b) => (b.code ?? '').toUpperCase() === code.toUpperCase())
  return match?.id != null ? { id: match.id, code: match.code ?? code } : null
}
