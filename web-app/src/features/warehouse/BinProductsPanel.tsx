// Informe "Productos por posición" en Ubicaciones (servidor: `GET .../bin-products`). `BinProductsModal`: qué imprimir (filtro
// actual o marcadas), "Incluir posiciones vacías" y "Generar PDF" con el flujo de `printBinProducts` (lee por tandas y
// descarga: una posición por página con el código de barras de cada producto, para escanear desde el papel en un rack alto).
// Mientras lee se puede cancelar; mientras genera, no. No guarda ningún estado. Todo con `inventory.view`.
//   {products && <BinProductsModal open initialScope={products} … onClose={() => setProducts(null)} />}
import { useContext, useId, useRef, useState } from 'react'
import { SessionContext } from '../../app/session'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, toast, useAppliedFilters } from '../../kernel/ui'
import { downloadBarcodeReportPdf } from '../../kernel/ui/barcodeReportPdf'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { fetchBinProducts, warehouseLabel } from './api'
import {
  BIN_PRODUCTS_NOTICE,
  binProductsSources,
  printBinProducts,
  printedSummary,
  type BinProductsProgress,
  type BinProductsScope,
} from './binProducts'
import type { BinListQuery } from './locations'
import { problemText } from './problemText'

const S = 'warehouse.binProducts'

export interface BinProductsModalProps {
  open: boolean
  onClose: () => void
  warehousePublicId: string
  warehouse: { code?: string | null; name?: string | null }
  /** Consulta de la tabla sin `skip`/`take` (null = filtros imposibles). */
  query: BinListQuery | null
  initialScope: BinProductsScope
  /** Cuántas posiciones tiene cada alcance (null = todavía no se sabe). */
  counts: { filter: number | null; selected: number }
  selectedIds: readonly number[]
}

/** "Productos por posición": elegir qué imprimir y generar el PDF (ver el encabezado del archivo). */
export function BinProductsModal({ open, onClose, warehousePublicId, warehouse, query, initialScope, counts, selectedIds }: BinProductsModalProps) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const me = useContext(SessionContext)?.me
  const appliedFilters = useAppliedFilters()
  const groupId = useId()
  const emptyId = useId()
  const [scope, setScope] = useState<BinProductsScope>(initialScope)
  const [includeEmpty, setIncludeEmpty] = useState(false)
  const [progress, setProgress] = useState<BinProductsProgress | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const controller = useRef<AbortController | null>(null)
  const running = progress !== null

  const count = scope === 'filter' ? counts.filter : counts.selected
  const large = count !== null && count > BIN_PRODUCTS_NOTICE
  const none = count === 0
  const fmt = (n: number) => f.number(n)

  const option = (value: BinProductsScope, n: number | null, disabled: boolean) => (
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
      const result = await printBinProducts(
        {
          fetchPage: (q) => fetchBinProducts(warehousePublicId, q, ctrl.signal),
          download: (spec) => downloadBarcodeReportPdf(spec),
          signal: ctrl.signal,
          onProgress: setProgress,
        },
        {
          sources: binProductsSources(scope, query, selectedIds),
          includeEmpty,
          spec: {
            title: t(`${S}.pdfTitle`),
            subtitle: t(`${S}.pdfSubtitle`),
            company: me?.tenantName,
            user: me?.fullName || me?.email,
            locale: lang,
            // el almacén y la barra de filtros de la pantalla en el momento del clic (mismo texto que la línea de filtros de Exportar)
            filters: [{ label: t(`${S}.warehouseFilter`), value: warehouseLabel(warehouse) }, ...appliedFilters()],
          },
          t,
          lang,
        },
      )
      switch (result.status) {
        case 'nothing':
          setNotice(t(`${S}.nothing`))
          break
        case 'cancelled':
          toast.info(t(`${S}.result.cancelled`))
          onClose()
          break
        case 'printed':
          toast.success(printedSummary(result, t, fmt))
          onClose()
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
  else if (progress?.phase === 'render') progressText = t(`${S}.progress.render`, { products: fmt(progress.products) })

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
          <button type="button" className="btn flow" onClick={generate} disabled={running || none || count === null} aria-busy={running || undefined}>
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
        </fieldset>
        <label className="sw bs-empty" htmlFor={emptyId}>
          <input id={emptyId} type="checkbox" role="switch" checked={includeEmpty} disabled={running} onChange={(e) => setIncludeEmpty(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t(`${S}.includeEmpty`)}</span>
        </label>
        <p className="bs-hint">{t(`${S}.includeEmptyHint`)}</p>
        {large && <p className="bs-hint">{t(`${S}.large`, { count: fmt(count ?? 0) })}</p>}
        {none && <p className="bs-hint">{t(`${S}.noneInScope`)}</p>}
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
