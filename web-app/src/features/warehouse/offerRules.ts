// Lote 229 — reglas puras de las OFERTAS: borrador de la oferta (nombre, vigencia, a quién aplica), cuerpo que se envía, qué impide publicar, el kit creado
// para una oferta (piezas con cantidad y «No sustituible») y la suma de sus piezas a precio normal. Espejo de `OfferRules` del servidor (que repite todo):
// aquí solo para avisar antes de enviar. Los textos de los problemas están en `warehouse.products.prices.offer.err.*`.
import type { ChipTone } from '../../kernel/ui'
import type { OfferKitRequest, OfferRequest } from './offerApi'
import { parsePrice } from './priceListRules'

export const OFFER_NAME_MAX = 120
export const OFFER_NOTES_MAX = 500

export interface OfferDraft {
  name: string
  validFrom: string
  /** '' = sin fecha de fin: la oferta vale hasta que se desactive. */
  validTo: string
  appliesToAll: boolean
  notes: string
}

export const EMPTY_OFFER_DRAFT = (today: string): OfferDraft => ({ name: '', validFrom: today, validTo: '', appliesToAll: true, notes: '' })

export type OfferDraftProblem = 'nameRequired' | 'nameLong' | 'fromRequired' | 'range' | 'notesLong'

export function offerDraftError(d: OfferDraft): OfferDraftProblem | null {
  if (!d.name.trim()) return 'nameRequired'
  if (d.name.trim().length > OFFER_NAME_MAX) return 'nameLong'
  if (!d.validFrom) return 'fromRequired'
  if (d.validTo && d.validTo < d.validFrom) return 'range'
  if (d.notes.trim().length > OFFER_NOTES_MAX) return 'notesLong'
  return null
}

/** Cuerpo de crear/editar: sin «hasta» se manda `clearValidTo` (sin fecha de fin). */
export function toOfferRequest(d: OfferDraft): OfferRequest {
  return {
    name: d.name.trim(),
    validFrom: d.validFrom,
    validTo: d.validTo || null,
    clearValidTo: !d.validTo,
    appliesToAll: d.appliesToAll,
    notes: d.notes.trim() || null,
  }
}

/** Para publicar hace falta al menos un renglón y (clientes o «aplica a todos»), como en el servidor. */
export function offerPublishBlocker(itemCount: number, appliesToAll: boolean, clientCount: number): 'noItems' | 'noClients' | null {
  if (itemCount < 1) return 'noItems'
  if (!appliesToAll && clientCount < 1) return 'noClients'
  return null
}

export type OfferState = 'DRAFT' | 'SCHEDULED' | 'ACTIVE' | 'EXPIRED'

/** Estado que se pinta en la tarjeta: borrador / publicada (programada) / vigente / vencida; desconocido = borrador. */
export function offerStateKey(state: string | null | undefined): OfferState {
  return state === 'ACTIVE' || state === 'SCHEDULED' || state === 'EXPIRED' ? state : 'DRAFT'
}

export function offerStateTone(state: string | null | undefined): ChipTone {
  switch (offerStateKey(state)) {
    case 'ACTIVE':
      return 'deliv'
    case 'SCHEDULED':
      return 'route'
    case 'EXPIRED':
      return 'fail'
    default:
      return 'cap'
  }
}

/** ¿La oferta no tiene fecha de fin? (`hasEndDate` del servidor; si no viene, por `validTo`). */
export function offerIsOpenEnded(o: { hasEndDate?: boolean | null; validTo?: string | null }): boolean {
  return o.hasEndDate == null ? !o.validTo : !o.hasEndDate
}

// ---- Kit creado para una oferta ----
export interface OfferKitPiece {
  key: string
  productPublicId: string | null
  sku: string
  name: string
  /** Texto de la cantidad, tal cual se escribió. */
  quantity: string
  notSubstitutable: boolean
  /** Precio de venta de la pieza si se conoce (al escogerla en el buscador); null = no se sabe. */
  salePrice: number | null
}

export interface OfferKitDraft {
  sku: string
  name: string
  price: string
  pieces: OfferKitPiece[]
}

export const newOfferKitPiece = (key: string): OfferKitPiece => ({ key, productPublicId: null, sku: '', name: '', quantity: '1', notSubstitutable: false, salePrice: null })

export type OfferKitProblem = 'skuRequired' | 'nameRequired' | 'priceRequired' | 'noPieces' | 'pieceProduct' | 'pieceQuantity' | 'pieceRepeated'

/** Cantidad de una pieza: número > 0 con hasta 4 decimales; si no, null. */
export function pieceQuantity(text: string): number | null {
  const n = parsePrice(text)
  return n != null && n > 0 ? n : null
}

/** `editing` = cambiar un kit ya creado (no se cambian SKU ni precio desde aquí: el precio es el del renglón). */
export function offerKitDraftError(d: OfferKitDraft, editing = false): OfferKitProblem | null {
  if (!editing && !d.sku.trim()) return 'skuRequired'
  if (!d.name.trim()) return 'nameRequired'
  if (!editing && parsePrice(d.price) == null) return 'priceRequired'
  const pieces = d.pieces.filter((p) => p.productPublicId || p.quantity.trim() !== '1')
  if (pieces.length === 0) return 'noPieces'
  if (pieces.some((p) => !p.productPublicId)) return 'pieceProduct'
  if (pieces.some((p) => pieceQuantity(p.quantity) == null)) return 'pieceQuantity'
  const ids = pieces.map((p) => p.productPublicId)
  if (new Set(ids).size !== ids.length) return 'pieceRepeated'
  return null
}

function components(d: OfferKitDraft) {
  return d.pieces
    .filter((p) => p.productPublicId)
    .map((p) => ({ productPublicId: p.productPublicId, quantity: pieceQuantity(p.quantity) ?? 0, notSubstitutable: p.notSubstitutable }))
}

export function toOfferKitRequest(d: OfferKitDraft): OfferKitRequest {
  return { sku: d.sku.trim(), name: d.name.trim(), price: parsePrice(d.price), components: components(d) }
}

export function toOfferKitUpdate(d: OfferKitDraft) {
  return { name: d.name.trim(), components: components(d) }
}

/**
 * Suma de las piezas a su precio de venta y el ahorro contra el precio de la oferta. Solo si se conoce el precio de TODAS las piezas escogidas
 * (si falta uno, null: no se inventa); el ahorro puede ser negativo (la oferta sale más cara que las piezas sueltas).
 */
export function offerKitSavings(d: OfferKitDraft): { normal: number; offer: number | null; saving: number | null } | null {
  const pieces = d.pieces.filter((p) => p.productPublicId)
  if (pieces.length === 0 || pieces.some((p) => p.salePrice == null || pieceQuantity(p.quantity) == null)) return null
  const normal = Math.round(pieces.reduce((s, p) => s + (p.salePrice ?? 0) * (pieceQuantity(p.quantity) ?? 0), 0) * 100) / 100
  const offer = parsePrice(d.price)
  return { normal, offer, saving: offer == null ? null : Math.round((normal - offer) * 100) / 100 }
}

/** Piezas del editor a partir del kit que manda el servidor (al cambiar un kit de oferta ya creado). */
export function piecesFromKit(
  components: { productPublicId?: string; sku?: string | null; name?: string | null; quantity?: number; notSubstitutable?: boolean }[] | null | undefined,
  key: (i: number) => string,
): OfferKitPiece[] {
  const rows = (components ?? []).map((c, i) => ({
    key: key(i),
    productPublicId: c.productPublicId ?? null,
    sku: c.sku ?? '',
    name: c.name ?? '',
    quantity: String(c.quantity ?? 1),
    notSubstitutable: c.notSubstitutable === true,
    salePrice: null,
  }))
  return rows.length ? rows : [newOfferKitPiece(key(0))]
}

// ---- Foto del producto ----
/** Tipos de foto que acepta el servidor (`ProductImageRules`). */
export const PRODUCT_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const
export const PRODUCT_PHOTO_ACCEPT = PRODUCT_PHOTO_TYPES.join(',')

/** ¿El archivo es una foto admitida? Por tipo o, si el navegador no lo da, por extensión. */
export function isProductPhoto(file: { type: string; name: string }): boolean {
  if (file.type) return (PRODUCT_PHOTO_TYPES as readonly string[]).includes(file.type.toLowerCase())
  return /\.(jpe?g|png|webp)$/i.test(file.name)
}
