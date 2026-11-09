// Lote 229 — avisos de precio del pedido guiado, discretos: junto al precio en la lista y las tarjetas (`PriceNoticeList`), en cada línea del carrito con la
// oferta de donde sale el precio (`CartLinePriceInfo`) y en el resumen del paso de entrega (`DeliveryPriceNotices`). Solo informan: se cobra el precio más bajo.
import { useT } from '../../../kernel/i18n'
import { Chip } from '../../../kernel/ui'
import type { SalesOrderDto } from '../salesApi'
import { cartPriceNotices, priceSourceOf } from './priceSource'
import { useSalesCatalog } from './wizardApi'
import './priceNotices.css'

/** Lista corta de avisos (texto pequeño con el color de aviso del tema). */
export function PriceNoticeList({ notices, label }: { notices: string[]; label: string }) {
  if (notices.length === 0) return null
  return (
    <ul className="wz-notices" aria-label={label}>
      {notices.map((m) => (
        <li key={m}>{m}</li>
      ))}
    </ul>
  )
}

/** En una línea del carrito: «Oferta «nombre»» si el precio sale de una oferta, y sus avisos. */
export function CartLinePriceInfo({ productPublicId, sku, clientPublicId, saved }: { productPublicId: string; sku: string; clientPublicId: string | null; saved: SalesOrderDto | null }) {
  const t = useT()
  const catalog = useSalesCatalog(clientPublicId).data
  const info = priceSourceOf(productPublicId, catalog, saved)
  if (!info.fromOffer && info.notices.length === 0) return null
  return (
    <div className="wz-line-price">
      {info.fromOffer && <Chip tone="cod">{info.offerName ? t('salesWizard.cart.offerOf', { name: info.offerName }) : t('salesWizard.products.offerPriceChip')}</Chip>}
      <PriceNoticeList notices={info.notices} label={t('salesWizard.products.notices', { sku })} />
    </div>
  )
}

/** Resumen del paso de entrega: los avisos de precio de todo el carrito (lo guardado en el borrador y lo del catálogo). */
export function DeliveryPriceNotices({ cart, clientPublicId, saved }: { cart: readonly { productPublicId: string; sku: string }[]; clientPublicId: string | null; saved: SalesOrderDto | null }) {
  const t = useT()
  const catalog = useSalesCatalog(clientPublicId).data
  const rows = cartPriceNotices(cart, catalog, saved)
  if (rows.length === 0) return null
  return (
    <section className="wz-price-notices" aria-labelledby="wz-price-notices-title">
      <h3 id="wz-price-notices-title">{t('salesWizard.summary.priceNotices')}</h3>
      <ul className="wz-notices">
        {rows.flatMap((r) =>
          r.notices.map((m) => (
            <li key={`${r.productPublicId}-${m}`}>
              <b className="mono">{r.sku}</b> {m}
            </li>
          )),
        )}
      </ul>
      <p className="help">{t('salesWizard.summary.priceNoticesHelp')}</p>
    </section>
  )
}
