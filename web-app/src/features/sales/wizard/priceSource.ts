// Lote 229 — de dónde sale el precio de un producto del pedido y sus avisos (`notices`: dos ofertas a la vez, oferta más cara que la lista general), juntando
// lo que dice el catálogo (`GET /sales-orders/catalog`, al escoger) y lo que devolvió el servidor al guardar el borrador (`linePrices`, solo en la respuesta de
// crear/editar con líneas). Sin React: la app móvil puede copiarlo. El precio que se cobra siempre lo decide el servidor (gana el más bajo).
import type { SalesOrderDto } from '../salesApi'
import type { SalesCatalogDto, SalesCatalogItemDto } from './wizardApi'

export interface PriceSourceInfo {
  /** El precio que se cobra sale de una oferta. */
  fromOffer: boolean
  offerName: string | null
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

/** Avisos del renglón del catálogo. */
export function itemNotices(item: Pick<SalesCatalogItemDto, 'notices'> | null | undefined): string[] {
  return unique((item?.notices ?? []).map((n) => n.message))
}

/** Lo guardado (`linePrices`) manda sobre el catálogo para la fuente del precio; los avisos se juntan de los dos. */
export function priceSourceOf(productPublicId: string, catalog: SalesCatalogDto | null | undefined, saved: SalesOrderDto | null | undefined): PriceSourceInfo {
  const line = (saved?.linePrices ?? []).find((l) => l.productPublicId === productPublicId)
  const item = catalogItemOf(catalog, productPublicId)
  const source = line?.priceSource ?? item?.priceSource ?? null
  const fromOffer = source === 'OFFER'
  return {
    fromOffer,
    offerName: fromOffer ? line?.offerName ?? item?.offerName ?? null : null,
    notices: unique([...(line?.notices ?? []).map((n) => n.message), ...(item?.notices ?? []).map((n) => n.message)]),
  }
}

/** Los avisos de todo el carrito (para el resumen del paso de entrega), en el orden del carrito y solo los productos que tienen alguno. */
export function cartPriceNotices(
  cart: readonly { productPublicId: string; sku: string }[],
  catalog: SalesCatalogDto | null | undefined,
  saved: SalesOrderDto | null | undefined,
): { productPublicId: string; sku: string; notices: string[] }[] {
  return cart
    .map((l) => ({ productPublicId: l.productPublicId, sku: l.sku, notices: priceSourceOf(l.productPublicId, catalog, saved).notices }))
    .filter((x) => x.notices.length > 0)
}
