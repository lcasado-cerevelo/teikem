// Lote 223 (P3) — paso 2 del pedido guiado: los PRODUCTOS. Pestañas por marca (las del catálogo: solo marcas con productos activos; los sin marca al final);
// arriba de cada marca sus ofertas vigentes como tarjetas con foto, precio y + / −; debajo la lista completa de la marca (SKU, nombre, precio de la lista del
// cliente o la general) con su casilla de cantidad: teclear una cantidad agrega la línea. El carrito (`CartBar`) va fijo abajo; tocarlo muestra las líneas para
// corregir o quitar, y un kit muestra sus piezas (`KitLineBlock`). El vendedor no teclea precios.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useCan } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Chip, EmptyState, matchesQ, QBox, Spinner, Tabs } from '../../../kernel/ui'
import { useStoredFileUrl } from '../../orders/api'
import { StoredImage } from '../../warehouse/StoredImage'
import { KitLineBlock } from '../KitLineBlock'
import type { SalesOrderDto } from '../salesApi'
import { useSalesCatalog, type SalesCatalogBrandDto, type SalesCatalogItemDto } from './wizardApi'
import {
  cartLineAmount,
  cartQuantity,
  cartSummary,
  removeFromCart,
  setCartPieces,
  setCartPrice,
  setCartQuantity,
  stepCartQuantity,
  type CartLine,
  type WizardState,
} from './wizardRules'

const NO_BRAND = '__none__'
const brandKey = (b: SalesCatalogBrandDto) => b.brand ?? NO_BRAND

export interface ProductsStepProps {
  state: WizardState
  onChange: (next: (s: WizardState) => WizardState) => void
}

function PriceText({ item }: { item: SalesCatalogItemDto }) {
  const t = useT()
  const f = useFormat()
  if (item.price == null) return <span className="wz-noprice">{t('salesWizard.products.noPrice')}</span>
  return <span className="mono">{f.money(item.price)}</span>
}

/** Cuadrito con la foto del producto (la de su oferta o la del renglón de la lista, si la hay); al pasar el mouse, al enfocar o al tocarlo se ve grande. */
function ProductThumb({ item }: { item: SalesCatalogItemDto }) {
  const t = useT()
  const url = useStoredFileUrl(item.imagePublicId).data
  const ref = useRef<HTMLButtonElement>(null)
  const [pos, setPos] = useState<{ x: number; y: number } | null>(null)
  if (!item.imagePublicId || !url) return <span className="wz-thumb blank" aria-hidden="true" />
  const name = item.name ?? item.sku ?? ''
  const show = () => {
    const r = ref.current?.getBoundingClientRect()
    if (!r) return
    const size = 280
    setPos({ x: Math.max(8, Math.min(r.right + 8, window.innerWidth - size - 8)), y: Math.max(8, Math.min(r.top, window.innerHeight - size - 8)) })
  }
  return (
    <>
      <button
        type="button"
        ref={ref}
        className="wz-thumb"
        aria-label={t('salesWizard.products.photoOf', { name })}
        onMouseEnter={show}
        onMouseLeave={() => setPos(null)}
        onFocus={show}
        onBlur={() => setPos(null)}
        onClick={() => (pos ? setPos(null) : show())}
      >
        <img src={url} alt="" />
      </button>
      {pos && <img className="wz-thumb-big" src={url} alt={name} style={{ left: pos.x, top: pos.y }} />}
    </>
  )
}

/** Solo dígitos y un punto decimal (hasta 4 decimales, como el precio del servidor). */
function decimalText(text: string): string {
  const t = text.replace(/[^\d.]/g, '')
  const [int = '', ...rest] = t.split('.')
  return rest.length === 0 ? int : `${int}.${rest.join('').slice(0, 4)}`
}

function OfferCard({ item, quantity, onStep, onSet }: { item: SalesCatalogItemDto; quantity: string; onStep: (delta: number) => void; onSet: (text: string) => void }) {
  const t = useT()
  const name = item.name ?? item.sku ?? ''
  const canSell = item.price != null
  return (
    <li className={`wz-offer${quantity ? ' on' : ''}`}>
      <StoredImage publicId={item.imagePublicId} alt={t('salesWizard.products.offerPhoto', { name })} className="wz-offer-img" emptyClassName="wz-offer-img empty" />
      <div className="wz-offer-body">
        <Chip tone="cod">{t('salesWizard.products.offer')}</Chip>
        <b>{name}</b>
        <span className="help">{item.sku}</span>
        <PriceText item={item} />
      </div>
      <div className="wz-stepper">
        <button type="button" className="btn sm" aria-label={t('salesWizard.products.less', { name })} disabled={!canSell || !quantity} onClick={() => onStep(-1)}>−</button>
        <input aria-label={t('salesWizard.products.qtyOf', { sku: item.sku ?? '' })} inputMode="decimal" className="wz-qty" value={quantity} disabled={!canSell} onChange={(e) => onSet(e.target.value)} />
        <button type="button" className="btn sm" aria-label={t('salesWizard.products.more', { name })} disabled={!canSell} onClick={() => onStep(1)}>+</button>
      </div>
    </li>
  )
}

export function ProductsStep({ state, onChange }: ProductsStepProps) {
  const t = useT()
  const catalog = useSalesCatalog(state.clientPublicId)
  const canPrice = useCan('sales.price')
  const brands = useMemo(() => (catalog.data?.brands ?? []).filter((b) => (b.offers ?? []).length + (b.products ?? []).length > 0), [catalog.data])
  const [tab, setTab] = useState<string>('')
  const [q, setQ] = useState('')
  // la pestaña escogida o, si ya no está (otro cliente, catálogo nuevo), la primera marca
  const current = brands.find((b) => brandKey(b) === tab) ?? brands[0]
  // con texto en el buscador se busca en TODAS las marcas (productos y ofertas); sin texto, la lista de la marca escogida
  const searching = q.trim() !== ''
  const products = useMemo(
    () => (searching ? brands.flatMap((b) => b.products ?? []) : current?.products ?? []).filter((p) => matchesQ(q, p.sku ?? '', p.name ?? '')),
    [brands, current, q, searching],
  )
  const offers = useMemo(
    () => (searching ? brands.flatMap((b) => b.offers ?? []) : current?.offers ?? []).filter((o) => matchesQ(q, o.sku ?? '', o.name ?? '')),
    [brands, current, q, searching],
  )
  // al entrar al paso el buscador toma el foco
  const searchRef = useRef<HTMLDivElement>(null)
  const ready = catalog.isSuccess && brands.length > 0
  useEffect(() => {
    if (ready) searchRef.current?.querySelector('input')?.focus()
  }, [ready])

  const set = (item: SalesCatalogItemDto, text: string) => onChange((s) => ({ ...s, cart: setCartQuantity(s.cart, item, text) }))
  const step = (item: SalesCatalogItemDto, delta: number) => onChange((s) => ({ ...s, cart: stepCartQuantity(s.cart, item, delta) }))

  return (
    <section className="wz-step" aria-labelledby="wz-step-title">
      <h2 id="wz-step-title">{t('salesWizard.products.title')}</h2>
      {catalog.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(catalog.error).title}</p>
      ) : catalog.isPending ? (
        <Spinner block />
      ) : brands.length === 0 ? (
        <EmptyState title={t('salesWizard.products.empty')} />
      ) : (
        <>
          <Tabs
            label={t('salesWizard.products.brands')}
            value={brandKey(current!)}
            onChange={(k) => (setTab(k), setQ(''))}
            tabs={brands.map((b) => ({ key: brandKey(b), label: b.brand ?? t('salesWizard.products.noBrand') }))}
          />
          {offers.length > 0 && (
            <>
              <h3 className="wz-sub">{t('salesWizard.products.offers')}</h3>
              <ul className="wz-offers" aria-label={t('salesWizard.products.offersOf', { brand: searching ? t('salesWizard.products.allBrands') : current?.brand ?? t('salesWizard.products.noBrand') })}>
                {offers.map((o) => (
                  <OfferCard key={o.productPublicId} item={o} quantity={cartQuantity(state.cart, o.productPublicId)} onStep={(d) => step(o, d)} onSet={(text) => set(o, text)} />
                ))}
              </ul>
            </>
          )}
          <h3 className="wz-sub">{t('salesWizard.products.all')}</h3>
          <div ref={searchRef}>
            <QBox value={q} onChange={setQ} placeholder={t('salesWizard.products.search')} />
          </div>
          {products.length === 0 ? (
            <p className="help" role="status">{t('salesWizard.products.noMatch')}</p>
          ) : (
            <ul className="wz-prods" aria-label={t('salesWizard.products.listOf', { brand: searching ? t('salesWizard.products.allBrands') : current?.brand ?? t('salesWizard.products.noBrand') })}>
              {products.map((p) => {
                const qty = cartQuantity(state.cart, p.productPublicId)
                return (
                  <li key={p.productPublicId} className={`wz-prod${qty ? ' on' : ''}`}>
                    <ProductThumb item={p} />
                    <div className="wz-prod-main">
                      <b className="mono">{p.sku}</b>
                      <span>{p.name}</span>
                      {p.isKit && <Chip tone="neutral">{t('sales.kit.badge')}</Chip>}
                    </div>
                    <input
                      aria-label={t('salesWizard.products.qtyOf', { sku: p.sku ?? '' })}
                      inputMode="numeric"
                      className="wz-qty"
                      placeholder={t('salesWizard.products.qtyPlaceholder')}
                      value={qty}
                      disabled={p.price == null && !canPrice}
                      onChange={(e) => set(p, e.target.value.replace(/\D/g, ''))}
                    />
                    {p.price == null && canPrice ? (
                      <label className="wz-money">
                        <span aria-hidden="true">$</span>
                        <input
                          aria-label={t('salesWizard.products.priceOf', { sku: p.sku ?? '' })}
                          inputMode="decimal"
                          className="wz-qty wz-price"
                          placeholder={t('salesWizard.products.pricePlaceholder')}
                          value={state.cart.find((l) => l.productPublicId === p.productPublicId)?.typedPrice ?? ''}
                          disabled={!qty}
                          onChange={(e) => onChange((s) => ({ ...s, cart: setCartPrice(s.cart, p.productPublicId ?? '', decimalText(e.target.value)) }))}
                        />
                      </label>
                    ) : (
                      <PriceText item={p} />
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </>
      )}
    </section>
  )
}

/** El carrito fijo abajo: conteo y subtotal; abierto, las líneas para corregir la cantidad o quitar, y las piezas de los kits. */
export function CartBar({ state, onChange, saved }: { state: WizardState; onChange: (next: (s: WizardState) => WizardState) => void; saved: SalesOrderDto | null }) {
  const t = useT()
  const f = useFormat()
  const [open, setOpen] = useState(false)
  const sum = cartSummary(state.cart)
  const setQty = (l: CartLine, text: string) =>
    onChange((s) => ({ ...s, cart: s.cart.map((x) => (x.productPublicId === l.productPublicId ? { ...x, quantity: text } : x)) }))
  return (
    <div className="wz-cart">
      <button type="button" className="btn block wz-cart-toggle" aria-expanded={open} aria-controls="wz-cart-lines" onClick={() => setOpen((v) => !v)}>
        <span>{t('salesWizard.cart.summary', { lines: sum.lines, units: f.number(sum.units) })}</span>
        <b className="mono">{f.money(sum.subtotal)}</b>
        <span className="help">{open ? t('salesWizard.cart.hide') : t('salesWizard.cart.show')}</span>
      </button>
      {open && (
        <div id="wz-cart-lines" className="wz-cart-lines" role="region" aria-label={t('salesWizard.cart.title')}>
          {state.cart.length === 0 ? (
            <p className="help">{t('salesWizard.cart.empty')}</p>
          ) : (
            <ul>
              {state.cart.map((l) => (
                <li key={l.productPublicId} className="wz-cart-line">
                  <div className="wz-prod-main">
                    <b className="mono">{l.sku}</b>
                    <span>{l.name}</span>
                  </div>
                  <input aria-label={t('salesWizard.products.qtyOf', { sku: l.sku })} inputMode="decimal" className="wz-qty" value={l.quantity} onChange={(e) => setQty(l, e.target.value)} />
                  <span className="mono wz-amount">{l.price == null ? '—' : f.money(cartLineAmount(l))}</span>
                  <button type="button" className="btn sm" onClick={() => onChange((s) => ({ ...s, cart: removeFromCart(s.cart, l.productPublicId) }))} aria-label={t('salesWizard.cart.removeOf', { sku: l.sku })}>
                    {t('salesWizard.cart.remove')}
                  </button>
                  {l.isKit && (
                    <KitLineBlock
                      line={{ key: 0, productPublicId: l.productPublicId, sku: l.sku, name: l.name, listPrice: l.price, quantity: l.quantity, price: '', isKit: true, components: l.components }}
                      saved={saved?.lines?.find((x) => x.productPublicId === l.productPublicId)}
                      onPieces={(pieces) => onChange((s) => ({ ...s, cart: setCartPieces(s.cart, l.productPublicId, pieces) }))}
                    />
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
