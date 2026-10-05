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
// Lote F12 (conteo por producto, revisión del supervisor):
// - con warehouse.count y el conteo abierto se pide la vista previa (`GET /reconcile-preview`): columna "Ajuste" contra la
//   existencia ACTUAL con el error de la línea; un conteo Contado abre mostrando SOLO LAS LÍNEAS QUE FALLAN (ajuste ≠ 0 o con
//   error) con el interruptor "Ver todas"; la fila que se corrige en la sesión no desaparece al dejar de fallar;
// - columna "Evidencia": *Contó X (quién, cuándo) · Corregido a Y (quién, cuándo)* (también a ciegas: no revela lo esperado);
//   editar la cantidad de un conteo Contado es una CORRECCIÓN (no un ajuste: no mueve inventario por sí misma);
// - posición provisional (`binIsProvisional`): chip "Posición pendiente de revisión" y, con warehouse.manage, "Confirmar posición";
// - "Confirmar conteo y ajustar" abre la vista previa del efecto (`ReconcilePreviewModal`) y desde ahí se confirma (todo o nada).
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, StatusHistory } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
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
import {
  useConfirmProvisionalBin,
  useCycleCount,
  useCycleCountAction,
  useReconcilePreview,
  type CycleCountDetailDto,
  type CycleCountDto,
  type CycleCountLineDto,
  type ReconcilePreviewLineDto,
  fetchProductByCode,
} from './api'
import { AddFoundLineModal, CountQtyModal, type AddFoundProduct } from './CountLineModals'
import { adjustmentClass, evidenceText, failingLines, lineEvidence, previewByLine, signedQty } from './countReview'
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
import { problemText } from './problemText'
import { ReconcilePreviewModal } from './ReconcilePreviewModal'
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
  /** Lote F12: vista previa por línea (null = sin permiso, cerrado o aún no llega). */
  preview: ReadonlyMap<number, ReconcilePreviewLineDto> | null
  /** Lote F12: la línea se tocó en esta sesión (se queda visible en "solo las que fallan"). */
  pin: (lineId: number) => void
  /** Lote F12: confirmar una posición provisional (null = sin warehouse.manage). */
  confirmBin: ((line: CycleCountLineDto) => void) | null
  confirmingBin: number | null
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
  const { drafts, canCapture, position, openSerials, pin } = useGrid()
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
        onChange={(e) => {
          pin(id)
          drafts.input(id, e.target.value)
        }}
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

/** Lote F12 — ajuste contra la existencia ACTUAL (vista previa) y el error de la línea, tal cual del servidor. */
function AdjustmentCell({ line }: { line: CycleCountLineDto }) {
  const t = useT()
  const lang = useLang()
  const { preview } = useGrid()
  const p = preview?.get(line.id ?? 0)
  if (!p || p.isPending) return <span className="rcp-faint">—</span>
  const num = (n: number) => formatNumber(n, lang)
  return (
    <span className="cc-adjcell">
      <span
        className={`mono ${adjustmentClass(p.adjustmentQty)}`}
        title={t('warehouse.cycleCounts.detail.adjustmentHelp', { current: num(p.currentQty ?? 0) || '0', resulting: num(p.resultingQty ?? 0) || '0' })}
      >
        {signedQty(p.adjustmentQty, num)}
      </span>
      {p.error && <span className="ferr cc-line-err">{p.error}</span>}
    </span>
  )
}

/** Lote F12 — evidencia: *Contó X (quién, cuándo)* y, si se corrigió, *Corregido a Y (quién, cuándo)*. */
function EvidenceCell({ line }: { line: CycleCountLineDto }) {
  const t = useT()
  const lang = useLang()
  const ev = lineEvidence(line)
  if (!ev.counted && !ev.corrected) return <span className="rcp-faint">—</span>
  const num = (n: number | null | undefined) => formatNumber(n, lang) || '0'
  const when = (iso: string | null | undefined) => formatDateTime(iso, lang)
  const one = (key: 'counted' | 'corrected') => {
    const step = ev[key]
    if (!step) return null
    const who = [step.by, step.at ? when(step.at) : null].filter(Boolean).join(', ')
    const qty = step.qty == null ? '—' : num(step.qty)
    return t(`warehouse.cycleCounts.evidence.${key}${who ? 'By' : ''}`, { qty, who })
  }
  return (
    <span className="cc-evidence">
      {ev.counted && <span>{one('counted')}</span>}
      {ev.corrected && (
        <span className="cc-evidence-fix">
          <Chip tone="disp">{t('warehouse.cycleCounts.evidence.correction')}</Chip>
          <span>{one('corrected')}</span>
        </span>
      )}
    </span>
  )
}

/** Lote F12 — posición con el chip "Posición pendiente de revisión" y, con warehouse.manage, "Confirmar posición". */
function BinCell({ line }: { line: CycleCountLineDto }) {
  const t = useT()
  const { confirmBin, confirmingBin } = useGrid()
  if (!line.binIsProvisional) return <>{line.binCode ?? '—'}</>
  return (
    <span className="cc-bincell">
      <span>{line.binCode ?? '—'}</span>
      <Chip tone="warn" title={t('warehouse.cycleCounts.provisional.help')}>
        {t('warehouse.cycleCounts.provisional.chip')}
      </Chip>
      {confirmBin && (
        <button
          type="button"
          className="btn sm"
          disabled={confirmingBin === line.binId}
          aria-label={t('warehouse.cycleCounts.provisional.confirmOf', { code: line.binCode ?? '' })}
          onClick={() => confirmBin(line)}
        >
          {confirmingBin === line.binId ? t('common.loading') : t('warehouse.cycleCounts.provisional.confirm')}
        </button>
      )}
    </span>
  )
}

function CountLinesGrid({
  detail,
  rows,
  drafts,
  canCapture,
  hit,
  onSerials,
  preview,
  pin,
  confirmBin,
  confirmingBin,
}: {
  detail: CycleCountDetailDto
  /** Líneas que se pintan (todas o solo las que fallan); las posiciones `n` de las etiquetas son las de TODAS. */
  rows: readonly CycleCountLineDto[]
  drafts: CountDraftsState
  canCapture: boolean
  hit: number | null
  onSerials: (line: CycleCountLineDto) => void
  preview: ReadonlyMap<number, ReconcilePreviewLineDto> | null
  pin: (lineId: number) => void
  confirmBin: ((line: CycleCountLineDto) => void) | null
  confirmingBin: number | null
}) {
  const t = useT()
  const lang = useLang()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const lines = useMemo(() => detail.lines ?? [], [detail.lines])
  const blind = Boolean(detail.isBlind)
  const closed = isCountClosed(detail.count?.statusCode)
  const editable = isCountEditable(detail.count?.statusCode)
  const showBin = new Set(lines.map((l) => l.binId)).size > 1 || lines.some((l) => l.binIsProvisional)
  const showLot = lines.some((l) => l.lotNumber)
  // las columnas no pueden depender de lo que llega DESPUÉS de montar (la vista previa, la primera captura): `FlexRender` vuelve a
  // montar las celdas al cambiar las columnas y el campo que se está tecleando perdería el foco. Con captura en la web, Ajuste y
  // Evidencia van siempre; en solo lectura, Evidencia solo si alguna línea la tiene.
  const showEvidence = canCapture || lines.some((l) => l.capturedAtUtc != null || l.capturedByName != null || l.wasCorrected)
  const showAdjustment = canCapture && !closed && !blind
  const position = useMemo(() => new Map(lines.map((l, i) => [l.id ?? 0, i + 1])), [lines])
  // las columnas no cambian con cada vista previa recalculada (no se pierde el foco del campo): ordenar y exportar el ajuste leen esta ref
  const previewRef = useRef(preview)
  useEffect(() => {
    previewRef.current = preview
  }, [preview])
  const ctx = useMemo<GridContextValue>(
    () => ({ drafts, canCapture, position, openSerials: onSerials, preview, pin, confirmBin, confirmingBin }),
    [drafts, canCapture, position, onSerials, preview, pin, confirmBin, confirmingBin],
  )

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
        ? [
            {
              id: 'bin',
              header: t('warehouse.cycleCounts.detail.bin'),
              cell: (l: CycleCountLineDto) => <BinCell line={l} />,
              sortValue: (l: CycleCountLineDto) => l.binCode,
              exportValue: (l: CycleCountLineDto) => l.binCode ?? '',
            },
          ]
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
      ...(showAdjustment
        ? [
            {
              id: 'adjustment',
              header: t('warehouse.cycleCounts.detail.adjustment'),
              align: 'end' as const,
              cell: (l: CycleCountLineDto) => <AdjustmentCell line={l} />,
              sortValue: (l: CycleCountLineDto) => previewRef.current?.get(l.id ?? 0)?.adjustmentQty ?? undefined,
              exportValue: (l: CycleCountLineDto) => previewRef.current?.get(l.id ?? 0)?.adjustmentQty ?? null,
            },
          ]
        : []),
      ...(showEvidence
        ? [
            {
              id: 'evidence',
              header: t('warehouse.cycleCounts.evidence.column'),
              cell: (l: CycleCountLineDto) => <EvidenceCell line={l} />,
              sortValue: (l: CycleCountLineDto) => (l.wasCorrected ? `1 ${l.correctedAtUtc ?? ''}` : `0 ${l.capturedAtUtc ?? ''}`),
              exportValue: (l: CycleCountLineDto) =>
                evidenceText(lineEvidence(l), t, (n) => formatNumber(n, lang) || '0', (iso) => formatDateTime(iso, lang)),
            },
          ]
        : []),
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
    [t, lang, showBin, showLot, blind, editable, closed, showAdjustment, showEvidence],
  )

  return (
    <div ref={boxRef} className="rcp-lines cc-lines">
      <GridContext.Provider value={ctx}>
        <DataTable
          label={t('warehouse.cycleCounts.detail.lines')}
          columns={columns}
          rows={rows}
          rowKey={(l) => l.id ?? 0}
          rowClassName={(l) =>
            [l.id === hit ? 'cc-hit' : '', drafts.saving.has(l.id ?? 0) ? 'rcp-saving' : '', preview?.get(l.id ?? 0)?.error ? 'cc-row-error' : '']
              .filter(Boolean)
              .join(' ') || undefined
          }
          pagination={false}
          forceCards={width > 0 && width < CARDS_BELOW_PX}
          exportFileName={detail.count?.number ?? undefined}
          empty={<EmptyState title={rows.length === 0 && lines.length > 0 ? t('warehouse.cycleCounts.detail.failingNone') : t('warehouse.cycleCounts.detail.noLines')} />}
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
  const canCount = useCan('warehouse.count')
  const canManage = useCan('warehouse.manage')
  const action = useCycleCountAction()
  const confirmProvisional = useConfirmProvisionalBin()
  const drafts = useCountDrafts(detail)
  const count: CycleCountDto = detail.count ?? {}
  const id = count.id ?? 0
  const lines = useMemo(() => detail.lines ?? [], [detail.lines])
  const blind = Boolean(detail.isBlind)
  const editable = isCountEditable(count.statusCode)
  const closed = isCountClosed(count.statusCode)
  // la web cuenta en modo informado: capturar exige warehouse.count y un conteo abierto (a ciegas, solo en la app)
  const canCapture = canCount && editable && !blind
  // Lote F12: la vista previa (existencia actual, ajuste, errores) solo con warehouse.count y el conteo abierto
  const previewQuery = useReconcilePreview(id, { enabled: canCapture })
  const preview = useMemo(() => (canCapture && previewQuery.data ? previewByLine(previewQuery.data) : null), [canCapture, previewQuery.data])
  // un conteo Contado (lo terminó el operario) se abre para REVISAR: solo las líneas que fallan
  const reviewing = canCapture && count.statusCode === 'COUNTED'
  const [showAll, setShowAll] = useState(() => !reviewing)
  const [pinned, setPinned] = useState<ReadonlySet<number>>(() => new Set())
  const pin = useCallback((lineId: number) => setPinned((prev) => (prev.has(lineId) ? prev : new Set(prev).add(lineId))), [])
  const [qtyFor, setQtyFor] = useState<{ line: CycleCountLineDto; serial?: string | null } | null>(null)
  const [hit, setHit] = useState<number | null>(null)
  // "Agregar lo encontrado": abierto a mano (true) o con el producto que se escaneó (Lote 24)
  const [adding, setAdding] = useState<boolean | AddFoundProduct>(false)
  const [history, setHistory] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [refreshing, setRefreshing] = useState(false)
  const [confirmingBin, setConfirmingBin] = useState<number | null>(null)
  const anyStale = lines.some((l) => l.isStale)
  const blocker = confirmBlocker({ statusCode: count.statusCode, isBlind: blind, lines, drafts: drafts.drafts })
  const filtering = canCapture && !showAll
  const rows = useMemo(
    () => (filtering ? failingLines(lines, preview, (l) => lineVariance(l, drafts.drafts.get(l.id ?? 0)), pinned) : lines),
    [filtering, lines, preview, drafts.drafts, pinned],
  )

  const onPick = (m: CountLineMatch) => {
    setHit(m.line.id ?? null)
    // el escáner lleva a la línea aunque no esté entre "las que fallan"
    if (m.line.id != null) pin(m.line.id)
    setQtyFor({ line: m.line, serial: m.by === 'serial' ? m.serial : null })
  }
  /** Lote 24: código que no es de ninguna línea del conteo → si es un producto, se abre "Agregar lo encontrado" con él puesto. */
  const addScanned = useCallback(async (code: string): Promise<boolean> => {
    try {
      const found = await fetchProductByCode(code)
      const p = found.product
      if (!p?.publicId) return false
      setAdding({ publicId: p.publicId, sku: p.sku ?? '', trackingTypeCode: p.trackingTypeCode ?? '' })
      return true
    } catch {
      return false
    }
  }, [])
  const openSerials = useCallback((line: CycleCountLineDto) => setQtyFor({ line }), [])

  const warehousePublicId = count.warehousePublicId ?? null
  const confirmBin = useCallback(
    async (line: CycleCountLineDto) => {
      if (!warehousePublicId || line.binId == null) return
      setConfirmingBin(line.binId)
      try {
        await confirmProvisional.mutateAsync({ publicId: warehousePublicId, binId: line.binId })
        toast.success(t('warehouse.cycleCounts.provisional.confirmed', { code: line.binCode ?? '' }))
      } catch (err) {
        toast.error(problemText(err))
      } finally {
        setConfirmingBin(null)
      }
    },
    [warehousePublicId, confirmProvisional, t],
  )
  const confirmBinFn = useMemo(() => (canManage && warehousePublicId ? (l: CycleCountLineDto) => void confirmBin(l) : null), [canManage, warehousePublicId, confirmBin])

  const startConfirm = async () => {
    // lo tecleado se guarda antes de la vista previa (que se recalcula con lo guardado)
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
            (count.correctedLines ?? 0) > 0 ? t('warehouse.cycleCounts.detail.correctedLines', { n: count.correctedLines ?? 0 }) : null,
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
      {reviewing && <p className="note cc-note">{t('warehouse.cycleCounts.detail.reviewNote')}</p>}
      {canCapture && anyStale && <p className="note cc-note">{t('warehouse.cycleCounts.detail.staleNote')}</p>}
      {canCapture && (
        <div className="cc-scanrow">
          <CountScanBox lines={lines} onPick={onPick} onUnknown={addScanned} />
        </div>
      )}
      {canCapture && lines.length > 0 && (
        <div className="cc-failbar">
          <label className="sw">
            <input type="checkbox" role="switch" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('warehouse.cycleCounts.detail.showAll')}</span>
          </label>
          <span className="cc-head-meta" aria-live="polite">
            {filtering
              ? t('warehouse.cycleCounts.detail.failingCount', { shown: rows.length, total: lines.length })
              : t('warehouse.cycleCounts.detail.allCount', { total: lines.length })}
            {previewQuery.isFetching && ` · ${t('warehouse.cycleCounts.detail.previewUpdating')}`}
          </span>
        </div>
      )}

      <CountLinesGrid
        detail={detail}
        rows={rows}
        drafts={drafts}
        canCapture={canCapture}
        hit={hit}
        onSerials={openSerials}
        preview={preview}
        pin={pin}
        confirmBin={confirmBinFn}
        confirmingBin={confirmingBin}
      />

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
          onSave={(body) => {
            pin(qtyFor.line.id ?? 0)
            return drafts.capture(qtyFor.line.id ?? 0, body)
          }}
          onClose={() => setQtyFor(null)}
        />
      )}
      {adding && <AddFoundLineModal detail={detail} initialProduct={typeof adding === 'object' ? adding : null} onClose={() => setAdding(false)} />}
      <Modal open={history} title={t('warehouse.cycleCounts.detail.historyTitle', { number: count.number ?? '' })} onClose={() => setHistory(false)}>
        <StatusHistory entityType={COUNT_ENTITY_TYPE} entityId={id} domain={COUNT_STATUS_DOMAIN} />
      </Modal>
      {confirming && <ReconcilePreviewModal count={{ id, number: count.number }} onClose={() => setConfirming(false)} />}
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
