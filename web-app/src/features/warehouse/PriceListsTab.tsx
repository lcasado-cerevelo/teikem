// Lote 221 (P1) / Lote 229 — pantalla «Precios y ofertas» (Contabilidad, `/money/prices`, sales.view ve, sales.pricelist edita). Dos secciones claras:
// - Ofertas (`OffersPanel`, `/api/v1/offers`): cosa aparte de las listas, con foto, vigencia y renglones producto/kit → precio de oferta; «Nueva oferta» y cada
//   tarjeta abren `OfferEditorModal`. (Antes del lote 229 una oferta era un kit marcado «Es promoción»: ese flujo se quitó.)
// - Listas de precios: nombre, vigencia, estado calculado (borrador / programada / vigente / vencida), a quién aplica y cuántos renglones; la fila abre su editor.
// La lista no es paginada por el servidor (viene completa): no lleva `exportRows`.
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Filters, IconTag, Panel, type DataColumn } from '../../kernel/ui'
import { ToggleFilter } from './filterControls'
import { OfferEditorModal } from './OfferEditorModal'
import { OffersPanel } from './OffersPanel'
import { PriceListEditorModal } from './PriceListEditorModal'
import { usePriceLists, type PriceListListItemDto } from './priceListApi'
import { stateKey, stateTone } from './priceListRules'

const STATES = ['DRAFT', 'SCHEDULED', 'ACTIVE', 'EXPIRED'] as const
const NO_LISTS: PriceListListItemDto[] = []

export function PriceListsTab() {
  const t = useT()
  const f = useFormat()
  const [q, setQ] = useState('')
  const [state, setState] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [opened, setOpened] = useState<string | null | undefined>(undefined) // undefined = cerrado, null = nueva
  const [offerId, setOfferId] = useState<string | null | undefined>(undefined) // undefined = cerrada, null = nueva
  const lists = usePriceLists({ q: q.trim() || undefined, state: state || undefined, includeInactive })

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
            <button type="button" className="btn" onClick={() => setOfferId(null)}>
              {t('warehouse.products.prices.newOffer')}
            </button>
            <button type="button" className="btn flow" onClick={() => setOpened(null)}>
              {t('warehouse.products.prices.new')}
            </button>
          </Can>
        </div>
      </div>

      <OffersPanel includeInactive={includeInactive} onOpen={setOfferId} />

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
      {offerId !== undefined && <OfferEditorModal key={offerId ?? 'new'} publicId={offerId} onClose={() => setOfferId(undefined)} />}
    </>
  )
}
