// Lote 247 (Lote C) — «Precios especiales» en la ficha del cliente y de la cadena. Tabla Producto · Precio · Desde · Hasta · Estado · Lista general · Se cobra
// hoy (con su origen), alta en línea con el buscador de productos, cambio y quitar con confirmación. En el cliente, también los de su cadena (solo lectura, con
// enlace) y las listas normales que lo alcanzan. El precio es ANTES del IVU: las exenciones del cliente se aplican después. Se protege solo (módulo SALES_ORDERS y
// sales.view|sales.pricelist; sin ellos no se pinta, como la «Cadena» de la ficha); escribir va dentro de <Can perm="sales.pricelist">. Todo llega en una
// respuesta (tabla local, sin paginar por el servidor: no lleva `exportRows`).
import { useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Can, useAccess, useCan } from '../../kernel/access'
import { ModuleKeys } from '../../kernel/access/modules'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, DateField, EmptyState, Modal, Panel, Spinner, toast, useElementWidth, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconEdit, IconTrash } from '../../kernel/ui/actionIcons'
import { IconTag } from '../../kernel/ui/screenIcons'
import { ProductPicker } from '../warehouse/pickers'
import { stateKey, stateTone } from '../warehouse/priceListRules'
import {
  chargedSourceKey,
  draftOfRow,
  emptySpecialDraft,
  expiresSoon,
  specialDraftError,
  toSpecialPatch,
  toSpecialRequest,
  type SpecialPriceDraft,
} from './specialPriceRules'
import {
  SPECIAL_MANAGE,
  SPECIAL_VIEW,
  useSpecialPriceActions,
  useSpecialPrices,
  type SpecialOwnerKind,
  type SpecialPriceRowDto,
  type SpecialPricesDto,
} from './specialPricesApi'
import './specialPrices.css'

const K = 'specialPrices'
/** Bajo este ancho de PANEL (no de ventana) la tabla de 7 columnas pasa a tarjetas: no cabe en una línea por fila. */
const TABLE_MIN_WIDTH = 900

export interface SpecialPricesPanelProps {
  ownerKind: SpecialOwnerKind
  ownerPublicId: string
  /** La cadena o el cliente está activo: inactivo no admite precios nuevos (el servidor da 422). */
  ownerActive: boolean
}

export function SpecialPricesPanel(props: SpecialPricesPanelProps) {
  const { modules } = useAccess()
  const canView = useCan(SPECIAL_VIEW)
  if (!modules.has(ModuleKeys.SalesOrders) || !canView || !props.ownerPublicId) return null
  return <SpecialPricesBody {...props} />
}

/** Fecha «hasta» o «Sin fecha final». */
function useRowColumns(editable: boolean, chainName?: string | null): DataColumn<SpecialPriceRowDto>[] {
  const t = useT()
  const f = useFormat()
  return useMemo(
    () => [
      {
        id: 'product',
        header: t(`${K}.col.product`),
        cell: (r) => (
          <span className="spx-product">
            <b className="mono">{r.sku}</b> <span className="spx-name" title={r.productName ?? undefined}>{r.productName}</span>
            {r.isKit && <Chip tone="neutral">{t('warehouse.products.kit.chip')}</Chip>}
            {r.productIsActive === false && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}
          </span>
        ),
        sortValue: (r) => r.sku ?? '',
        exportValue: (r) => `${r.sku ?? ''} ${r.productName ?? ''}`.trim(),
        card: 'title',
      },
      {
        id: 'price',
        header: t(`${K}.col.price`),
        cell: (r) => (
          <span className="spx-price">
            {f.money(r.price ?? 0)}
            {r.aboveGeneral && (
              <Chip tone="warn" title={t(`${K}.aboveGeneral`)}>
                <span aria-hidden="true">▲</span>
                <span className="sr-only">{t(`${K}.aboveGeneral`)}</span>
              </Chip>
            )}
          </span>
        ),
        sortValue: (r) => r.price ?? 0,
        exportValue: (r) => r.price ?? 0,
        align: 'end',
      },
      { id: 'from', header: t(`${K}.col.from`), cell: (r) => f.date(r.validFrom), sortValue: (r) => r.validFrom ?? '', exportValue: (r) => r.validFrom ?? '' },
      {
        id: 'to',
        header: t(`${K}.col.to`),
        cell: (r) =>
          r.validTo ? (
            <span className={expiresSoon(r) ? 'spx-soon' : undefined}>
              {f.date(r.validTo)}
              {expiresSoon(r) && ` · ${t(`${K}.expiresIn`, { n: r.daysToExpire ?? 0 })}`}
            </span>
          ) : (
            <span className="spx-open">{t(`${K}.noEnd`)}</span>
          ),
        sortValue: (r) => r.validTo ?? '9999-12-31',
        exportValue: (r) => r.validTo ?? '',
      },
      {
        id: 'state',
        header: t(`${K}.col.state`),
        cell: (r) => (
          <span className="spx-state">
            <Chip tone={stateTone(r.state)}>{t(`warehouse.products.prices.state.${stateKey(r.state)}`)}</Chip>
          </span>
        ),
        sortValue: (r) => r.state ?? '',
        exportValue: (r) => t(`warehouse.products.prices.state.${stateKey(r.state)}`),
      },
      {
        id: 'general',
        header: t(`${K}.col.general`),
        cell: (r) => (r.generalListPrice == null ? '—' : f.money(r.generalListPrice)),
        sortValue: (r) => r.generalListPrice ?? -1,
        exportValue: (r) => r.generalListPrice ?? '',
        align: 'end',
      },
      {
        id: 'charged',
        header: t(`${K}.col.charged`),
        cell: (r) => <ChargedCell row={r} chainName={chainName} />,
        sortValue: (r) => r.chargedPrice ?? -1,
        exportValue: (r) => (r.chargedPrice == null ? '' : `${r.chargedPrice} ${chargedLabel(t, r, chainName)}`),
        align: 'end',
      },
    ],
    // `editable` no cambia las columnas: se deja para que la lista de dependencias sea explícita
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, editable, chainName],
  )
}

type T = ReturnType<typeof useT>

/** Texto del origen: de una lista de cadena dice la cadena (el nombre que manda el servidor es el de la lista dedicada, largo); de una oferta, la oferta. */
function chargedLabel(t: T, r: Pick<SpecialPriceRowDto, 'chargedSource' | 'chargedSourceName'>, chainName?: string | null): string {
  const key = chargedSourceKey(r.chargedSource)
  return t(`${K}.source.${key}`, { name: key === 'CHAIN_LIST' ? chainName ?? '' : r.chargedSourceName ?? '' })
}

/** «Se cobra hoy»: el precio y el chip de dónde sale. */
function ChargedCell({ row, chainName }: { row: SpecialPriceRowDto; chainName?: string | null }) {
  const t = useT()
  const f = useFormat()
  if (row.chargedPrice == null) return <span>—</span>
  const key = chargedSourceKey(row.chargedSource)
  const tone = key === 'CLIENT_LIST' || key === 'CHAIN_LIST' ? 'deliv' : key === 'OFFER' ? 'cod' : 'neutral'
  return (
    <span className="spx-charged">
      {f.money(row.chargedPrice)}
      <Chip tone={tone} title={chargedLabel(t, row, chainName)}>
        {chargedLabel(t, row, chainName)}
      </Chip>
    </span>
  )
}

function SpecialPricesBody({ ownerKind, ownerPublicId, ownerActive }: SpecialPricesPanelProps) {
  const t = useT()
  const [includeExpired, setIncludeExpired] = useState(false)
  const q = useSpecialPrices(ownerKind, ownerPublicId, includeExpired)
  const actions = useSpecialPriceActions(ownerKind, ownerPublicId, includeExpired)
  const canWrite = useCan(SPECIAL_MANAGE)
  const editable = canWrite && ownerActive
  const [editing, setEditing] = useState<SpecialPriceRowDto | null>(null)
  const [removing, setRemoving] = useState<SpecialPriceRowDto | null>(null)
  const dto = q.data
  // en la ficha de la cadena, lo que se cobra de una lista de cadena es de ESTA cadena
  const boxRef = useRef<HTMLDivElement>(null)
  const boxWidth = useElementWidth(boxRef)
  const cards = boxWidth > 0 && boxWidth < TABLE_MIN_WIDTH
  const columns = useRowColumns(editable, ownerKind === 'chain' ? dto?.ownerName : dto?.chainName)

  const rowActions = useMemo<RowAction<SpecialPriceRowDto>[]>(
    () => [
      { key: 'edit', label: t(`${K}.edit`), icon: <IconEdit />, perm: SPECIAL_MANAGE, visible: () => ownerActive, onClick: (r) => setEditing(r) },
      { key: 'remove', label: t(`${K}.remove`), icon: <IconTrash />, perm: SPECIAL_MANAGE, tone: 'danger', visible: () => ownerActive, onClick: (r) => setRemoving(r) },
    ],
    [t, ownerActive],
  )

  const subtitle = ownerKind === 'client' ? t(`${K}.subtitleClient`) : t(`${K}.subtitleChain`)
  return (
    <Panel icon={<IconTag />} title={t(`${K}.title`)} badge={dto?.rows?.length} subtitle={subtitle} flush collapsible defaultCollapsed={ownerKind === 'client'} summary={t(`${K}.summary`, { n: dto?.rows?.length ?? 0 })} storageKey={`special-prices.${ownerKind}`}>
      <div ref={boxRef} className="spx-box">
      {q.isPending ? (
        <Spinner block />
      ) : q.isError || !dto ? (
        <p className="pb ferr" role="alert">
          {applyProblemDetails(q.error).title}
        </p>
      ) : (
        <>
          <Can perm={SPECIAL_MANAGE}>{ownerActive ? <AddRow ownerKind={ownerKind} ownerPublicId={ownerPublicId} includeExpired={includeExpired} /> : <p className="pb spx-note">{t(`${K}.ownerInactive`)}</p>}</Can>
          <div className="spx-bar">
            <label className="sw spx-expired">
              <input type="checkbox" role="switch" checked={includeExpired} onChange={(e) => setIncludeExpired(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t(`${K}.showExpired`)}</span>
            </label>
          </div>
          <Warnings dto={dto} />
          <DataTable
            label={t(`${K}.title`)}
            forceCards={cards}
            columns={columns}
            rows={dto.rows ?? []}
            rowKey={(r) => r.itemId ?? 0}
            pageSize={25}
            rowActions={editable ? rowActions : undefined}
            rowClassName={(r) => (r.state === 'EXPIRED' ? 'dim' : undefined)}
            empty={<EmptyState title={t(`${K}.empty`)} body={ownerKind === 'client' ? t(`${K}.emptyClientBody`) : t(`${K}.emptyChainBody`)} />}
          />
          {ownerKind === 'client' && <ChainSection dto={dto} cards={cards} />}
          {ownerKind === 'client' && <ClientLists dto={dto} />}
        </>
      )}
      </div>
      {editing && <EditModal row={editing} ownerKind={ownerKind} ownerPublicId={ownerPublicId} includeExpired={includeExpired} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={removing !== null}
        title={t(`${K}.removeTitle`)}
        message={t(`${K}.removeMessage`, { sku: removing?.sku ?? '', name: removing?.productName ?? '' })}
        confirmLabel={t(`${K}.remove`)}
        tone="danger"
        onClose={() => setRemoving(null)}
        onConfirm={async () => {
          if (!removing?.itemId) return
          await actions.remove.mutateAsync(removing.itemId)
          toast.success(t(`${K}.removed`))
        }}
      />
    </Panel>
  )
}

/** Avisos del servidor (precio especial más caro que la lista general): informan, no bloquean. */
function Warnings({ dto }: { dto: SpecialPricesDto }) {
  const t = useT()
  const warns = dto.warnings ?? []
  if (warns.length === 0) return null
  return (
    <section className="spx-warns" role="status" aria-label={t(`${K}.warningsTitle`)}>
      <b>{t(`${K}.warningsTitle`)}</b>
      <ul>
        {warns.map((w, i) => (
          <li key={`${w.productPublicId}-${i}`}>{w.message}</li>
        ))}
      </ul>
    </section>
  )
}

/** Alta en línea: producto (buscador de productos), precio, desde y hasta. */
function AddRow({ ownerKind, ownerPublicId, includeExpired }: { ownerKind: SpecialOwnerKind; ownerPublicId: string; includeExpired: boolean }) {
  const t = useT()
  const f = useFormat()
  const actions = useSpecialPriceActions(ownerKind, ownerPublicId, includeExpired)
  const [draft, setDraft] = useState<SpecialPriceDraft>(() => emptySpecialDraft(f.today()))
  const [error, setError] = useState<string | null>(null)
  const submit = async () => {
    const problem = specialDraftError(draft, f.today(), true)
    if (problem) return setError(t(`${K}.err.${problem}`))
    setError(null)
    try {
      await actions.add.mutateAsync(toSpecialRequest(draft))
      toast.success(t(`${K}.added`))
      setDraft(emptySpecialDraft(f.today()))
    } catch (e) {
      const p = applyProblemDetails(e)
      const fields = Object.values(p.errors ?? {}).flat()
      setError([p.title, ...fields.filter((m) => m !== p.title)].join(' '))
    }
  }
  return (
    <div className="spx-add">
      <div className="f spx-add-product">
        <label htmlFor="spx-add-p">{t(`${K}.addProduct`)}</label>
        <ProductPicker id="spx-add-p" ownOnly value={draft.productPublicId} onChange={(id) => setDraft({ ...draft, productPublicId: id })} />
      </div>
      <div className="f">
        <label htmlFor="spx-add-price">{t(`${K}.col.price`)}</label>
        <input id="spx-add-price" inputMode="decimal" value={draft.price} onChange={(e) => setDraft({ ...draft, price: e.target.value })} />
      </div>
      <div className="f">
        <label htmlFor="spx-add-from">{t(`${K}.col.from`)}</label>
        <DateField id="spx-add-from" value={draft.validFrom} onChange={(v) => setDraft({ ...draft, validFrom: v })} />
      </div>
      <div className="f">
        <label htmlFor="spx-add-to">{t(`${K}.col.to`)}</label>
        <DateField id="spx-add-to" value={draft.validTo} onChange={(v) => setDraft({ ...draft, validTo: v })} />
      </div>
      <button type="button" className="btn flow" disabled={actions.add.isPending} onClick={() => void submit()}>
        {actions.add.isPending ? t('common.loading') : t(`${K}.add`)}
      </button>
      {error && (
        <p className="ferr spx-err" role="alert">
          {error}
        </p>
      )}
      <p className="help spx-help">{t(`${K}.beforeTaxHelp`)}</p>
    </div>
  )
}

/** Cambiar precio y fechas de un renglón (con fechas nuevas pasa a la lista de esas fechas). */
function EditModal({ row, ownerKind, ownerPublicId, includeExpired, onClose }: { row: SpecialPriceRowDto; ownerKind: SpecialOwnerKind; ownerPublicId: string; includeExpired: boolean; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const actions = useSpecialPriceActions(ownerKind, ownerPublicId, includeExpired)
  const [draft, setDraft] = useState<SpecialPriceDraft>(() => draftOfRow(row))
  const [error, setError] = useState<string | null>(null)
  const save = async () => {
    const problem = specialDraftError(draft, f.today(), false)
    if (problem) return setError(t(`${K}.err.${problem}`))
    setError(null)
    try {
      await actions.update.mutateAsync({ itemId: row.itemId ?? 0, body: toSpecialPatch(draft) })
      toast.success(t(`${K}.saved`))
      onClose()
    } catch (e) {
      const p = applyProblemDetails(e)
      const fields = Object.values(p.errors ?? {}).flat()
      setError([p.title, ...fields.filter((m) => m !== p.title)].join(' '))
    }
  }
  return (
    <Modal
      open
      title={t(`${K}.editTitle`, { sku: row.sku ?? '' })}
      onClose={onClose}
      dismissible={!actions.update.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={actions.update.isPending} onClick={() => void save()}>
            {actions.update.isPending ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p className="spx-note">
          <b className="mono">{row.sku}</b> {row.productName}
        </p>
        <div className="f">
          <label htmlFor="spx-edit-price">{t(`${K}.col.price`)}</label>
          <input id="spx-edit-price" inputMode="decimal" value={draft.price} onChange={(e) => setDraft({ ...draft, price: e.target.value })} />
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="spx-edit-from">{t(`${K}.col.from`)}</label>
            <DateField id="spx-edit-from" value={draft.validFrom} onChange={(v) => setDraft({ ...draft, validFrom: v })} />
          </div>
          <div className="f">
            <label htmlFor="spx-edit-to">{t(`${K}.col.to`)}</label>
            <DateField id="spx-edit-to" value={draft.validTo} onChange={(v) => setDraft({ ...draft, validTo: v })} />
          </div>
        </div>
        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** En la ficha del cliente: los precios especiales de su cadena, en solo lectura y con enlace a la cadena. */
function ChainSection({ dto, cards }: { dto: SpecialPricesDto; cards: boolean }) {
  const t = useT()
  const columns = useRowColumns(false, dto.chainName)
  if (!dto.chainPublicId) return null
  const rows = dto.chainRows ?? []
  return (
    <section className="spx-section" aria-label={t(`${K}.chainTitle`, { name: dto.chainName ?? '' })}>
      <h3 className="spx-h">
        {t(`${K}.chainTitle`, { name: dto.chainName ?? '' })}{' '}
        <Link className="spx-link" to={`/catalog/chains?id=${dto.chainPublicId}`}>
          {t(`${K}.openChain`)}
        </Link>
      </h3>
      <p className="help spx-help">{t(`${K}.chainHelp`)}</p>
      {rows.length === 0 ? (
        <p className="spx-note">{t(`${K}.chainEmpty`)}</p>
      ) : (
        <DataTable label={t(`${K}.chainTitle`, { name: dto.chainName ?? '' })} forceCards={cards} columns={columns} rows={rows} rowKey={(r) => r.itemId ?? 0} pageSize={10} exportable={false} />
      )}
    </section>
  )
}

/** Las listas normales que alcanzan al cliente (por él o por su cadena), como chips. */
function ClientLists({ dto }: { dto: SpecialPricesDto }) {
  const t = useT()
  const lists = dto.clientLists ?? []
  if (lists.length === 0) return null
  return (
    <section className="spx-section" aria-label={t(`${K}.listsTitle`)}>
      <h3 className="spx-h">{t(`${K}.listsTitle`)}</h3>
      <div className="spx-chips">
        {lists.map((l) => (
          <Chip key={l.priceListPublicId} tone="neutral">
            {l.name} · {t(`${K}.listBy.${l.matchedBy === 'CHAIN' ? 'CHAIN' : 'CLIENT'}`)}
          </Chip>
        ))}
      </div>
    </section>
  )
}
