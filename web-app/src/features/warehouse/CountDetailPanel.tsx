// Lote 14 (P8) — panel derecho de 'Conteo cíclico': el conteo elegido (`?count=<id>`, "Conteo · A-01 · CC-00012" de la
// maqueta `conteo()`), lo que antes era la ficha `/warehouse/cycle-counts/:id`.
// - TODAS las líneas desde el inicio con lo esperado (D9): SKU, Producto, Posición (si hay más de una), Lote (si alguna lo
//   tiene), Esperado, Contado EDITABLE EN LA FILA (guarda al salir del campo o con Enter y pasa a la línea siguiente;
//   `useCountDrafts`) y Varianza con color; SERIAL: botón "Series (n)" con el modal de series.
// - Escáner arriba (`CountScanBox`): el código lleva a la línea (resaltada) y pide la cantidad (`CountQtyModal`).
// - "Confirmar conteo y ajustar" en UN paso desde Pendiente o Contado (D8, `POST /reconcile`, warehouse.count) con
//   confirmación; termina en Concordancia (sin ajustes) o Diferencia (con ajustes en el Kárdex). Cerrado: aviso y enlace a los
//   ajustes del Kárdex (`?refEntity=CYCLE_COUNT&refId=`).
// - "Agregar lo encontrado", "Refrescar foto" (si hay fotos viejas) e Historial en la cabecera.
// - A ciegas (sin warehouse.count: el API omite lo esperado): solo lectura; la web no cuenta a ciegas (eso es la app).
import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, StatusHistory } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  EmptyState,
  IconCheck,
  IconClip,
  Modal,
  Panel,
  Spinner,
  toast,
  useElementWidth,
  type DataColumn,
} from '../../kernel/ui'
import { useCycleCount, useCycleCountAction, warehouseKeys, type CycleCountDetailDto, type CycleCountDto, type CycleCountLineDto } from './api'
import { AddFoundLineModal, CountQtyModal } from './CountLineModals'
import { CountScanBox } from './CountScanBox'
import {
  COUNT_ENTITY_TYPE,
  COUNT_STATUS_DOMAIN,
  confirmBlocker,
  countedText,
  countWhere,
  isChangesOrigin,
  isCountClosed,
  isCountEditable,
  lineVariance,
  type CountLineMatch,
} from './countView'
import { formatDateTime, formatNumber } from './lineRules'
import { useCountDrafts, type CountDraftsState } from './useCountDrafts'

/** Bajo este ancho del panel la rejilla pasa a tarjetas. */
const CARDS_BELOW_PX = 560

// ---------------------------------------------------------------------------------------------------------------------
// Rejilla de líneas: las columnas NO dependen de lo tecleado (las celdas leen un contexto) para no perder el foco
// ---------------------------------------------------------------------------------------------------------------------
interface GridContextValue {
  drafts: CountDraftsState
  canCapture: boolean
  position: ReadonlyMap<number, number>
  openSerials: (line: CycleCountLineDto) => void
}
const GridContext = createContext<GridContextValue | null>(null)

function useGrid(): GridContextValue {
  const ctx = useContext(GridContext)
  if (!ctx) throw new Error('CountDetailPanel: celda fuera de la rejilla')
  return ctx
}

function CountedCell({ line }: { line: CycleCountLineDto }) {
  const t = useT()
  const lang = useLang()
  const { drafts, canCapture, position, openSerials } = useGrid()
  const id = line.id ?? 0
  const n = position.get(id) ?? 0
  const error = drafts.errors.get(id)
  const savingLine = drafts.saving.has(id)
  if (line.trackingTypeCode === 'SERIAL') {
    const count = line.countedQty == null ? null : line.countedQty
    if (!canCapture) return <span className="mono">{count == null ? '—' : formatNumber(count, lang)}</span>
    return (
      <span className="rcp-cell">
        <button type="button" className="btn sm cc-serials" aria-label={t('warehouse.cycleCounts.detail.serialsOf', { n })} onClick={() => openSerials(line)}>
          {t('warehouse.cycleCounts.detail.serialsBtn', { n: count == null ? '—' : formatNumber(count, lang) })}
        </button>
        {error && (
          <span className="ferr" role="alert">
            {error}
          </span>
        )}
      </span>
    )
  }
  if (!canCapture) {
    return line.countedQty == null ? <Chip tone="neutral">{t('warehouse.cycleCounts.detail.pending')}</Chip> : <span className="mono">{formatNumber(line.countedQty, lang)}</span>
  }
  const draft = drafts.drafts.get(id)
  return (
    <span className="rcp-cell">
      <input
        className="rcp-qty cc-qty"
        type="text"
        inputMode="decimal"
        autoComplete="off"
        aria-label={t('warehouse.cycleCounts.detail.countedOf', { n, sku: line.sku ?? '' })}
        aria-invalid={error ? true : undefined}
        placeholder="—"
        value={draft ?? countedText(line.countedQty)}
        onChange={(e) => drafts.input(id, e.target.value)}
        onFocus={(e) => e.currentTarget.select()}
        onBlur={() => void drafts.save(id)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            // Enter guarda y pasa a la línea siguiente (el blur del campo dispara el guardado)
            const grid = e.currentTarget.closest('.cc-lines')
            const inputs = grid ? Array.from(grid.querySelectorAll<HTMLInputElement>('input.cc-qty')) : []
            const next = inputs[inputs.indexOf(e.currentTarget) + 1]
            if (next) next.focus()
            else void drafts.save(id)
          }
        }}
      />
      {savingLine && (
        <span className="rcp-faint" role="status">
          {t('warehouse.cycleCounts.detail.saving')}
        </span>
      )}
      {error && (
        <span className="ferr" role="alert">
          {error}
        </span>
      )}
    </span>
  )
}

function VarianceCell({ line }: { line: CycleCountLineDto }) {
  const lang = useLang()
  const { drafts } = useGrid()
  const d = lineVariance(line, drafts.drafts.get(line.id ?? 0))
  if (d === null) return <span className="rcp-faint">—</span>
  return <span className={d === 0 ? 'mono rcp-diff0' : 'mono rcp-diff'}>{`${d > 0 ? '+' : ''}${formatNumber(d, lang)}`}</span>
}

function CountLinesGrid({
  detail,
  drafts,
  canCapture,
  hit,
  onSerials,
}: {
  detail: CycleCountDetailDto
  drafts: CountDraftsState
  canCapture: boolean
  hit: number | null
  onSerials: (line: CycleCountLineDto) => void
}) {
  const t = useT()
  const lang = useLang()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const lines = useMemo(() => detail.lines ?? [], [detail.lines])
  const blind = Boolean(detail.isBlind)
  const closed = isCountClosed(detail.count?.statusCode)
  const editable = isCountEditable(detail.count?.statusCode)
  const showBin = new Set(lines.map((l) => l.binId)).size > 1
  const showLot = lines.some((l) => l.lotNumber)
  const position = useMemo(() => new Map(lines.map((l, i) => [l.id ?? 0, i + 1])), [lines])
  const ctx = useMemo<GridContextValue>(() => ({ drafts, canCapture, position, openSerials: onSerials }), [drafts, canCapture, position, onSerials])

  // el escáner lleva a la línea: se desplaza hasta ella
  useEffect(() => {
    if (hit == null) return
    boxRef.current?.querySelector('.cc-hit')?.scrollIntoView?.({ block: 'nearest' })
  }, [hit])

  const columns = useMemo<DataColumn<CycleCountLineDto>[]>(
    () => [
      { id: 'sku', header: t('warehouse.cycleCounts.detail.sku'), cell: (l) => <span className="ref">{l.sku}</span>, sortValue: (l) => l.sku, card: 'title' },
      { id: 'product', header: t('warehouse.cycleCounts.detail.product'), cell: (l) => l.productName ?? '—', sortValue: (l) => l.productName },
      ...(showBin
        ? [{ id: 'bin', header: t('warehouse.cycleCounts.detail.bin'), cell: (l: CycleCountLineDto) => l.binCode ?? '—', sortValue: (l: CycleCountLineDto) => l.binCode }]
        : []),
      ...(showLot
        ? [{ id: 'lot', header: t('warehouse.cycleCounts.detail.lot'), cell: (l: CycleCountLineDto) => l.lotNumber ?? '—', sortValue: (l: CycleCountLineDto) => l.lotNumber }]
        : []),
      ...(blind
        ? []
        : [
            {
              id: 'expected',
              header: t('warehouse.cycleCounts.detail.expected'),
              align: 'end' as const,
              cell: (l: CycleCountLineDto) => (
                <span className="cc-expected">
                  <span className="mono">{formatNumber(l.systemQty, lang) || '0'}</span>
                  {editable && l.isStale && (
                    <Chip tone="warn" title={t('warehouse.cycleCounts.detail.staleHelp')}>
                      {t('warehouse.cycleCounts.detail.stale')}
                    </Chip>
                  )}
                  {closed && l.systemQtyChanged && (
                    <Chip tone="warn" title={t('warehouse.cycleCounts.detail.changedHelp')}>
                      {t('warehouse.cycleCounts.detail.changed')}
                    </Chip>
                  )}
                </span>
              ),
              sortValue: (l: CycleCountLineDto) => l.systemQty ?? 0,
              exportValue: (l: CycleCountLineDto) => l.systemQty ?? null,
            },
          ]),
      {
        id: 'counted',
        header: t('warehouse.cycleCounts.detail.counted'),
        align: 'end',
        cell: (l) => <CountedCell line={l} />,
        sortValue: (l) => l.countedQty ?? undefined,
        exportValue: (l) => l.countedQty ?? null,
      },
      ...(blind
        ? []
        : [
            {
              id: 'variance',
              header: t('warehouse.cycleCounts.detail.variance'),
              align: 'end' as const,
              cell: (l: CycleCountLineDto) => <VarianceCell line={l} />,
              sortValue: (l: CycleCountLineDto) => lineVariance(l) ?? undefined,
              exportValue: (l: CycleCountLineDto) => lineVariance(l),
            },
          ]),
      ...(closed && !blind
        ? [
            {
              id: 'adjusted',
              header: t('warehouse.cycleCounts.detail.adjusted'),
              align: 'end' as const,
              cell: (l: CycleCountLineDto) => (l.adjustedQty != null && l.adjustedQty !== 0 ? <span className="mono">{`${l.adjustedQty > 0 ? '+' : ''}${formatNumber(l.adjustedQty, lang)}`}</span> : <span className="rcp-faint">—</span>),
              sortValue: (l: CycleCountLineDto) => l.adjustedQty ?? undefined,
              exportValue: (l: CycleCountLineDto) => l.adjustedQty ?? null,
            },
          ]
        : []),
    ],
    [t, lang, showBin, showLot, blind, editable, closed],
  )

  return (
    <div ref={boxRef} className="rcp-lines cc-lines">
      <GridContext.Provider value={ctx}>
        <DataTable
          label={t('warehouse.cycleCounts.detail.lines')}
          columns={columns}
          rows={lines}
          rowKey={(l) => l.id ?? 0}
          rowClassName={(l) => [l.id === hit ? 'cc-hit' : '', drafts.saving.has(l.id ?? 0) ? 'rcp-saving' : ''].filter(Boolean).join(' ') || undefined}
          pagination={false}
          forceCards={width > 0 && width < CARDS_BELOW_PX}
          exportFileName={detail.count?.number ?? undefined}
          empty={<EmptyState title={t('warehouse.cycleCounts.detail.noLines')} />}
        />
      </GridContext.Provider>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Conteo cargado
// ---------------------------------------------------------------------------------------------------------------------
function CountBody({ detail }: { detail: CycleCountDetailDto }) {
  const t = useT()
  const lang = useLang()
  const qc = useQueryClient()
  const canCount = useCan('warehouse.count')
  const action = useCycleCountAction()
  const drafts = useCountDrafts(detail)
  const count: CycleCountDto = detail.count ?? {}
  const id = count.id ?? 0
  const lines = useMemo(() => detail.lines ?? [], [detail.lines])
  const blind = Boolean(detail.isBlind)
  const editable = isCountEditable(count.statusCode)
  const closed = isCountClosed(count.statusCode)
  // la web cuenta en modo informado: capturar exige warehouse.count y un conteo abierto (a ciegas, solo en la app)
  const canCapture = canCount && editable && !blind
  const [qtyFor, setQtyFor] = useState<{ line: CycleCountLineDto; serial?: string | null } | null>(null)
  const [hit, setHit] = useState<number | null>(null)
  const [adding, setAdding] = useState(false)
  const [history, setHistory] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [refreshing, setRefreshing] = useState(false)
  const anyStale = lines.some((l) => l.isStale)
  const blocker = confirmBlocker({ statusCode: count.statusCode, isBlind: blind, lines, drafts: drafts.drafts })
  const varianceLines = lines.filter((l) => {
    const d = lineVariance(l, drafts.drafts.get(l.id ?? 0))
    return d !== null && d !== 0
  }).length

  const onPick = (m: CountLineMatch) => {
    setHit(m.line.id ?? null)
    setQtyFor({ line: m.line, serial: m.by === 'serial' ? m.serial : null })
  }
  const openSerials = useCallback((line: CycleCountLineDto) => setQtyFor({ line }), [])

  const startConfirm = async () => {
    // lo tecleado se guarda antes de confirmar
    const ok = await drafts.flush()
    if (!ok) {
      toast.error(t('warehouse.cycleCounts.detail.unsaved'))
      return
    }
    setConfirming(true)
  }

  const refresh = async () => {
    setRefreshing(true)
    try {
      await action.mutateAsync({ id, action: 'refresh' })
      toast.success(t('warehouse.cycleCounts.detail.refreshed'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    } finally {
      setRefreshing(false)
    }
  }

  const w = countWhere(count)
  const title = (
    <>
      {t('warehouse.cycleCounts.detail.title')} ·{' '}
      <span className="ref">{w.kind === 'bin' ? w.code : w.kind === 'many' ? t('warehouse.cycleCounts.list.positions', { n: w.bins }) : count.warehouseCode}</span>
    </>
  )

  return (
    <Panel
      flush
      icon={<IconClip />}
      title={title}
      badge={count.number ?? undefined}
      className="cc-detail"
      actions={
        <>
          <button type="button" className="btn sm" onClick={() => setHistory(true)}>
            {t('warehouse.cycleCounts.detail.history')}
          </button>
          {canCapture && anyStale && (
            <button type="button" className="btn sm" disabled={refreshing} onClick={() => void refresh()}>
              {refreshing ? t('common.loading') : t('warehouse.cycleCounts.detail.refresh')}
            </button>
          )}
          {canCapture && (
            <button type="button" className="btn sm" onClick={() => setAdding(true)}>
              {t('warehouse.cycleCounts.detail.addLine')}
            </button>
          )}
        </>
      }
    >
      <div className="cc-head">
        <StatusChip domain={COUNT_STATUS_DOMAIN} code={count.statusCode} label={count.status} />
        {count.origin && <span className={isChangesOrigin(count) ? 'tag' : 'tag cc-plain'}>{count.origin}</span>}
        <span className="cc-head-meta">
          {[
            count.warehouseCode,
            w.kind === 'bin' && w.zone ? t('warehouse.cycleCounts.list.zone', { zone: w.zone }) : null,
            t('warehouse.cycleCounts.detail.progress', { counted: count.countedLines ?? 0, total: count.lineCount ?? 0 }),
            formatDateTime(count.createdAtUtc, lang),
            count.taskId != null
              ? count.assignedToName
                ? t('warehouse.cycleCounts.list.assigned', { name: count.assignedToName })
                : t('warehouse.cycleCounts.list.unassigned')
              : null,
          ]
            .filter(Boolean)
            .join(' · ')}
        </span>
        {count.changesFromUtc && count.changesToUtc && (
          <span className="cc-head-meta">
            {t('warehouse.cycleCounts.detail.window', { from: formatDateTime(count.changesFromUtc, lang), to: formatDateTime(count.changesToUtc, lang) })}
          </span>
        )}
      </div>

      {blind && <p className="note cc-note">{t('warehouse.cycleCounts.detail.blindNote')}</p>}
      {canCapture && anyStale && <p className="note cc-note">{t('warehouse.cycleCounts.detail.staleNote')}</p>}
      {canCapture && (
        <div className="cc-scanrow">
          <CountScanBox lines={lines} onPick={onPick} />
        </div>
      )}

      <CountLinesGrid detail={detail} drafts={drafts} canCapture={canCapture} hit={hit} onSerials={openSerials} />

      {closed && (
        <div className="note rcp-note rcp-note-money cc-closed">
          <IconCheck />
          <span>
            {t('warehouse.cycleCounts.detail.closedNote')}{' '}
            <Link to={`/warehouse/kardex?refEntity=${COUNT_ENTITY_TYPE}&refId=${id}`}>{t('warehouse.cycleCounts.detail.seeAdjustments')}</Link>
          </span>
        </div>
      )}
      {canCount && editable && !blind && (
        <>
          <p className="note rcp-note">{t('warehouse.cycleCounts.detail.confirmNote', { number: count.number ?? '' })}</p>
          <div className="rcp-confirm">
            <button
              type="button"
              className="btn flow block"
              // no se deshabilita mientras guarda una fila (el clic llega tras el blur del campo): Confirmar espera esos guardados
              disabled={blocker !== null}
              aria-describedby={blocker ? `cc-block-${id}` : undefined}
              onClick={() => void startConfirm()}
            >
              <IconCheck /> {t('warehouse.cycleCounts.detail.confirm')}
            </button>
            {blocker && (
              <p className="help" id={`cc-block-${id}`}>
                {t(`warehouse.cycleCounts.detail.blockers.${blocker.key}`, 'params' in blocker ? blocker.params : undefined)}
              </p>
            )}
          </div>
        </>
      )}

      {qtyFor && (
        <CountQtyModal
          line={qtyFor.line}
          scannedSerial={qtyFor.serial}
          isBlind={blind}
          onSave={(body) => drafts.capture(qtyFor.line.id ?? 0, body)}
          onClose={() => setQtyFor(null)}
        />
      )}
      {adding && <AddFoundLineModal detail={detail} onClose={() => setAdding(false)} />}
      <Modal open={history} title={t('warehouse.cycleCounts.detail.historyTitle', { number: count.number ?? '' })} onClose={() => setHistory(false)}>
        <StatusHistory entityType={COUNT_ENTITY_TYPE} entityId={id} domain={COUNT_STATUS_DOMAIN} />
      </Modal>
      <ConfirmDialog
        open={confirming}
        tone="flow"
        title={t('warehouse.cycleCounts.detail.confirmTitle')}
        message={t('warehouse.cycleCounts.detail.confirmBody', { number: count.number ?? '', n: varianceLines })}
        confirmLabel={t('warehouse.cycleCounts.detail.confirm')}
        onConfirm={async () => {
          try {
            const dto = await action.mutateAsync({ id, action: 'reconcile', body: { rowVersion: drafts.rowVersion() } })
            const code = dto?.count?.statusCode
            const adjusted = (dto?.lines ?? []).filter((l) => l.adjustedQty != null && l.adjustedQty !== 0).length
            if (code === 'RECONCILED_VARIANCE')
              toast.success(t('warehouse.cycleCounts.detail.confirmedVariance', { number: count.number ?? '', status: dto?.count?.status ?? '', n: adjusted }))
            else toast.success(t('warehouse.cycleCounts.detail.confirmedMatch', { number: count.number ?? '', status: dto?.count?.status ?? '' }))
          } catch (err) {
            if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.cycleCount })
            throw err
          }
        }}
        onClose={() => setConfirming(false)}
      />
    </Panel>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Panel
// ---------------------------------------------------------------------------------------------------------------------
export function CountDetailPanel({ id }: { id: number | null }) {
  const t = useT()
  const { data, isLoading, error } = useCycleCount(id, {}, { enabled: id != null })

  if (id == null)
    return (
      <Panel flush className="cc-detail">
        <div className="empty rcp-empty lg">
          <div>
            <IconClip />
            <p>{t('warehouse.cycleCounts.detail.select')}</p>
          </div>
        </div>
      </Panel>
    )
  // la ficha anterior se queda mientras llega la nueva (keepPreviousData): se espera la del conteo elegido
  if (isLoading || (data && data.count?.id !== id && !error)) return <Panel flush className="cc-detail"><Spinner block /></Panel>
  if (error || !data?.count) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <Panel flush className="cc-detail">
        <EmptyState title={notFound ? t('warehouse.cycleCounts.notFound') : error ? applyProblemDetails(error).title : t('errors.generic')} />
      </Panel>
    )
  }
  // `key`: al cambiar de conteo se descarta lo tecleado del anterior
  return <CountBody key={id} detail={data} />
}
