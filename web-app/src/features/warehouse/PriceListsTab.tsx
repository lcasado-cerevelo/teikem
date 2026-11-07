// Lote 221 (P1) — pestaña «Precios y ofertas» de Productos e inventario (`?tab=prices`, sales.view; editar = sales.pricelist). Dos bloques:
// - Ofertas: tarjetas con foto de los kits marcados «Es promoción» (vigencia y precio de venta); «Editar oferta» abre el editor del kit (foto, vigencia y marca).
// - Listas de precios: nombre, vigencia, estado calculado (borrador / programada / vigente / vencida), a quién aplica y cuántos renglones; la fila abre su editor.
// La lista no es paginada por el servidor (viene completa): no lleva `exportRows`.
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Filters, IconTag, Panel, Spinner, type DataColumn } from '../../kernel/ui'
import { ToggleFilter } from './filterControls'
import { KitEditorByIdModal } from './KitTab'
import { PriceListEditorModal } from './PriceListEditorModal'
import { useOffers, usePriceLists, type PriceListListItemDto, type ProductPromoDto } from './priceListApi'
import { promoStatus, stateKey, stateTone } from './priceListRules'
import { StoredImage } from './StoredImage'

const STATES = ['DRAFT', 'SCHEDULED', 'ACTIVE', 'EXPIRED'] as const
const NO_LISTS: PriceListListItemDto[] = []

function OfferCard({ offer, onEdit }: { offer: ProductPromoDto; onEdit: () => void }) {
  const t = useT()
  const f = useFormat()
  const status = promoStatus(offer.promoFrom, offer.promoTo, f.today())
  const range = [offer.promoFrom && f.date(offer.promoFrom), offer.promoTo && f.date(offer.promoTo)].filter(Boolean).join(' – ')
  return (
    <article className="pl-offer" aria-label={`${offer.sku ?? ''} ${offer.name ?? ''}`}>
      <StoredImage publicId={offer.imagePublicId} alt={offer.name ?? ''} className="pl-photo" emptyClassName="pl-nophoto" emptyText={t('warehouse.products.prices.noPhoto')} />
      <div className="pl-body">
        <h3>{offer.name}</h3>
        <span className="pl-sub">
          {offer.sku}
          {offer.brand ? ` · ${offer.brand}` : ''}
        </span>
        <span>
          <Chip tone={status === 'active' ? 'deliv' : status === 'scheduled' ? 'warn' : 'fail'}>{t(`warehouse.products.prices.offerStatus.${status}`)}</Chip>{' '}
          {!offer.isActive && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}
        </span>
        <span className="pl-sub">{range || t('warehouse.products.prices.noDates')}</span>
        {offer.salePrice != null && <b className="mono">{f.money(offer.salePrice)}</b>}
        <Can perm="sales.pricelist">
          <button type="button" className="btn sm" onClick={onEdit}>
            {t('warehouse.products.prices.editOffer')}
          </button>
        </Can>
      </div>
    </article>
  )
}

export function PriceListsTab() {
  const t = useT()
  const f = useFormat()
  const [q, setQ] = useState('')
  const [state, setState] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [opened, setOpened] = useState<string | null | undefined>(undefined) // undefined = cerrado, null = nueva
  const [offerId, setOfferId] = useState<string | null>(null)
  const lists = usePriceLists({ q: q.trim() || undefined, state: state || undefined, includeInactive })
  const offers = useOffers(includeInactive)

  const columns = useMemo<DataColumn<PriceListListItemDto>[]>(
    () => [
      { id: 'name', header: t('warehouse.products.prices.name'), cell: (l) => <b>{l.name}</b>, sortValue: (l) => l.name ?? '', card: 'title' },
      {
        id: 'validity',
        header: t('warehouse.products.prices.validity'),
        cell: (l) => `${f.date(l.validFrom)} – ${l.validTo ? f.date(l.validTo) : t('warehouse.products.prices.open')}`,
        sortValue: (l) => l.validFrom,
      },
      {
        id: 'state',
        header: t('warehouse.products.prices.stateCol'),
        cell: (l) => (
          <>
            <Chip tone={stateTone(l.state)}>{t(`warehouse.products.prices.state.${stateKey(l.state)}`)}</Chip> {!l.isActive && <Chip tone="fail">{t('warehouse.products.prices.inactive')}</Chip>}
          </>
        ),
        sortValue: (l) => t(`warehouse.products.prices.state.${stateKey(l.state)}`),
      },
      {
        id: 'applies',
        header: t('warehouse.products.prices.applies'),
        cell: (l) => (l.appliesToAll ? t('warehouse.products.prices.appliesAll') : t('warehouse.products.prices.appliesSome', { count: l.clientCount })),
        sortValue: (l) => (l.appliesToAll ? -1 : l.clientCount),
      },
      { id: 'items', header: t('warehouse.products.prices.itemsCol'), cell: (l) => f.number(l.itemCount), sortValue: (l) => l.itemCount, align: 'end' },
    ],
    [t, f],
  )

  return (
    <>
      <div className="head">
        <div>
          <h2>{t('warehouse.products.prices.title')}</h2>
          <p>{t('warehouse.products.prices.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="sales.pricelist">
            <button type="button" className="btn flow" onClick={() => setOpened(null)}>
              {t('warehouse.products.prices.new')}
            </button>
          </Can>
        </div>
      </div>

      <Panel flush icon={<IconTag />} title={t('warehouse.products.prices.offers')} badge={offers.data?.length}>
        {offers.isLoading ? (
          <Spinner block />
        ) : (offers.data ?? []).length === 0 ? (
          <EmptyState title={t('warehouse.products.prices.noOffers')} body={t('warehouse.products.prices.noOffersBody')} />
        ) : (
          <div className="pl-offers">
            {(offers.data ?? []).map((o) => (
              <OfferCard key={o.productPublicId} offer={o} onEdit={() => setOfferId(o.productPublicId)} />
            ))}
          </div>
        )}
      </Panel>

      <div style={{ height: 14 }} />
      <Filters
        onClear={() => {
          setQ('')
          setState('')
          setIncludeInactive(false)
        }}
      >
        <div className="f">
          <label htmlFor="pl-q">{t('warehouse.products.prices.search')}</label>
          <input id="pl-q" type="search" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor="pl-state">{t('warehouse.products.prices.stateCol')}</label>
          <select id="pl-state" value={state} onChange={(e) => setState(e.target.value)}>
            <option value="">{t('warehouse.products.prices.allStates')}</option>
            {STATES.map((s) => (
              <option key={s} value={s}>
                {t(`warehouse.products.prices.state.${s}`)}
              </option>
            ))}
          </select>
        </div>
        {/* Lote 228: el interruptor de los filtros del almacén (etiqueta arriba, a la altura de los campos); la casilla suelta quedaba descuadrada */}
        <ToggleFilter label={t('warehouse.products.prices.showInactive')} checked={includeInactive} onChange={setIncludeInactive} />
      </Filters>
      <Panel flush icon={<IconTag />} title={t('warehouse.products.prices.lists')} badge={lists.data?.length}>
        {lists.error ? (
          <p className="pb ferr" role="alert">
            {lists.error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.products.prices.lists')}
            columns={columns}
            rows={lists.data ?? NO_LISTS}
            rowKey={(l) => l.publicId}
            loading={lists.isLoading}
            pageSize={25}
            onRowClick={(l) => setOpened(l.publicId)}
            rowClassName={(l) => (l.isActive ? undefined : 'dim')}
            empty={<EmptyState title={t('warehouse.products.prices.noLists')} body={t('warehouse.products.prices.noListsBody')} />}
          />
        )}
      </Panel>

      {opened !== undefined && <PriceListEditorModal key={opened ?? 'new'} publicId={opened} onClose={() => setOpened(undefined)} />}
      {offerId && <KitEditorByIdModal publicId={offerId} onClose={() => setOfferId(null)} />}
    </>
  )
}
