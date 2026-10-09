// Etiquetas de producto (2026-10-09) en Productos e inventario: botón junto a «Códigos de barras» y su ventana (tamaño 4 × 2, 4 × 4 o 4 × 6, orientación
// y «Generar PDF»). Mismo diseño que las etiquetas de posición (`BinLabelsPanel`); lógica en `productLabels.ts`. El tamaño y la orientación se recuerdan
// en este navegador (`teikem.productLabels.size` / `.orientation`). Imprime los productos del filtro actual de la tabla.
import { useContext, useId, useState } from 'react'
import { SessionContext } from '../../app/session'
import { Can } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { BIN_LABEL_ORIENTATIONS, BIN_LABEL_SIZES, BIN_LABEL_SIZE_KEYS, downloadBinLabelsPdf, type BinLabelOrientation, type BinLabelSize } from '../../kernel/ui/binLabelPdf'
import { IconTag } from '../../kernel/ui/screenIcons'
import { exportProducts } from './api'
import { parseLabelOrientation, parseLabelSize, printedLabelsSummary } from './binLabels'
import { problemText } from './problemText'
import { productListQuery, type ProductFilterState } from './productFilters'
import { PRODUCT_LABELS_PERMISSION, printProductLabels } from './productLabels'

const S = 'warehouse.productLabels'
const B = 'warehouse.binLabels' // tamaños, orientación y resultados son los mismos textos
const SIZE_KEY = 'teikem.productLabels.size'
const ORIENTATION_KEY = 'teikem.productLabels.orientation'

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

function LabelShape({ size }: { size: BinLabelSize }) {
  const s = BIN_LABEL_SIZES[size]
  return <span className="bl-shape" aria-hidden="true" style={{ aspectRatio: `${s.widthIn} / ${s.heightIn}` }} />
}

/** «Etiquetas de producto» (cabecera de Productos e inventario). */
export function ProductLabelsButton({ filters }: { filters: ProductFilterState }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  return (
    <Can perm={PRODUCT_LABELS_PERMISSION}>
      <button type="button" className="btn sm" title={t(`${S}.hint`)} onClick={() => setOpen(true)}>
        <IconTag />
        {t(`${S}.button`)}
      </button>
      {open && <ProductLabelsModal filters={filters} onClose={() => setOpen(false)} />}
    </Can>
  )
}

function ProductLabelsModal({ filters, onClose }: { filters: ProductFilterState; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const me = useContext(SessionContext)?.me
  const sizeName = useId()
  const orientationName = useId()
  const [size, setSizeState] = useState<BinLabelSize>(() => parseLabelSize(readPref(SIZE_KEY)))
  const [orientation, setOrientationState] = useState<BinLabelOrientation>(() => parseLabelOrientation(readPref(ORIENTATION_KEY)))
  const [running, setRunning] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [warnings, setWarnings] = useState<{ summary: string; notices: string[] } | null>(null)

  const setSize = (v: BinLabelSize) => {
    setSizeState(v)
    writePref(SIZE_KEY, v)
  }
  const setOrientation = (v: BinLabelOrientation) => {
    setOrientationState(v)
    writePref(ORIENTATION_KEY, v)
  }

  const generate = async () => {
    setNotice(null)
    setWarnings(null)
    setRunning(true)
    try {
      const result = await printProductLabels(
        { fetchProducts: () => exportProducts(productListQuery(filters)), download: (spec) => downloadBinLabelsPdf(spec) },
        { spec: { title: t(`${S}.pdfTitle`), company: me?.tenantName, locale: lang, size, orientation }, t, lang },
      )
      if (result.status === 'tooMany') setNotice(t(`${S}.tooMany`, { count: String(result.total), max: String(result.max) }))
      else if (result.status === 'nothing') setNotice(t(`${S}.nothing`))
      else if (result.status === 'printed') {
        const summary = printedLabelsSummary(result, size, t)
        if (result.notices.length > 0) setWarnings({ summary, notices: result.notices })
        else {
          toast.success(summary)
          onClose()
        }
      }
    } catch (err) {
      setNotice(t(`${B}.result.error`, { error: problemText(err) }))
    } finally {
      setRunning(false)
    }
  }

  return (
    <Modal
      open
      title={t(`${S}.title`)}
      onClose={onClose}
      dismissible={!running}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={running}>
            {warnings ? t(`${B}.close`) : t(`${B}.cancel`)}
          </button>
          <button type="button" className="btn flow" onClick={generate} disabled={running} aria-busy={running || undefined}>
            <IconTag />
            {running ? t(`${B}.generating`) : t(`${B}.generate`)}
          </button>
        </>
      }
    >
      <div className="bs-modal bl-modal">
        <p className="note">{t(`${S}.intro`)}</p>
        <fieldset className="bs-scope bl-sizes">
          <legend>{t(`${B}.size.legend`)}</legend>
          {BIN_LABEL_SIZE_KEYS.map((k) => (
            <label key={k} className="bs-opt bl-size">
              <input type="radio" name={sizeName} value={k} checked={size === k} disabled={running} onChange={() => setSize(k)} />
              <LabelShape size={k} />
              <span>
                <span className="bl-size-name">{t(`${B}.sizes.${k}.label`)}</span>
                <span className="bs-hint bl-size-hint">{t(`${B}.sizes.${k}.hint`)}</span>
              </span>
            </label>
          ))}
        </fieldset>
        <fieldset className="bs-scope">
          <legend>{t(`${B}.orientation.legend`)}</legend>
          {BIN_LABEL_ORIENTATIONS.map((o) => (
            <label key={o} className="bs-opt">
              <input type="radio" name={orientationName} value={o} checked={orientation === o} disabled={running} onChange={() => setOrientation(o)} />
              <span>{t(`${B}.orientation.${o}`)}</span>
            </label>
          ))}
          <p className="bs-hint">{t(`${B}.orientation.hint`)}</p>
        </fieldset>
        {running && (
          <p className="note" role="status">
            {t(`${S}.working`)}
          </p>
        )}
        {notice && (
          <p className="note ferr" role="alert">
            {notice}
          </p>
        )}
        {warnings && (
          <div role="status">
            <p className="note">{warnings.summary}</p>
            <ul>
              {warnings.notices.map((n) => (
                <li key={n}>{n}</li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </Modal>
  )
}
