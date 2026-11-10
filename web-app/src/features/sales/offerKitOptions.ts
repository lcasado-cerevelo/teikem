// Lote 234 — los kits de oferta que se pueden escoger en la ventana «Nueva orden»: los que el catálogo del pedido (`GET /sales-orders/catalog?clientPublicId=`) da
// para el cliente, es decir, los de ofertas vigentes que le aplican HOY, con el precio ya resuelto (el de la oferta). Es la MISMA condición del pedido guiado: antes
// el buscador de productos traía cualquier kit de oferta (también los de ofertas vencidas o de otros clientes) y el servidor lo rechazaba al guardar.
import type { components } from '../../kernel/api/schema'

type S = components['schemas']

export function offerKitOptions(catalog: S['SalesCatalogDto'] | null | undefined): S['ProductListItemDto'][] {
  const seen = new Set<string>()
  const out: S['ProductListItemDto'][] = []
  for (const brand of catalog?.brands ?? [])
    for (const item of brand.offers ?? []) {
      if (!item.isOfferKit || !item.productPublicId || seen.has(item.productPublicId)) continue
      seen.add(item.productPublicId)
      out.push({ publicId: item.productPublicId, sku: item.sku, name: item.name, brand: item.brand, salePrice: item.price ?? null, isKit: true, isOwn: true, isActive: true })
    }
  return out.sort((a, b) => (a.sku ?? '').localeCompare(b.sku ?? ''))
}
