// Lote 231 — piezas de un kit (sin React), comunes a todo editor de composición: el kit de oferta (Precios y ofertas) y, después, el kit armado de Productos.
// Una pieza = producto + cantidad por kit + «No sustituible» (por omisión toda pieza es sustituible, §9 del diseño). Aquí solo lo que se puede decir antes de
// enviar (al menos una pieza, producto escogido, cantidad > 0 con hasta 4 decimales, sin repetir); el servidor repite todo y manda en lo demás (otro kit,
// otro dueño, rastreo…). Los textos de los problemas están en `warehouse.products.kit.err.*` y los pinta `KitPiecesEditor`.
import { parsePrice } from './priceListRules'

export interface KitPiece {
  key: string
  productPublicId: string | null
  sku: string
  name: string
  /** Texto de la cantidad, tal cual se escribió (acepta coma decimal). */
  quantity: string
  notSubstitutable: boolean
  /** Precio de venta de la pieza si se conoce (al escogerla en el buscador); null = no se sabe. */
  salePrice: number | null
}

export type KitPiecesProblem = 'noPieces' | 'pieceProduct' | 'pieceQuantity' | 'pieceRepeated'
export const KIT_PIECES_PROBLEMS: readonly KitPiecesProblem[] = ['noPieces', 'pieceProduct', 'pieceQuantity', 'pieceRepeated']

export const newKitPiece = (key: string): KitPiece => ({ key, productPublicId: null, sku: '', name: '', quantity: '1', notSubstitutable: false, salePrice: null })

/** Cantidad de una pieza: número > 0 con hasta 4 decimales; si no, null. */
export function pieceQuantity(text: string): number | null {
  const n = parsePrice(text)
  return n != null && n > 0 ? n : null
}

/** Primer problema de las piezas; un renglón vacío y sin tocar (sin producto, cantidad 1) no cuenta. null = listas para enviar. */
export function kitPiecesError(pieces: readonly KitPiece[]): KitPiecesProblem | null {
  const filled = pieces.filter((p) => p.productPublicId || p.quantity.trim() !== '1')
  if (filled.length === 0) return 'noPieces'
  if (filled.some((p) => !p.productPublicId)) return 'pieceProduct'
  if (filled.some((p) => pieceQuantity(p.quantity) == null)) return 'pieceQuantity'
  const ids = filled.map((p) => p.productPublicId)
  if (new Set(ids).size !== ids.length) return 'pieceRepeated'
  return null
}

/** Las piezas como las pide el servidor (`KitComponentRequest[]`): solo las que tienen producto. */
export function piecesToComponents(pieces: readonly KitPiece[]) {
  return pieces
    .filter((p) => p.productPublicId)
    .map((p) => ({ productPublicId: p.productPublicId, quantity: pieceQuantity(p.quantity) ?? 0, notSubstitutable: p.notSubstitutable }))
}

/** Piezas del editor a partir de las que manda el servidor (`KitComponentDto[]`); sin ninguna, un renglón vacío. */
export function piecesFromComponents(
  components: { productPublicId?: string; sku?: string | null; name?: string | null; quantity?: number; notSubstitutable?: boolean }[] | null | undefined,
  key: (i: number) => string,
): KitPiece[] {
  const rows = (components ?? []).map((c, i) => ({
    key: key(i),
    productPublicId: c.productPublicId ?? null,
    sku: c.sku ?? '',
    name: c.name ?? '',
    quantity: String(c.quantity ?? 1),
    notSubstitutable: c.notSubstitutable === true,
    salePrice: null,
  }))
  return rows.length ? rows : [newKitPiece(key(0))]
}
