// Lote F15 — hojas de posición en Ubicaciones (servidor: Lote 23). Piezas de la pantalla:
// - `BinSheetCell`: insignia del estado de la hoja (texto y color, nunca solo color: Sin hoja impresa / Desactualizada /
//   Al día; "—" sin productos) y, debajo, la fecha y hora de la última impresión (visible también en la tarjeta del
//   celular); el `title` agrega el último cambio de la lista de productos.
// - `StaleSheetsBar`: aviso acumulado "N posiciones con la hoja desactualizada o sin imprimir" (`staleCount` del servidor,
//   con los filtros de la tabla salvo "Hoja") y "Imprimir las desactualizadas", que abre el modal con ese alcance. Sin
//   ventanas emergentes por cada movimiento (decisión del dueño).
// - `BinSheetsModal`: qué imprimir (filtro actual, marcadas o solo las desactualizadas), "Incluir posiciones vacías" y
//   "Generar PDF" con el flujo de `printBinSheets` (lee por tandas, descarga, marca impresas solo si salió bien, refresca
//   la lista). Mientras lee se puede cancelar (no marca nada); mientras genera y marca, no.
// Todo con `inventory.view` (el permiso de la pantalla y del servidor para leer y marcar las hojas).
//   <BinSheetCell bin={b} />
//   <StaleSheetsBar count={staleCount} filtered={filtered} onPrint={() => setSheets('stale')} />
//   {sheets && <BinSheetsModal open initialScope={sheets} … onClose={() => setSheets(null)} />}
import { useQueryClient } from '@tanstack/react-query'
import { useContext, useId, useRef, useState } from 'react'
import { SessionContext } from '../../app/session'
import { Can } from '../../kernel/access'
import { parseApiDate } from '../../kernel/api/dates'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, Modal, toast } from '../../kernel/ui'
import { downloadBinSheetsPdf } from '../../kernel/ui/binSheetPdf'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { fetchBinSheets, markBinSheetsPrinted, warehouseKeys, warehouseLabel, type WarehouseBinDto } from './api'
import {
  BIN_SHEETS_MAX_BINS,
  BIN_SHEETS_PERMISSION,
  SHEET_STATUS_TONE,
  binSheetsSources,
  printBinSheets,
  printedSummary,
  sheetStatusOf,
  type BinSheetsProgress,
  type BinSheetsScope,
} from './binSheets'
import type { BinListQuery } from './locations'
import { problemText } from './problemText'

const S = 'warehouse.binSheets'

/** Celda "Hoja": insignia + "Impresa <fecha y hora>" (o nada si nunca se imprimió). */
export function BinSheetCell({ bin }: { bin: Pick<WarehouseBinDto, 'sheetStatus' | 'sheetPrintedAtUtc' | 'sheetContentChangedAtUtc'> }) {
  const t = useT()
  const f = useFormat()
  const status = sheetStatusOf(bin)
  const printed = bin.sheetPrintedAtUtc ? f.dateTime(parseApiDate(bin.sheetPrintedAtUtc)) : null
  const changed = bin.sheetContentChangedAtUtc ? f.dateTime(parseApiDate(bin.sheetContentChangedAtUtc)) : null
  const title = [
    printed ? t(`${S}.lastPrinted`, { date: printed }) : t(`${S}.neverPrinted`),
    changed ? t(`${S}.lastChanged`, { date: changed }) : null,
  ]
    .filter(Boolean)
    .join(' · ')
  if (!status) return <span className="loc-none">—</span>
  return (
    <span className="loc-sheet" title={title}>
      {status === 'EMPTY' ? (
        <span className="loc-none" aria-label={t(`${S}.status.EMPTY_ARIA`)}>
          —
        </span>
      ) : (
        <Chip tone={SHEET_STATUS_TONE[status]}>{t(`${S}.status.${status}`)}</Chip>
      )}
      {printed && <span className="loc-sheet-when">{t(`${S}.printedShort`, { date: printed })}</span>}
    </span>
  )
}

/** Aviso acumulado de hojas por imprimir con su botón. `count` null = cargando (no se pinta). */
export function StaleSheetsBar({ count, filtered, onPrint }: { count: number | null; filtered: boolean; onPrint: () => void }) {
  const t = useT()
  const f = useFormat()
  if (count === null) return null
  if (count === 0) {
    return (
      <p className="loc-stale ok" role="status">
        {t(`${S}.allCurrent`)}
      </p>
    )
  }
  return (
    <div className="loc-stale" role="status">
      <span className="loc-stale-text">
        <IconDoc />
        <span>
          <strong className="mono">{f.number(count)}</strong> {count === 1 ? t(`${S}.staleOne`) : t(`${S}.stale`)}
          {filtered && <span className="loc-stale-scope"> {t(`${S}.staleFiltered`)}</span>}
        </span>
      </span>
      <Can perm={BIN_SHEETS_PERMISSION}>
        <button type="button" className="btn sm flow" onClick={onPrint}>
          {t(`${S}.printStale`)}
        </button>
      </Can>
    </div>
  )
}

export interface BinSheetsModalProps {
  open: boolean
  onClose: () => void
  /** Al terminar bien (las hojas se marcaron); `scope` = lo que se imprimió. */
  onPrinted?: (scope: BinSheetsScope) => void
  warehousePublicId: string
  warehouse: { code?: string | null; name?: string | null }
  /** Consulta de la tabla sin `skip`/`take` (null = filtros imposibles). */
  query: BinListQuery | null
  initialScope: BinSheetsScope
  /** Cuántas posiciones tiene cada alcance (null = todavía no se sabe). */
  counts: { filter: number | null; selected: number; stale: number | null }
  selectedIds: readonly number[]
}

/** "Hojas de posición": elegir qué imprimir y generar el PDF (ver el encabezado del archivo). */
export function BinSheetsModal({ open, onClose, onPrinted, warehousePublicId, warehouse, query, initialScope, counts, selectedIds }: BinSheetsModalProps) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const qc = useQueryClient()
  const me = useContext(SessionContext)?.me
  const groupId = useId()
  const emptyId = useId()
  const [scope, setScope] = useState<BinSheetsScope>(initialScope)
  const [includeEmpty, setIncludeEmpty] = useState(false)
  const [progress, setProgress] = useState<BinSheetsProgress | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const controller = useRef<AbortController | null>(null)
  const running = progress !== null

  const count = scope === 'filter' ? counts.filter : scope === 'selected' ? counts.selected : counts.stale
  const tooMany = count !== null && count > BIN_SHEETS_MAX_BINS
  const none = count === 0
  const fmt = (n: number) => f.number(n)

  const option = (value: BinSheetsScope, n: number | null, disabled: boolean) => (
    <label className={disabled ? 'bs-opt dis' : 'bs-opt'}>
      <input type="radio" name={groupId} value={value} checked={scope === value} disabled={disabled || running} onChange={() => setScope(value)} />
      <span>{t(`${S}.scope.${value}`, { count: n === null ? '…' : fmt(n) })}</span>
    </label>
  )

  const generate = async () => {
    setNotice(null)
    const ctrl = new AbortController()
    controller.current = ctrl
    setProgress({ phase: 'read', done: 0, total: count ?? 0 })
    try {
      const result = await printBinSheets(
        {
          fetchPage: (q) => fetchBinSheets(warehousePublicId, q, ctrl.signal),
          download: (spec) => downloadBinSheetsPdf(spec),
          markPrinted: (binIds, generatedAtUtc) => markBinSheetsPrinted(warehousePublicId, { binIds, generatedAtUtc }),
          signal: ctrl.signal,
          onProgress: setProgress,
        },
        {
          sources: binSheetsSources(scope, query, selectedIds),
          includeEmpty,
          spec: { title: t(`${S}.pdfTitle`), company: me?.tenantName, warehouse: warehouseLabel(warehouse), locale: lang },
          t,
          lang,
        },
      )
      switch (result.status) {
        case 'tooMany':
          setNotice(t(`${S}.tooMany`, { count: fmt(result.total), max: fmt(result.max) }))
          break
        case 'nothing':
          setNotice(t(`${S}.nothing`))
          break
        case 'cancelled':
          toast.info(t(`${S}.result.cancelled`))
          onClose()
          break
        case 'printed':
          await qc.invalidateQueries({ queryKey: warehouseKeys.bins })
          toast.success(printedSummary(result, t, fmt))
          onPrinted?.(scope)
          onClose()
          break
        case 'markFailed':
          await qc.invalidateQueries({ queryKey: warehouseKeys.bins })
          setNotice(t(`${S}.result.markFailed`, { error: problemText(result.error) }))
          break
      }
    } catch (err) {
      setNotice(t(`${S}.result.error`, { error: problemText(err) }))
    } finally {
      controller.current = null
      setProgress(null)
    }
  }

  const cancel = () => {
    if (running) controller.current?.abort()
    else onClose()
  }

  let progressText: string | null = null
  if (progress?.phase === 'read') progressText = t(`${S}.progress.read`, { done: fmt(progress.done), total: fmt(progress.total) })
  else if (progress?.phase === 'render') progressText = t(`${S}.progress.render`, { sheets: fmt(progress.sheets) })
  else if (progress?.phase === 'mark') progressText = t(`${S}.progress.mark`, { bins: fmt(progress.bins) })

  return (
    <Modal
      open={open}
      title={t(`${S}.title`)}
      onClose={onClose}
      dismissible={!running}
      footer={
        <>
          <button type="button" className="btn" onClick={cancel} disabled={running && progress?.phase !== 'read'}>
            {t(`${S}.cancel`)}
          </button>
          <button type="button" className="btn flow" onClick={generate} disabled={running || tooMany || none || count === null} aria-busy={running || undefined}>
            <IconDoc />
            {running ? t(`${S}.generating`) : t(`${S}.generate`)}
          </button>
        </>
      }
    >
      <div className="bs-modal">
        <p className="note">{t(`${S}.intro`)}</p>
        <fieldset className="bs-scope">
          <legend>{t(`${S}.scope.legend`)}</legend>
          {option('filter', counts.filter, query === null)}
          {option('selected', counts.selected, counts.selected === 0)}
          {counts.selected === 0 && <p className="bs-hint">{t(`${S}.scope.selectedNone`)}</p>}
          {option('stale', counts.stale, query === null)}
        </fieldset>
        <label className="sw bs-empty" htmlFor={emptyId}>
          <input id={emptyId} type="checkbox" role="switch" checked={includeEmpty} disabled={running} onChange={(e) => setIncludeEmpty(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t(`${S}.includeEmpty`)}</span>
        </label>
        <p className="bs-hint">{t(`${S}.includeEmptyHint`)}</p>
        {tooMany && (
          <p className="note ferr" role="alert">
            {t(`${S}.tooMany`, { count: fmt(count ?? 0), max: fmt(BIN_SHEETS_MAX_BINS) })}
          </p>
        )}
        {none && !tooMany && <p className="bs-hint">{t(`${S}.noneInScope`)}</p>}
        {progressText && (
          <p className="bs-progress" role="status" aria-live="polite">
            {progressText}
          </p>
        )}
        {notice && (
          <p className="note ferr" role="alert">
            {notice}
          </p>
        )}
      </div>
    </Modal>
  )
}
