// Lote 229 — de dónde sale el precio de un producto del pedido y sus avisos (`notices`: dos ofertas a la vez, oferta más cara que la lista general), juntando
// lo que dice el catálogo (`GET /sales-orders/catalog`, al escoger) y lo que devolvió el servidor al guardar el borrador (`linePrices`, solo en la respuesta de
// crear/editar con líneas). Sin React: la app móvil puede copiarlo. El precio que se cobra siempre lo decide el servidor (gana el más bajo).
import type { SalesPriceNotice } from '../priceText'
import type { SalesOrderDto } from '../salesApi'
import type { SalesCatalogDto, SalesCatalogItemDto } from './wizardApi'

export interface PriceSourceInfo {
  /** El precio que se cobra sale de una oferta. */
  fromOffer: boolean
  offerName: string | null
  /** Lote 247: de dónde sale el precio cuando es un precio especial (el del cliente o el de su cadena); null = oferta, lista general o producto. */
  special: 'CLIENT_LIST' | 'CHAIN_LIST' | null
  /** Nombre de la cadena cuando `special` = CHAIN_LIST (solo la web del personal lo muestra: el portal del cliente no ve la cadena). */
  chainName: string | null
  /** Avisos sin repetir (por mensaje), listos para mostrar. */
  notices: string[]
}

/** El renglón del catálogo de un producto (en la lista de su marca o como tarjeta de oferta). */
export function catalogItemOf(catalog: SalesCatalogDto | null | undefined, productPublicId: string | null | undefined): SalesCatalogItemDto | null {
  if (!catalog || !productPublicId) return null
  for (const b of catalog.brands ?? []) {
    const hit = (b.products ?? []).find((p) => p.productPublicId === productPublicId) ?? (b.offers ?? []).find((p) => p.productPublicId === productPublicId)
    if (hit) return hit
  }
  return null
}

function unique(messages: (string | null | undefined)[]): string[] {
  const out: string[] = []
  for (const m of messages) if (m && !out.includes(m)) out.push(m)
  return out
}

/** Cómo se escribe un aviso: por omisión el texto del servidor (español); las pantallas pasan `usePriceNoticeText()` para armarlo en el idioma activo. */
export type NoticeText = (n: SalesPriceNotice) => string
const serverText: NoticeText = (n) => n.message ?? ''

/** Avisos del renglón del catálogo. */
export function itemNotices(item: Pick<SalesCatalogItemDto, 'notices'> | null | undefined, text: NoticeText = serverText): string[] {
  return unique((item?.notices ?? []).map(text))
}

/** Lo guardado (`linePrices`) manda sobre el catálogo para la fuente del precio; los avisos se juntan de los dos. */
export function priceSourceOf(productPublicId: string, catalog: SalesCatalogDto | null | undefined, saved: SalesOrderDto | null | undefined, text: NoticeText = serverText): PriceSourceInfo {
  const line = (saved?.linePrices ?? []).find((l) => l.productPublicId === productPublicId)
  const item = catalogItemOf(catalog, productPublicId)
  const source = line?.priceSource ?? item?.priceSource ?? null
  const fromOffer = source === 'OFFER'
  const special = source === 'CLIENT_LIST' || source === 'CHAIN_LIST' ? source : null
  return {
    fromOffer,
    offerName: fromOffer ? line?.offerName ?? item?.offerName ?? null : null,
    special,
    chainName: special === 'CHAIN_LIST' ? line?.chainName ?? item?.chainName ?? null : null,
    notices: unique([...(line?.notices ?? []).map(text), ...(item?.notices ?? []).map(text)]),
  }
}

/** Los avisos de todo el carrito (para el resumen del paso de entrega), en el orden del carrito y solo los productos que tienen alguno. */
export function cartPriceNotices(
  cart: readonly { productPublicId: string; sku: string }[],
  catalog: SalesCatalogDto | null | undefined,
  saved: SalesOrderDto | null | undefined,
  text: NoticeText = serverText,
): { productPublicId: string; sku: string; notices: string[] }[] {
  return cart
    .map((l) => ({ productPublicId: l.productPublicId, sku: l.sku, notices: priceSourceOf(l.productPublicId, catalog, saved, text).notices }))
    .filter((x) => x.notices.length > 0)
}

/**
 * Lote 247 — origen del precio de una línea ya guardada de la orden, para pintar el chip: manda lo guardado en la línea (`priceSource`, nombres, `isManualPrice`: así
 * una orden solicitada sigue diciendo «Cadena X» u «Oferta X») y, si la línea no lo trae (borrador recién guardado), lo que devolvió el servidor en `linePrices`.
 * Sin origen guardado (líneas de antes del lote), todo null y sin chip.
 */
export function savedLineOrigin(order: SalesOrderDto | null | undefined, productPublicId: string | null | undefined) {
  if (!order || !productPublicId) return null
  const line = (order.lines ?? []).find((l) => l.productPublicId === productPublicId)
  if (line?.priceSource) return line
  const priced = (order.linePrices ?? []).find((l) => l.productPublicId === productPublicId)
  if (!priced?.priceSource) return null
  return { priceSource: priced.priceSource, offerName: priced.offerName ?? null, priceChainName: priced.chainName ?? null, resolvedUnitPrice: priced.resolvedPrice ?? null, isManualPrice: false }
}
