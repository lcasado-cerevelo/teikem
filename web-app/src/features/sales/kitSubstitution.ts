// Lote 230 — reglas puras de la SUSTITUCIÓN de piezas de un kit al pedir (sin React; la app móvil las puede copiar). El servidor
// (`KitSubstitutionRules`) es quien decide qué es válido: aquí solo se arma lo que se muestra y lo que se manda.
// - `GET /sales-orders/kits/{kitPublicId}/substitutes` trae cada pieza del catálogo del kit con «no sustituible» y sus sustitutos válidos (misma
//   categoría exacta y mismo precio de venta; activos, no kits, del mismo dueño y que no son ya otra pieza del kit).
// - Una pieza sustituida guarda el producto REAL en `productPublicId` y la original en `substitutesProductPublicId` (con su SKU y nombre para
//   decir «en lugar de …»). Va en la misma cantidad que la pieza; el precio de la línea no cambia.
// - La línea manda TODAS sus piezas: `components: [{ productPublicId, quantity, substitutesProductPublicId? }]`.
import type { components } from '../../kernel/api/schema'
import type { KitPiece } from './salesRules'
import { parseNumber } from './salesRules'

export type KitSubstitutesDto = components['schemas']['KitSubstitutesDto']
export type KitComponentSubstitutesDto = components['schemas']['KitComponentSubstitutesDto']
export type KitSubstituteDto = components['schemas']['KitSubstituteDto']

/** La pieza original (la del catálogo del kit) de una pieza que puede estar sustituida. */
export function originalOf(p: KitPiece): string {
  return p.substitutesProductPublicId || p.productPublicId
}

/** La pieza del catálogo (con sus sustitutos) que corresponde a una pieza de la línea. */
export function catalogPieceOf(subs: KitSubstitutesDto | undefined, p: KitPiece): KitComponentSubstitutesDto | undefined {
  const original = originalOf(p)
  return subs?.components?.find((c) => c.productPublicId === original)
}

/**
 * Los productos que se pueden escoger para una pieza: los sustitutos válidos de su pieza original, sin el que ya tiene puesto y sin los que ya son otra
 * pieza de la línea (dos piezas no pueden quedar con el mismo producto). Una pieza «no sustituible» no tiene ninguno, ni un kit que no es de oferta.
 */
export function substituteOptions(subs: KitSubstitutesDto | undefined, pieces: readonly KitPiece[], p: KitPiece): KitSubstituteDto[] {
  // Lote 231: solo un kit de oferta cambia piezas al pedir (el servidor ya manda la lista vacía para los demás; aquí no se confía en eso)
  if (subs?.isOfferKit === false) return []
  const c = catalogPieceOf(subs, p)
  if (!c || c.notSubstitutable) return []
  const used = new Set(pieces.filter((x) => originalOf(x) !== originalOf(p)).map((x) => x.productPublicId))
  return (c.substitutes ?? []).filter((s) => s.productPublicId && s.productPublicId !== p.productPublicId && !used.has(s.productPublicId))
}

/** Cambia la pieza (por su original) por el sustituto escogido, en la misma cantidad. */
export function substitutePiece(pieces: readonly KitPiece[], original: string, s: KitSubstituteDto): KitPiece[] {
  return pieces.map((p) => {
    if (originalOf(p) !== original) return p
    const isSubstituted = Boolean(p.substitutesProductPublicId)
    return {
      productPublicId: s.productPublicId ?? '',
      sku: s.sku ?? '',
      name: s.name ?? '',
      quantity: p.quantity,
      substitutesProductPublicId: original,
      substitutesSku: isSubstituted ? (p.substitutesSku ?? '') : p.sku,
      substitutesName: isSubstituted ? (p.substitutesName ?? '') : p.name,
    }
  })
}

/** Deshace la sustitución de una pieza: vuelve la original, en la misma cantidad. */
export function undoSubstitution(pieces: readonly KitPiece[], original: string): KitPiece[] {
  return pieces.map((p) =>
    originalOf(p) === original && p.substitutesProductPublicId
      ? { productPublicId: original, sku: p.substitutesSku ?? '', name: p.substitutesName ?? '', quantity: p.quantity }
      : p,
  )
}

/** Las piezas como las pide el servidor en la línea (todas; la sustituta con la original en `substitutesProductPublicId`). */
export function piecesToRequest(pieces: readonly KitPiece[]) {
  return pieces.map((c) => ({
    productPublicId: c.productPublicId,
    quantity: parseNumber(c.quantity) ?? 0,
    ...(c.substitutesProductPublicId ? { substitutesProductPublicId: c.substitutesProductPublicId } : {}),
  }))
}

/** «SKU · Nombre · Marca» de un sustituto (lo que se ve en la lista de «Cambiar»). */
export function substituteLabel(s: KitSubstituteDto): string {
  return [s.sku, s.name, s.brand].filter((x) => x && x.trim() !== '').join(' · ')
}
