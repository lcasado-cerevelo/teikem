// Lote 221 (P1) / Lote 229 — pantalla «Precios y ofertas» (Contabilidad, `/money/prices`, sales.view ve, sales.pricelist edita). Dos secciones claras:
// - Ofertas (`OffersPanel`, `/api/v1/offers`): cosa aparte de las listas, con foto, vigencia y renglones producto/kit → precio de oferta; «Nueva oferta» y cada
//   tarjeta abren `OfferEditorModal`. (Antes del lote 229 una oferta era un kit marcado «Es promoción»: ese flujo se quitó.)
// - Listas de precios: nombre, vigencia, estado calculado (borrador / programada / vigente / vencida), a quién aplica y cuántos renglones; la fila abre su editor.
// La lista no es paginada por el servidor (viene completa): no lleva `exportRows`.
import { useEffect, useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Filters, IconTag, Panel, type DataColumn } from '../../kernel/ui'
import { ToggleFilter } from './filterControls'
import { OfferEditorModal } from './OfferEditorModal'
import { OffersPanel } from './OffersPanel'
import { PriceListEditorModal } from './PriceListEditorModal'
import { usePriceLists, type PriceListListItemDto } from './priceListApi'
import { dedicatedListName } from '../sales/priceText'
import { isDedicated, listAudienceCount, stateKey, stateTone } from './priceListRules'

const STATES = ['DRAFT', 'SCHEDULED', 'ACTIVE', 'EXPIRED'] as const
const NO_LISTS: PriceListListItemDto[] = []
const SPECIAL_KEY = 'teikem.priceLists.includeSpecial'

/** Lote 247: «Ver también los precios especiales» se recuerda en este navegador. */
function readIncludeSpecial(): boolean {
  try {
    return window.localStorage.getItem(SPECIAL_KEY) === '1'
  } catch {
    return false
  }
}

export function PriceListsTab() {
  const t = useT()
  const f = useFormat()
  const [q, setQ] = useState('')
  const [state, setState] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [includeSpecial, setIncludeSpecial] = useState(readIncludeSpecial)
  useEffect(() => {
    try {
      window.localStorage.setItem(SPECIAL_KEY, includeSpecial ? '1' : '0')
    } catch {
      /* sin almacenamiento: no se recuerda */
    }
  }, [includeSpecial])
  const [opened, setOpened] = useState<string | null | undefined>(undefined) // undefined = cerrado, null = nueva
  const [offerId, setOfferId] = useState<string | null | undefined>(undefined) // undefined = cerrada, null = nueva
  const lists = usePriceLists({ q: q.trim() || undefined, state: state || undefined, includeInactive, includeSpecial })

  const columns = useMemo<DataColumn<PriceListListItemDto>[]>(
    () => [
      { id: 'name', header: t('warehouse.products.prices.name'), cell: (l) => (
          <span className="pl-namecell">
            {/* la lista dedicada se llama «Precio especial — dueño (fechas)»: el chip ya lo dice, el nombre completo queda en el título */}
            {isDedicated(l) ? (
              <Chip tone="cod" title={dedicatedListName(t, f, l) || undefined}>
                {t('warehouse.products.prices.special.chip', { name: l.specialOwnerName ?? '' })}
              </Chip>
            ) : (
              <b>{l.name}</b>
            )}
          </span>
        ),
        sortValue: (l) => dedicatedListName(t, f, l),
        exportValue: (l) => dedicatedListName(t, f, l),
        card: 'title',
      },
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
        cell: (l) =>
          isDedicated(l)
            ? t(`warehouse.products.prices.special.applies${l.specialOwnerKind === 'CHAIN' ? 'Chain' : 'Client'}`)
            : l.appliesToAll
              ? t('warehouse.products.prices.appliesAll')
              : t('warehouse.products.prices.appliesSome', { count: listAudienceCount(l) }),
        sortValue: (l) => (l.appliesToAll ? -1 : listAudienceCount(l)),
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
            <button type="button" className="btn" onClick={() => setOfferId(null)}>
              {t('warehouse.products.prices.newOffer')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setQ('')
          setState('')
          setIncludeInactive(false)
          setIncludeSpecial(false)
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
        <ToggleFilter label={t('warehouse.products.prices.special.showSpecial')} checked={includeSpecial} onChange={setIncludeSpecial} />
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

      {/* las listas de precios van primero, como dice el título «Precios y ofertas»; las ofertas, después */}
      <div style={{ height: 14 }} />
      <OffersPanel includeInactive={includeInactive} onOpen={setOfferId} />

      {opened !== undefined && <PriceListEditorModal key={opened ?? 'new'} publicId={opened} onClose={() => setOpened(undefined)} />}
      {offerId !== undefined && <OfferEditorModal key={offerId ?? 'new'} publicId={offerId} onClose={() => setOfferId(undefined)} />}
    </>
  )
}
