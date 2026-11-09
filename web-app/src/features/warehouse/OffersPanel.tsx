// Lote 229 — sección «Ofertas» de Contabilidad → Precios y ofertas (`GET /api/v1/offers`, sales.view). Una oferta es aparte de las listas de precios: nombre, foto,
// vigencia y renglones producto/kit → precio de oferta. Cada tarjeta lleva la foto, el nombre, la vigencia («Sin fecha de fin: vale hasta que la desactive» bien
// visible si no tiene «hasta»), el estado (borrador / programada / vigente / vencida), a quién aplica y cuántos renglones; «Abrir oferta» abre su editor.
import { useCan } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, EmptyState, IconTag, Panel, Spinner } from '../../kernel/ui'
import { useOfferList, type OfferListItemDto } from './offerApi'
import { offerIsOpenEnded, offerStateKey, offerStateTone } from './offerRules'
import { StoredImage } from './StoredImage'
import './offers.css'

/** Vigencia de una oferta: «Del … al …» o «Desde el …» + el aviso de que no tiene fecha de fin. */
export function OfferValidity({ offer }: { offer: Pick<OfferListItemDto, 'validFrom' | 'validTo' | 'hasEndDate'> }) {
  const t = useT()
  const f = useFormat()
  if (offerIsOpenEnded(offer))
    return (
      <>
        <span className="pl-sub">{t('warehouse.products.prices.offer.validityOpen', { from: f.date(offer.validFrom) })}</span>
        <span className="of-open">{t('warehouse.products.prices.offer.openEnded')}</span>
      </>
    )
  return <span className="pl-sub">{t('warehouse.products.prices.offer.validity', { from: f.date(offer.validFrom), to: f.date(offer.validTo) })}</span>
}

function OfferCard({ offer, onOpen }: { offer: OfferListItemDto; onOpen: () => void }) {
  const t = useT()
  const f = useFormat()
  const canEdit = useCan('sales.pricelist')
  const state = offerStateKey(offer.state)
  return (
    <article className={`of-card${offer.isActive === false ? ' dim' : ''}`} aria-label={offer.name ?? ''}>
      <StoredImage publicId={offer.imagePublicId} alt={t('warehouse.products.prices.offer.photoAlt', { name: offer.name ?? '' })} className="pl-photo" emptyClassName="pl-nophoto" emptyText={t('warehouse.products.prices.noPhoto')} />
      <div className="of-card-body">
        <h3>{offer.name}</h3>
        <span className="of-chips">
          <Chip tone={offerStateTone(offer.state)}>{t(`warehouse.products.prices.offer.state.${state}`)}</Chip>
          {offer.inEffect && <Chip tone="deliv">{t('warehouse.products.prices.offer.inEffect')}</Chip>}
          {offer.isActive === false && <Chip tone="fail">{t('warehouse.products.prices.offer.inactive')}</Chip>}
        </span>
        <OfferValidity offer={offer} />
        <span className="pl-sub">
          {offer.appliesToAll ? t('warehouse.products.prices.appliesAll') : t('warehouse.products.prices.appliesSome', { count: offer.clientCount ?? 0 })}
          {' · '}
          {t('warehouse.products.prices.offer.itemsCount', { count: f.number(offer.itemCount ?? 0) })}
        </span>
        <button type="button" className="btn sm" onClick={onOpen}>
          {canEdit ? t('warehouse.products.prices.offer.openOffer') : t('warehouse.products.prices.offer.viewOffer')}
        </button>
      </div>
    </article>
  )
}

export function OffersPanel({ includeInactive, onOpen }: { includeInactive: boolean; onOpen: (publicId: string) => void }) {
  const t = useT()
  const offers = useOfferList({ includeInactive })
  const rows = offers.data ?? []
  return (
    <Panel flush icon={<IconTag />} title={t('warehouse.products.prices.offers')} badge={offers.data?.length}>
      {offers.isLoading ? (
        <Spinner block />
      ) : offers.error ? (
        <p className="pb ferr" role="alert">
          {offers.error.message}
        </p>
      ) : rows.length === 0 ? (
        <EmptyState title={t('warehouse.products.prices.noOffers')} body={t('warehouse.products.prices.noOffersBody')} />
      ) : (
        <div className="pl-offers">
          {rows.map((o) => (
            <OfferCard key={o.publicId} offer={o} onOpen={() => o.publicId && onOpen(o.publicId)} />
          ))}
        </div>
      )}
    </Panel>
  )
}
