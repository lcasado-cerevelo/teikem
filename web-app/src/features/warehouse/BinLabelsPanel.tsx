// Lote F16 — etiquetas de posición en Ubicaciones (solo web; lee el listado de posiciones existente). Piezas de la pantalla:
// - `BinLabelsButton`: "Etiquetas de posición" junto a "Códigos de barras" y "Hojas de posición", con el mismo permiso que
//   el reporte de códigos de barras de posiciones (`inventory.view`).
// - `BinLabelsModal`: qué imprimir (el filtro actual de la tabla o las marcadas), tamaño (4 × 2, 4 × 4 o 4 × 6 pulgadas,
//   con su equivalente en cm y su forma), orientación (Automática / Girar 90°) y "Generar PDF" con el flujo de
//   `printBinLabels` (lee por tandas, ordena y descarga). El tamaño y la orientación elegidos se recuerdan en este navegador
//   (`teikem.binLabels.size` / `.orientation`) para reimprimir igual. NO marca nada: las etiquetas no tienen estado.
//   Si alguna posición salió sin código de barras (demasiado larga o con caracteres que Code 128 no admite), el modal se
//   queda abierto con la lista (el PDF ya se descargó).
//   <BinLabelsButton onOpen={() => setLabels(selected.size > 0 ? 'selected' : 'filter')} />
//   {labels && <BinLabelsModal open initialScope={labels} query={query} counts={counts} selectedIds={ids} warehousePublicId={id} warehouse={w} onClose={() => setLabels(null)} />}
import { useContext, useId, useRef, useState } from 'react'
import { SessionContext } from '../../app/session'
import { Can } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { BIN_LABEL_ORIENTATIONS, BIN_LABEL_SIZES, BIN_LABEL_SIZE_KEYS, downloadBinLabelsPdf, type BinLabelOrientation, type BinLabelSize } from '../../kernel/ui/binLabelPdf'
import { IconTag } from '../../kernel/ui/screenIcons'
import { fetchWarehouseBins } from './api'
import {
  BIN_LABELS_MAX,
  BIN_LABELS_PERMISSION,
  binLabelsSources,
  parseLabelOrientation,
  parseLabelSize,
  printBinLabels,
  printedLabelsSummary,
  type BinLabelsProgress,
  type BinLabelsScope,
} from './binLabels'
import type { BinListQuery } from './locations'
import { problemText } from './problemText'

const S = 'warehouse.binLabels'
const SIZE_KEY = 'teikem.binLabels.size'
const ORIENTATION_KEY = 'teikem.binLabels.orientation'

function readPref(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function writePref(key: string, value: string) {
  try {
    localStorage.setItem(key, value)
  } catch {
    // sin almacenamiento (modo privado): se elige de nuevo la próxima vez
  }
}

/** "Etiquetas de posición" (cabecera de la tabla de Ubicaciones). */
export function BinLabelsButton({ onOpen }: { onOpen: () => void }) {
  const t = useT()
  return (
    <Can perm={BIN_LABELS_PERMISSION}>
      <button type="button" className="btn sm" title={t(`${S}.hint`)} onClick={onOpen}>
        <IconTag />
        {t(`${S}.button`)}
      </button>
    </Can>
  )
}

export interface BinLabelsModalProps {
  open: boolean
  onClose: () => void
  warehousePublicId: string
  warehouse: { code?: string | null; name?: string | null }
  /** Consulta de la tabla sin `skip`/`take` (null = filtros imposibles). */
  query: BinListQuery | null
  initialScope: BinLabelsScope
  /** Cuántas posiciones tiene cada alcance (null = todavía no se sabe). */
  counts: { filter: number | null; selected: number }
  selectedIds: readonly number[]
}

/** Silueta de la etiqueta (proporción real) junto a cada tamaño: ayuda a reconocerla sin leer números. */
function LabelShape({ size }: { size: BinLabelSize }) {
  const s = BIN_LABEL_SIZES[size]
  return <span className="bl-shape" aria-hidden="true" style={{ aspectRatio: `${s.widthIn} / ${s.heightIn}` }} />
}

/** "Etiquetas de posición": elegir qué imprimir, tamaño y orientación, y generar el PDF (ver el encabezado del archivo). */
export function BinLabelsModal({ open, onClose, warehousePublicId, warehouse, query, initialScope, counts, selectedIds }: BinLabelsModalProps) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const me = useContext(SessionContext)?.me
  const scopeName = useId()
  const sizeName = useId()
  const orientationName = useId()
  const [scope, setScope] = useState<BinLabelsScope>(initialScope)
  const [size, setSizeState] = useState<BinLabelSize>(() => parseLabelSize(readPref(SIZE_KEY)))
  const [orientation, setOrientationState] = useState<BinLabelOrientation>(() => parseLabelOrientation(readPref(ORIENTATION_KEY)))
  const [progress, setProgress] = useState<BinLabelsProgress | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [warnings, setWarnings] = useState<{ summary: string; notices: string[] } | null>(null)
  const controller = useRef<AbortController | null>(null)
  const running = progress !== null

  const count = scope === 'filter' ? counts.filter : counts.selected
  const tooMany = count !== null && count > BIN_LABELS_MAX
  const none = count === 0
  const fmt = (n: number) => f.number(n)

  const setSize = (v: BinLabelSize) => {
    setSizeState(v)
    writePref(SIZE_KEY, v)
  }
  const setOrientation = (v: BinLabelOrientation) => {
    setOrientationState(v)
    writePref(ORIENTATION_KEY, v)
  }

  const scopeOption = (value: BinLabelsScope, n: number | null, disabled: boolean) => (
    <label className={disabled ? 'bs-opt dis' : 'bs-opt'}>
      <input type="radio" name={scopeName} value={value} checked={scope === value} disabled={disabled || running} onChange={() => setScope(value)} />
      <span>{t(`${S}.scope.${value}`, { count: n === null ? '…' : fmt(n) })}</span>
    </label>
  )

  const generate = async () => {
    setNotice(null)
    setWarnings(null)
    const ctrl = new AbortController()
    controller.current = ctrl
    setProgress({ phase: 'read', done: 0, total: count ?? 0 })
    try {
      const result = await printBinLabels(
        {
          fetchPage: (q) => fetchWarehouseBins(warehousePublicId, q, ctrl.signal),
          download: (spec) => downloadBinLabelsPdf(spec),
          signal: ctrl.signal,
          onProgress: setProgress,
        },
        {
          sources: binLabelsSources(scope, query, selectedIds),
          spec: { title: t(`${S}.pdfTitle`), company: me?.tenantName, locale: lang, size, orientation },
          warehouseCode: warehouse.code,
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
        case 'printed': {
          const summary = printedLabelsSummary(result, size, t, fmt)
          if (result.notices.length > 0) {
            // el PDF ya se descargó; el modal se queda con la lista de las que salieron sin código de barras
            setWarnings({ summary, notices: result.notices })
          } else {
            toast.success(summary)
            onClose()
          }
          break
        }
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
  else if (progress?.phase === 'render') progressText = t(`${S}.progress.render`, { labels: fmt(progress.labels) })

  return (
    <Modal
      open={open}
      title={t(`${S}.title`)}
      onClose={onClose}
      dismissible={!running}
      footer={
        <>
          <button type="button" className="btn" onClick={cancel} disabled={running && progress?.phase !== 'read'}>
            {warnings && !running ? t(`${S}.close`) : t(`${S}.cancel`)}
          </button>
          <button type="button" className="btn flow" onClick={generate} disabled={running || tooMany || none || count === null} aria-busy={running || undefined}>
            <IconTag />
            {running ? t(`${S}.generating`) : t(`${S}.generate`)}
          </button>
        </>
      }
    >
      <div className="bs-modal bl-modal">
        <p className="note">{t(`${S}.intro`)}</p>
        <fieldset className="bs-scope">
          <legend>{t(`${S}.scope.legend`)}</legend>
          {scopeOption('filter', counts.filter, query === null)}
          {scopeOption('selected', counts.selected, counts.selected === 0)}
          {counts.selected === 0 && <p className="bs-hint">{t(`${S}.scope.selectedNone`)}</p>}
        </fieldset>
        <fieldset className="bs-scope bl-sizes">
          <legend>{t(`${S}.size.legend`)}</legend>
          {BIN_LABEL_SIZE_KEYS.map((k) => (
            <label key={k} className="bs-opt bl-size">
              <input type="radio" name={sizeName} value={k} checked={size === k} disabled={running} onChange={() => setSize(k)} />
              <LabelShape size={k} />
              <span>
                <span className="bl-size-name">{t(`${S}.sizes.${k}.label`)}</span>
                <span className="bs-hint bl-size-hint">{t(`${S}.sizes.${k}.hint`)}</span>
              </span>
            </label>
          ))}
        </fieldset>
        <fieldset className="bs-scope">
          <legend>{t(`${S}.orientation.legend`)}</legend>
          {BIN_LABEL_ORIENTATIONS.map((o) => (
            <label key={o} className="bs-opt">
              <input type="radio" name={orientationName} value={o} checked={orientation === o} disabled={running} onChange={() => setOrientation(o)} />
              <span>{t(`${S}.orientation.${o}`)}</span>
            </label>
          ))}
          <p className="bs-hint">{t(`${S}.orientation.hint`)}</p>
        </fieldset>
        {tooMany && (
          <p className="note ferr" role="alert">
            {t(`${S}.tooMany`, { count: fmt(count ?? 0), max: fmt(BIN_LABELS_MAX) })}
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
        {warnings && (
          <div className="note bl-warnings" role="status">
            <p>{warnings.summary}</p>
            {warnings.notices.map((n) => (
              <p key={n}>{n}</p>
            ))}
          </div>
        )}
      </div>
    </Modal>
  )
}
