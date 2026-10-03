// Lote F12 — pestaña "Por revisar" de 'Conteo cíclico' (diseño del conteo por producto, "Web — revisión rápida"; warehouse.count).
// - `GET /cycle-counts/review`: conteos Contados (los terminó el operario), más recientes primero, paginados en el servidor.
//   Columnas ordenables (orden local de la página: el endpoint no ordena): conteo, quién contó, producto (el primero y "y N
//   más"), posiciones, líneas, con diferencia, correcciones y el estado calculado (Cuadra, Con diferencia, Con errores, Faltan
//   líneas). "Cuadra" y las diferencias se miden contra la existencia ACTUAL (lo calcula el servidor con el plan de reconciliar).
// - Filtros: almacén (uno), quién contó y buscador libre (número, SKU o producto), todos al API y a la página 1.
// - "Cerrar los que cuadran": los elegidos que cuadran o, sin elegir, todos los que cuadran de la página que se ve; se
//   mandan por `ids` a `POST /cycle-counts/reconcile-matching` (con comentario opcional, queda en el historial de cada uno),
//   con una confirmación que dice cuántos y cuáles. El resultado muestra lo cerrado y lo omitido con su motivo en español.
// - Clic en una fila = abrir el conteo a la derecha (`?count=<id>`) para revisarlo, corregirlo y confirmarlo.
import { useQuery } from '@tanstack/react-query'
import { useId, useMemo, useRef, useState } from 'react'
import { useCan } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  EmptyState,
  FilterScope,
  Filters,
  IconCheck,
  IconClip,
  Modal,
  Panel,
  QBox,
  SelectFilter,
  SplitPane,
  toast,
  useElementWidth,
  type DataColumn,
} from '../../kernel/ui'
import { CountDetailPanel } from './CountDetailPanel'
import { exportCycleCountReview, useCycleCountReview, useReconcileMatching, type CycleCountReviewItemDto } from './api'
import {
  BULK_COMMENT_MAX,
  bulkSummary,
  counterOptions,
  EMPTY_REVIEW_FILTERS,
  idsToClose,
  keepSelection,
  rememberCounters,
  REVIEW_STATE_ORDER,
  REVIEW_STATE_TONE,
  reviewFilterQuery,
  reviewListQuery,
  reviewProduct,
  reviewState,
  type BulkSummary,
  type ReviewFilters,
} from './countReview'
import { useDebounced, formatNumber } from './lineRules'
import { WarehousePicker } from './pickers'
import { problemText } from './problemText'

const NO_ITEMS: CycleCountReviewItemDto[] = []
const DEFAULT_PAGE_SIZE = 25
/** Bajo este ancho del panel la tabla pasa a tarjetas. */
const CARDS_BELOW_PX = 720
/** Números de conteo que se listan en la confirmación (los demás, "y N más"). */
const LIST_IN_CONFIRM = 12

export interface CountReviewListProps {
  /** `?count=` de la URL (null = el primero de la página). */
  countId: number | null
  onSelect: (id: number) => void
}

/** La pestaña completa: la lista "Por revisar" a la izquierda y el conteo elegido a la derecha (`CountDetailPanel`). */
export function CountReviewTab({ countId, onSelect }: CountReviewListProps) {
  const t = useT()
  const lang = useLang()
  const canUsers = useCan('admin.users')
  const [filters, setFilters] = useState<ReviewFilters>(EMPTY_REVIEW_FILTERS)
  const [q, setQ] = useState('')
  const search = useDebounced(q.trim())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [selected, setSelected] = useState<ReadonlySet<number>>(() => new Set())
  const [seen, setSeen] = useState<ReadonlyMap<number, string>>(() => new Map())
  const [closing, setClosing] = useState(false)
  const [result, setResult] = useState<BulkSummary | null>(null)
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)

  const effective = useMemo(() => ({ ...filters, search }), [filters, search])
  const query = useMemo(() => reviewListQuery(effective, page, pageSize), [effective, page, pageSize])
  const list = useCycleCountReview(query)
  const items = list.data?.items ?? NO_ITEMS

  const selectedId = countId ?? items[0]?.count?.id ?? null
  // al llegar otra página: quienes contaron alimentan el filtro "Contó" (el API no tiene ese catálogo) y se descartan los
  // elegidos que ya no cuadran (ajuste de estado durante el render, sin efecto)
  const [prevItems, setPrevItems] = useState(items)
  if (prevItems !== items) {
    setPrevItems(items)
    setSeen(rememberCounters(seen, items))
    setSelected(keepSelection(selected, items))
  }
  const users = useQuery({
    queryKey: ['/api/v1/users'],
    queryFn: () => unwrap(api.GET('/api/v1/users')),
    enabled: canUsers,
    meta: { handleAccessDenied: false },
  })
  const counters = useMemo(() => counterOptions(seen, users.data ?? []), [seen, users.data])

  const update = (patch: Partial<ReviewFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }))
    setPage(1)
  }

  const toClose = idsToClose(items, selected)
  const matchingCount = idsToClose(items, new Set()).length

  const toggle = (id: number, on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (on) next.add(id)
      else next.delete(id)
      return next
    })

  const columns = useMemo<DataColumn<CycleCountReviewItemDto>[]>(() => {
    const num = (n: number | null | undefined) => formatNumber(n ?? 0, lang) || '0'
    const stateLabel = (it: CycleCountReviewItemDto) => t(`warehouse.cycleCounts.review.states.${reviewState(it)}`)
    const end = 'end' as const
    return [
      {
        id: 'select',
        header: t('warehouse.cycleCounts.review.columns.select'),
        exportable: false,
        cell: (it) => {
          const id = it.count?.id ?? 0
          const eligible = reviewState(it) === 'matches'
          return (
            <input
              type="checkbox"
              className="cc-review-check"
              checked={eligible && selected.has(id)}
              disabled={!eligible}
              aria-label={t('warehouse.cycleCounts.review.selectRow', { number: it.count?.number ?? '' })}
              title={eligible ? undefined : t('warehouse.cycleCounts.review.selectOnlyMatching')}
              // la casilla no abre el conteo (clic ni Espacio/Enter llegan a la fila)
              onClick={(e) => e.stopPropagation()}
              onKeyDown={(e) => e.stopPropagation()}
              onChange={(e) => toggle(id, e.target.checked)}
            />
          )
        },
      },
      {
        id: 'number',
        header: t('warehouse.cycleCounts.review.columns.number'),
        cell: (it) => (
          <span className="cc-review-num">
            <span className="ref">{it.count?.number ?? '—'}</span>
            <span className="rcp-faint">{it.count?.warehouseCode}</span>
          </span>
        ),
        sortValue: (it) => it.count?.number,
        exportValue: (it) => it.count?.number ?? '',
        card: 'title',
      },
      {
        id: 'countedBy',
        header: t('warehouse.cycleCounts.review.columns.countedBy'),
        cell: (it) =>
          it.countedByName
            ? (it.countedByCount ?? 0) > 1
              ? t('warehouse.cycleCounts.review.counters', { name: it.countedByName, n: (it.countedByCount ?? 1) - 1 })
              : it.countedByName
            : '—',
        sortValue: (it) => it.countedByName,
      },
      {
        id: 'product',
        header: t('warehouse.cycleCounts.review.columns.product'),
        cell: (it) => {
          const p = reviewProduct(it)
          if (!p.label) return '—'
          return p.more > 0 ? t('warehouse.cycleCounts.review.more', { label: p.label, n: p.more }) : p.label
        },
        sortValue: (it) => it.firstProductSku,
      },
      { id: 'positions', header: t('warehouse.cycleCounts.review.columns.positions'), align: end, cell: (it) => <span className="mono">{num(it.positions)}</span>, sortValue: (it) => it.positions ?? 0, exportValue: (it) => it.positions ?? 0 },
      { id: 'lines', header: t('warehouse.cycleCounts.review.columns.lines'), align: end, cell: (it) => <span className="mono">{num(it.lines)}</span>, sortValue: (it) => it.lines ?? 0, exportValue: (it) => it.lines ?? 0 },
      {
        id: 'differing',
        header: t('warehouse.cycleCounts.review.columns.differing'),
        align: end,
        cell: (it) => <span className={(it.differingLines ?? 0) > 0 ? 'mono rcp-diff' : 'mono rcp-diff0'}>{num(it.differingLines)}</span>,
        sortValue: (it) => it.differingLines ?? 0,
        exportValue: (it) => it.differingLines ?? 0,
      },
      { id: 'corrected', header: t('warehouse.cycleCounts.review.columns.corrected'), align: end, cell: (it) => <span className="mono">{num(it.correctedLines)}</span>, sortValue: (it) => it.correctedLines ?? 0, exportValue: (it) => it.correctedLines ?? 0 },
      {
        id: 'state',
        header: t('warehouse.cycleCounts.review.columns.state'),
        cell: (it) => <Chip tone={REVIEW_STATE_TONE[reviewState(it)]}>{stateLabel(it)}</Chip>,
        sortValue: (it) => `${REVIEW_STATE_ORDER[reviewState(it)]} ${stateLabel(it)}`,
        exportValue: (it) => stateLabel(it),
      },
    ]
    // `selected` cambia la casilla: las columnas se rehacen al elegir (no hay campos de texto que pierdan el foco)
  }, [t, lang, selected])

  return (
    <SplitPane
      storageKey="cycle-count-review"
      defaultRatio={0.5}
      minRatio={0.3}
      maxRatio={0.7}
      minPx={[340, 480]}
      label={t('warehouse.cycleCounts.review.splitLabel')}
      className="cc-split"
    >
      <div ref={boxRef} className="cc-review">
        <Panel
          flush
          icon={<IconClip />}
          title={t('warehouse.cycleCounts.review.title')}
          badge={list.data ? (list.data.total ?? 0) : undefined}
          actions={
            <button
              type="button"
              className="btn sm flow"
              disabled={toClose.length === 0}
              title={toClose.length === 0 ? t('warehouse.cycleCounts.review.closeNone') : undefined}
              onClick={() => setClosing(true)}
            >
              <IconCheck /> {selected.size > 0 ? t('warehouse.cycleCounts.review.closeSelected', { n: toClose.length }) : t('warehouse.cycleCounts.review.closeMatching')}
            </button>
          }
        >
          <div className="cc-review-filters">
            <Filters
              label={t('warehouse.cycleCounts.review.filters.aria')}
              onClear={() => {
                setFilters(EMPTY_REVIEW_FILTERS)
                setQ('')
                setPage(1)
              }}
            >
              <div className="f">
                <label htmlFor="cc-review-wh">{t('warehouse.cycleCounts.review.filters.warehouse')}</label>
                <WarehousePicker
                  id="cc-review-wh"
                  value={filters.warehousePublicId || null}
                  onChange={(v) => update({ warehousePublicId: v ?? '' })}
                  placeholder={t('ui.filters.all')}
                  filterLabel={t('warehouse.cycleCounts.review.filters.warehouse')}
                />
              </div>
              <SelectFilter
                label={t('warehouse.cycleCounts.review.filters.countedBy')}
                value={filters.countedByUserId}
                onChange={(v) => update({ countedByUserId: v })}
                options={counters}
              />
            </Filters>
          </div>
          <div className="qrow">
            <QBox
              value={q}
              onChange={(v) => {
                setQ(v)
                setPage(1)
              }}
              placeholder={t('warehouse.cycleCounts.searchPlaceholder')}
            />
          </div>
          <p className="note cc-note cc-review-intro">{t('warehouse.cycleCounts.review.intro')}</p>
          {list.error ? (
            <p className="pb ferr" role="alert">
              {problemText(list.error)}
            </p>
          ) : (
            <DataTable
              label={t('warehouse.cycleCounts.review.title')}
              columns={columns}
              rows={items}
              rowKey={(it) => it.count?.id ?? 0}
              loading={list.isLoading}
              page={page}
              pageSize={pageSize}
              total={list.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(n) => {
                setPageSize(n)
                setPage(1)
              }}
              exportRows={() => exportCycleCountReview(reviewFilterQuery(effective))}
              exportFileName={t('warehouse.cycleCounts.review.exportName')}
              forceCards={width > 0 && width < CARDS_BELOW_PX}
              onRowClick={(it) => it.count?.id != null && onSelect(it.count.id)}
              rowClassName={(it) => (it.count?.id === selectedId ? 'cc-review-on' : undefined)}
              empty={<EmptyState title={t('warehouse.cycleCounts.review.empty')} />}
            />
          )}
          {items.length > 0 && (
            <p className="help cc-review-help">
              {matchingCount > 0 ? t('warehouse.cycleCounts.review.selectHelp', { n: matchingCount }) : t('warehouse.cycleCounts.review.closeNone')}
            </p>
          )}
        </Panel>

        {closing && (
          <CloseMatchingModal
            targets={toClose}
            onlySelected={selected.size > 0}
            onClose={() => setClosing(false)}
            onDone={(summary) => {
              setClosing(false)
              setSelected(new Set())
              setResult(summary)
              toast.success(t('warehouse.cycleCounts.review.closedToast', { closed: summary.closed.length, skipped: summary.skipped.length }))
            }}
          />
        )}
        {result && (
          <BulkResultModal
            result={result}
            onClose={() => setResult(null)}
            onOpen={(id) => {
              setResult(null)
              onSelect(id)
            }}
          />
        )}
      </div>
      {/* ámbito propio: las líneas del conteo elegido no dependen de los filtros de la lista */}
      <FilterScope>
        <CountDetailPanel id={selectedId} />
      </FilterScope>
    </SplitPane>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Confirmación del cierre en bloque
// ---------------------------------------------------------------------------------------------------------------------

function CloseMatchingModal({
  targets,
  onlySelected,
  onClose,
  onDone,
}: {
  targets: { id: number; number: string }[]
  onlySelected: boolean
  onClose: () => void
  onDone: (summary: BulkSummary) => void
}) {
  const t = useT()
  const commentId = useId()
  const helpId = useId()
  const close = useReconcileMatching()
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const tooLong = comment.trim().length > BULK_COMMENT_MAX
  const shown = targets.slice(0, LIST_IN_CONFIRM).map((x) => x.number)
  const list =
    targets.length > LIST_IN_CONFIRM ? t('warehouse.cycleCounts.review.listMore', { list: shown.join(', '), n: targets.length - LIST_IN_CONFIRM }) : shown.join(', ')

  const submit = async () => {
    if (tooLong || targets.length === 0) return
    setError(null)
    try {
      const res = await close.mutateAsync({ ids: targets.map((x) => x.id), comment: comment.trim() || null })
      onDone(bulkSummary(res, t))
    } catch (err) {
      setError(problemText(err))
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('warehouse.cycleCounts.review.closeTitle')}
      onClose={onClose}
      dismissible={!close.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={close.isPending}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={close.isPending || tooLong || targets.length === 0} onClick={() => void submit()}>
            {close.isPending ? t('common.loading') : t('warehouse.cycleCounts.review.closeConfirm', { n: targets.length })}
          </button>
        </>
      }
    >
      <p>{t('warehouse.cycleCounts.review.closeBody', { n: targets.length, list })}</p>
      <p className="note">{onlySelected ? t('warehouse.cycleCounts.review.closeSelectedNote') : t('warehouse.cycleCounts.review.closePageNote')}</p>
      <div className="f">
        <label htmlFor={commentId}>{t('warehouse.cycleCounts.review.comment')}</label>
        <textarea
          id={commentId}
          rows={3}
          value={comment}
          aria-invalid={tooLong || undefined}
          aria-describedby={helpId}
          onChange={(e) => setComment(e.target.value)}
        />
        <p className={tooLong ? 'ferr' : 'help'} id={helpId}>
          {tooLong ? t('warehouse.cycleCounts.review.commentTooLong') : t('warehouse.cycleCounts.review.commentHelp')}
        </p>
      </div>
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Resultado: lo cerrado y lo que quedó para revisar, con su motivo
// ---------------------------------------------------------------------------------------------------------------------

function BulkResultModal({ result, onClose, onOpen }: { result: BulkSummary; onClose: () => void; onOpen: (id: number) => void }) {
  const t = useT()
  return (
    <Modal
      open
      size="md"
      title={t('warehouse.cycleCounts.review.resultTitle')}
      onClose={onClose}
      footer={
        <button type="button" className="btn flow" onClick={onClose}>
          {t('common.done')}
        </button>
      }
    >
      <section className="cc-result" aria-label={t('warehouse.cycleCounts.review.closedHead', { n: result.closed.length })}>
        <h3>{t('warehouse.cycleCounts.review.closedHead', { n: result.closed.length })}</h3>
        {result.closed.length === 0 ? (
          <p className="help">{t('warehouse.cycleCounts.review.noneClosed')}</p>
        ) : (
          <ul>
            {result.closed.map((c) => (
              <li key={c.id}>{t('warehouse.cycleCounts.review.closedItem', { number: c.number, n: c.lines })}</li>
            ))}
          </ul>
        )}
      </section>
      {result.skipped.length > 0 && (
        <section className="cc-result" aria-label={t('warehouse.cycleCounts.review.skippedHead', { n: result.skipped.length })}>
          <h3>{t('warehouse.cycleCounts.review.skippedHead', { n: result.skipped.length })}</h3>
          <ul>
            {result.skipped.map((s) => (
              <li key={`${s.id}-${s.code}`}>
                {s.code === 'NotFound' ? (
                  <span className="ref">{s.number}</span>
                ) : (
                  <button type="button" className="linkbtn ref" onClick={() => onOpen(s.id)}>
                    {s.number}
                  </button>
                )}
                : {s.text}
              </li>
            ))}
          </ul>
        </section>
      )}
      {result.truncated && <p className="note">{t('warehouse.cycleCounts.review.truncated')}</p>}
    </Modal>
  )
}
