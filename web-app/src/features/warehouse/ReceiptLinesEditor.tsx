// Lote 13 — rejilla de líneas del detalle del recibo (maqueta `recibo()`: SKU · Producto · Esperado · Recibido · Diferencia).
// - Con documento (aviso de llegada u orden de compra): lo esperado es de solo lectura y solo se captura lo recibido; no hay
//   "Añadir ítem" (decisión 6: líneas extra solo por la app o el API).
// - Sin documento (ciego o devolución): producto (`ProductPicker`), esperado y recibido editables, papelera y SIEMPRE una
//   fila vacía al final; mientras se teclea lo recibido, si el esperado estaba vacío o en 0, se copia (`receiptLineEdit.ts`).
// - Guardado por fila al salir del campo, con Enter o al elegir producto (si ya hay recibido); la ficha devuelta queda en
//   caché e invalida la lista (`useReceiptLineRows`). Errores del servidor bajo el campo de su fila.
// - Productos LOT/SERIAL: ícono "Lote y series" (`ReceiptLineCaptureModal`); en SERIAL lo recibido son las series.
// - Lote 16, recibo DIRECTO A POSICIÓN: columna "Posición destino" (`BinPicker` controlado sin zonas de recepción ni de
//   cruce, con las sugeridas de `target-suggestions` primero) que se guarda al elegir; debajo la pista "Sugerida: {bin} ·
//   {motivo}" (si la elegida no es esa) y el aviso naranja "Excede el cupo de {bin}: caben {free}" (D4: no bloquea).
// Confirmado o sin warehouse.receive: todo de solo lectura. `DataTable` con `pagination={false}` (las filas llevan lo
// tecleado: paginar lo desmontaría) y `exportable={false}` (rejilla de captura, excepción documentada en KIT.md); tarjetas
// bajo 720 px o si el panel mide menos de 600 px (760 px en un recibo directo).
// Las columnas NO dependen de las filas (DataTable pinta cada celda como un componente con la función de la columna: si
// cambiara en cada tecla, el campo se volvería a montar y perdería el foco): las celdas leen el estado de un contexto.
import { createContext, useContext, useMemo, useRef, useState } from 'react'
import type { components } from '../../kernel/api/schema'
import { useLang, useT } from '../../kernel/i18n'
import { formatQuantity } from '../../kernel/i18n/numberFormat'
import { ConfirmDialog, DataTable, EmptyState, IconTag, IconTrash, toast, useElementWidth, type DataColumn, type RowAction } from '../../kernel/ui'
import { productLabel, useReceiptTargetSuggestions, type ReceiptDetailDto } from './api'
import { formatNumber } from './lineRules'
import { BinPicker, ProductPicker } from './pickers'
import { ReceiptLineCaptureModal } from './ReceiptLineCaptureModal'
import { isEmptyRow, parseQtyText, rowNeedsTarget, rowVariance, type LineRow } from './receiptLineEdit'
import { exceedsCapacity, TARGET_EXCLUDED_ZONE_TYPES, topSuggestion } from './receivingMode'
import type { ReceiptLineRowsState } from './useReceiptLineRows'

type LineDto = components['schemas']['ReceiptLineDto']

interface EditorContextValue {
  state: ReceiptLineRowsState
  receipt: ReceiptDetailDto
  /** Número de fila (1…n) para las etiquetas accesibles. */
  position: ReadonlyMap<string, number>
}
const EditorContext = createContext<EditorContextValue | null>(null)

function useEditor(): EditorContextValue {
  const ctx = useContext(EditorContext)
  if (!ctx) throw new Error('ReceiptLinesEditor: celda fuera de la rejilla')
  return ctx
}

/** Cantidad para mostrar (solo lectura). */
function qtyView(text: string, lang: string): string {
  const n = parseQtyText(text)
  return n === null || Number.isNaN(n) ? '—' : formatNumber(n, lang)
}

/** Valor de orden de una cantidad tecleada (vacía o inválida: al final). */
function qtySort(text: string): number | undefined {
  const n = parseQtyText(text)
  return n === null || Number.isNaN(n) ? undefined : n
}

/** Campo de cantidad de una celda: texto libre (coma o punto), guarda al salir o con Enter. */
function QtyCell({ row, field }: { row: LineRow; field: 'expected' | 'received' }) {
  const t = useT()
  const { state, position } = useEditor()
  const error = row.errors[field]
  return (
    <span className="rcp-cell">
      <input
        className="rcp-qty"
        type="text"
        inputMode="decimal"
        autoComplete="off"
        aria-label={t(field === 'expected' ? 'warehouse.receipts.lines.expectedOf' : 'warehouse.receipts.lines.receivedOf', { n: position.get(row.key) ?? 0 })}
        aria-invalid={error ? true : undefined}
        value={row[field]}
        onChange={(e) => state.input(row.key, field, e.target.value)}
        onBlur={() => void state.save(row.key)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            void state.save(row.key)
          }
        }}
      />
      {error && (
        <span className="ferr" role="alert">
          {error}
        </span>
      )}
    </span>
  )
}

/** Producto de la fila: buscador en recibos sin documento; texto en los demás. Debajo, los errores de la fila. */
function ProductCell({ row, editable }: { row: LineRow; editable: boolean }) {
  const t = useT()
  const { state, position } = useEditor()
  return (
    <span className="rcp-cell rcp-prod">
      {editable ? (
        <ProductPicker
          value={row.productPublicId}
          aria-label={t('warehouse.receipts.lines.productOf', { n: position.get(row.key) ?? 0 })}
          invalid={Boolean(row.errors.product)}
          disabled={row.saving}
          onChange={(_publicId, product) => state.pickProduct(row.key, product)}
        />
      ) : (
        <span>{row.productName || row.sku || '—'}</span>
      )}
      {row.errors.product && (
        <span className="ferr" role="alert">
          {row.errors.product}
        </span>
      )}
      {row.errors.row && (
        <span className="ferr" role="alert">
          {row.errors.row}
        </span>
      )}
      {row.saving && (
        <span className="rcp-faint" role="status">
          {t('warehouse.receipts.lines.saving')}
        </span>
      )}
    </span>
  )
}

/** Lote 16: aviso naranja de cupo excedido (D4: se puede confirmar igual). */
function OverCapacity({ row }: { row: LineRow }) {
  const t = useT()
  const lang = useLang()
  const received = parseQtyText(row.received)
  if (!row.targetBinCode || row.targetFreeQty == null || !exceedsCapacity(row.targetFreeQty, received)) return null
  return (
    <span className="chip s-cod rcp-over" role="status">
      {t('warehouse.receipts.lines.overCapacity', { bin: row.targetBinCode, free: formatQuantity(row.targetFreeQty, lang) })}
    </span>
  )
}

/** Lote 16: posición destino de la fila (recibo directo). Editable: selector que guarda al elegir, con la pista de la
 *  sugerida; si no, el código. */
function TargetCell({ row }: { row: LineRow }) {
  const t = useT()
  const { state, position, receipt } = useEditor()
  const header = receipt.header ?? {}
  const editable = state.mode.editable
  // solo se piden sugerencias para las líneas guardadas que reciben algo (las que necesitan destino)
  const need = rowNeedsTarget(row)
  const suggestions = useReceiptTargetSuggestions(header.publicId, row.lineId, 3, { enabled: editable && need, handleAccessDenied: false })
  const top = need ? topSuggestion(suggestions.data) : null
  const suggestedIds = useMemo(() => (suggestions.data ?? []).map((s) => s.binId), [suggestions.data])

  if (!editable) {
    return (
      <span className="rcp-cell">
        <span className={row.targetBinCode ? 'ref' : 'rcp-faint'}>{row.targetBinCode ?? '—'}</span>
        <OverCapacity row={row} />
      </span>
    )
  }
  if (isEmptyRow(row)) return <span className="rcp-faint">—</span>
  const error = row.errors.target
  return (
    <span className="rcp-cell rcp-target">
      <BinPicker
        warehousePublicId={header.warehousePublicId}
        value={row.targetBinId}
        onChange={(_id, bin) => state.pickTarget(row.key, bin ? { id: bin.id, code: bin.code, zoneTypeCode: bin.zoneTypeCode } : null)}
        excludeZoneTypeCodes={TARGET_EXCLUDED_ZONE_TYPES}
        suggestedBinIds={suggestedIds}
        placeholder={t('warehouse.receipts.lines.targetPlaceholder')}
        aria-label={t('warehouse.receipts.lines.targetOf', { n: position.get(row.key) ?? 0 })}
        invalid={Boolean(error)}
        disabled={row.saving}
      />
      {top && top.binId !== row.targetBinId && (
        <span className="rcp-faint rcp-hint">{t('warehouse.receipts.lines.targetSuggested', { bin: top.binCode ?? '', reason: top.reason ?? '' })}</span>
      )}
      <OverCapacity row={row} />
      {error && (
        <span className="ferr" role="alert">
          {error}
        </span>
      )}
    </span>
  )
}

/** Ancho del panel (px) bajo el cual la rejilla pasa a tarjetas; en directo, más (la columna de destino). */
const CARDS_BELOW = 600
const DIRECT_CARDS_BELOW = 760

export interface ReceiptLinesEditorProps {
  receipt: ReceiptDetailDto
  state: ReceiptLineRowsState
}

export function ReceiptLinesEditor({ receipt, state }: ReceiptLinesEditorProps) {
  const t = useT()
  const lang = useLang()
  const { rows, mode } = state
  const { manual, editable } = mode
  const direct = mode.direct === true
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const [capturing, setCapturing] = useState<LineDto | null>(null)
  const [removing, setRemoving] = useState<LineRow | null>(null)

  const position = useMemo(() => new Map(rows.map((r, i) => [r.key, i + 1])), [rows])
  const ctx = useMemo<EditorContextValue>(() => ({ state, receipt, position }), [state, receipt, position])

  // estables mientras se teclea (solo cambian con el idioma o el modo)
  const columns = useMemo<DataColumn<LineRow>[]>(
    () => [
      {
        id: 'sku',
        header: t('warehouse.receipts.lines.sku'),
        cell: (r) => (r.sku ? <span className="ref">{r.sku}</span> : <span className="rcp-faint">{isEmptyRow(r) ? t('warehouse.receipts.lines.newRow') : '—'}</span>),
        sortValue: (r) => r.sku || undefined,
        card: 'title',
      },
      {
        id: 'product',
        header: t('warehouse.receipts.lines.product'),
        cell: (r) => <ProductCell row={r} editable={editable && manual && r.asnLineId === null} />,
        sortValue: (r) => r.productName || undefined,
      },
      {
        id: 'expected',
        header: t('warehouse.receipts.lines.expected'),
        align: 'end',
        cell: (r) => (editable && manual ? <QtyCell row={r} field="expected" /> : <span className="mono">{qtyView(r.expected, lang)}</span>),
        sortValue: (r) => qtySort(r.expected),
      },
      {
        id: 'received',
        header: t('warehouse.receipts.lines.received'),
        align: 'end',
        // SERIAL: lo recibido es el número de series (se capturan con "Lote y series")
        cell: (r) =>
          editable && r.trackingTypeCode !== 'SERIAL' ? <QtyCell row={r} field="received" /> : <span className="mono">{qtyView(r.received, lang)}</span>,
        sortValue: (r) => qtySort(r.received),
      },
      {
        id: 'diff',
        header: t('warehouse.receipts.lines.diff'),
        align: 'end',
        cell: (r) => {
          const d = rowVariance(r, manual)
          if (d === null) return <span className="rcp-faint">—</span>
          return <span className={d === 0 ? 'mono rcp-diff0' : 'mono rcp-diff'}>{`${d > 0 ? '+' : ''}${formatNumber(d, lang)}`}</span>
        },
        sortValue: (r) => rowVariance(r, manual) ?? undefined,
      },
      // Lote 16: solo en recibos directos
      ...(direct
        ? [
            {
              id: 'target',
              header: t('warehouse.receipts.lines.target'),
              cell: (r: LineRow) => <TargetCell row={r} />,
              sortValue: (r: LineRow) => r.targetBinCode || undefined,
            } satisfies DataColumn<LineRow>,
          ]
        : []),
    ],
    [t, lang, editable, manual, direct],
  )

  const rowActions = useMemo<RowAction<LineRow>[]>(
    () => [
      {
        key: 'lotSerial',
        label: t('warehouse.receipts.lines.lotSerial'),
        icon: <IconTag />,
        perm: 'warehouse.receive',
        visible: (r) => editable && r.lineId !== null && (r.trackingTypeCode === 'LOT' || r.trackingTypeCode === 'SERIAL'),
        disabled: (r) => r.saving,
        onClick: (r) => setCapturing((receipt.lines ?? []).find((l) => l.id === r.lineId) ?? null),
      },
      {
        key: 'remove',
        label: t('warehouse.receipts.lines.remove'),
        icon: <IconTrash />,
        tone: 'danger',
        perm: 'warehouse.receive',
        // las líneas del documento no se quitan (se captura 0) y las que tienen cruce de muelle tampoco
        visible: (r) => editable && r.asnLineId === null && r.allocatedToCrossDock <= 0 && !isEmptyRow(r),
        disabled: (r) => r.saving,
        onClick: (r) => {
          if (r.lineId === null) void state.remove(r.key)
          else setRemoving(r)
        },
      },
    ],
    [t, editable, receipt.lines, state],
  )

  return (
    <div ref={boxRef} className={direct ? 'rcp-lines rcp-direct' : 'rcp-lines'}>
      <EditorContext.Provider value={ctx}>
        <DataTable
          label={t('warehouse.receipts.lines.title')}
          columns={columns}
          rows={rows}
          rowKey={(r) => r.key}
          rowActions={rowActions}
          rowClassName={(r) => (r.saving ? 'rcp-saving' : undefined)}
          pagination={false}
          exportable={false}
          // en directo la columna "Posición destino" pide más ancho: tarjetas antes (sin desbordar el panel)
          forceCards={width > 0 && width < (direct ? DIRECT_CARDS_BELOW : CARDS_BELOW)}
          empty={<EmptyState title={t('warehouse.receipts.detail.noLines')} />}
        />
      </EditorContext.Provider>
      {capturing && (
        <ReceiptLineCaptureModal
          receipt={receipt}
          line={capturing}
          onClose={() => setCapturing(null)}
          onSaved={(_dto, line) => {
            if (line) state.applyLine(line)
          }}
        />
      )}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('warehouse.receipts.detail.removeTitle')}
        message={t('warehouse.receipts.detail.removeBody', { product: productLabel({ sku: removing?.sku, name: removing?.productName }) })}
        confirmLabel={t('warehouse.receipts.detail.removeLine')}
        onConfirm={async () => {
          if (!removing) return
          await state.remove(removing.key)
          toast.success(t('warehouse.receipts.detail.lineRemoved'))
        }}
        onClose={() => setRemoving(null)}
      />
    </div>
  )
}
