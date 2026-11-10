// Lote 229 — reglas puras de las OFERTAS: borrador de la oferta (nombre, vigencia, a quién aplica), cuerpo que se envía, qué impide publicar, el kit de
// oferta (piezas con cantidad y «No sustituible») y la suma de sus piezas a precio normal. Lote 231: el kit de oferta vive en una biblioteca (se reusa en
// otras ofertas con otro precio; su composición no cambia una vez usado). Espejo de `OfferRules` del servidor (que repite todo):
// aquí solo para avisar antes de enviar. Los textos de los problemas están en `warehouse.products.prices.offer.err.*`.
import type { ChipTone } from '../../kernel/ui'
import { KIT_PIECES_PROBLEMS, kitPiecesError, newKitPiece, pieceQuantity, piecesFromComponents, piecesToComponents, type KitPiece, type KitPiecesProblem } from './kitPieces'
import type { OfferKitRequest, OfferKitUpdateRequest, OfferRequest } from './offerApi'
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

// ---- Kit de oferta (biblioteca, lote 231) ----
// Las piezas son las comunes de `kitPieces.ts` (también las usará el kit armado); aquí quedan los nombres de siempre como alias.
export type OfferKitPiece = KitPiece
export const newOfferKitPiece = newKitPiece
export const piecesFromKit = piecesFromComponents
export { pieceQuantity }

export interface OfferKitDraft {
  sku: string
  name: string
  price: string
  pieces: OfferKitPiece[]
  /** «Ajustable al pedir» (por omisión no: el kit de oferta nace no ajustable). */
  adjustable?: boolean
}

export type OfferKitProblem = 'skuRequired' | 'nameRequired' | 'priceRequired' | KitPiecesProblem

/** ¿El problema es de las piezas? (su texto está en `warehouse.products.kit.err.*`; los demás en `…prices.offer.kit.err.*`). */
export const isPiecesProblem = (p: OfferKitProblem): p is KitPiecesProblem => (KIT_PIECES_PROBLEMS as readonly string[]).includes(p)

/**
 * `editing` = cambiar un kit de la biblioteca (no se cambian SKU ni precio desde aquí: el precio es el del renglón). `locked` = su composición ya no se
 * puede cambiar (ya se usó): solo se revisa el nombre.
 */
export function offerKitDraftError(d: OfferKitDraft, editing = false, locked = false): OfferKitProblem | null {
  if (!editing && !d.sku.trim()) return 'skuRequired'
  if (!d.name.trim()) return 'nameRequired'
  if (!editing && parsePrice(d.price) == null) return 'priceRequired'
  return locked ? null : kitPiecesError(d.pieces)
}

/** «Crear un kit nuevo» desde la oferta (`POST /offers/{id}/kits`): queda en la biblioteca y con su renglón en esta oferta. */
export function toOfferKitRequest(d: OfferKitDraft): OfferKitRequest {
  return { sku: d.sku.trim(), name: d.name.trim(), price: parsePrice(d.price), components: piecesToComponents(d.pieces), adjustable: d.adjustable === true }
}

/** Cambio de un kit de la biblioteca (`PATCH /offers/kits/{id}`): con la composición bloqueada solo viaja el nombre. */
export function toOfferKitUpdate(d: OfferKitDraft, locked = false): OfferKitUpdateRequest {
  return locked ? { name: d.name.trim() } : { name: d.name.trim(), components: piecesToComponents(d.pieces), adjustable: d.adjustable === true }
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

/** Precio que se sugiere al «Usar un kit anterior»: el último con que se puso en una oferta (`lastPrice`); sin él, en blanco. */
export const suggestedKitPrice = (kit: { lastPrice?: number | null }): string => (kit.lastPrice == null ? '' : String(kit.lastPrice))

/** Renglón de «Usar un kit anterior»: el precio escrito (editable) es obligatorio y válido; null = problema `priceRequired`. */
export function libraryKitItem(kit: { productPublicId?: string }, priceText: string): { productPublicId: string; price: number } | null {
  const price = parsePrice(priceText)
  return price == null || !kit.productPublicId ? null : { productPublicId: kit.productPublicId, price }
}

/** ¿El kit ya es un renglón activo de esta oferta? (no se agrega dos veces). */
export const kitInOffer = (items: readonly { productPublicId?: string }[] | null | undefined, kitId: string | null | undefined): boolean =>
  Boolean(kitId) && (items ?? []).some((i) => i.productPublicId === kitId)

// ---- Foto del producto ----
/** Tipos de foto que acepta el servidor (`ProductImageRules`). */
export const PRODUCT_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const
export const PRODUCT_PHOTO_ACCEPT = PRODUCT_PHOTO_TYPES.join(',')

/** ¿El archivo es una foto admitida? Por tipo o, si el navegador no lo da, por extensión. */
export function isProductPhoto(file: { type: string; name: string }): boolean {
  if (file.type) return (PRODUCT_PHOTO_TYPES as readonly string[]).includes(file.type.toLowerCase())
  return /\.(jpe?g|png|webp)$/i.test(file.name)
}
