// Ajustes de inventario (`/warehouse/inventory-adjustments`, maqueta `ajustesAlmacen()`): maestro-detalle de los faltantes
// de compra. Izquierda: órdenes con recibo parcial y faltante pendiente (`GET /purchase-orders/shortages`, chip "N corto" y
// "N línea(s)"); derecha: las líneas en faltante de la orden ELEGIDA (`GET /purchase-orders/{publicId}/shortage-lines`,
// una sola petición, filtradas a pendiente > 0 porque el endpoint trae también las ya resueltas). La elegida va en la URL
// (`?po=<publicId>`; sin ella o si ya no está, la primera), como `?warehouse=` de Ubicaciones.
// Lectura: purchasing.view + PURCHASING (por la ruta). Resolver: inventory.adjust — Cerrar; Reordenar exige además
// purchasing.manage; Ajuste manual exige además el módulo WMS_LOTSERIAL. Cada botón abre `ResolveShortageModal` con su
// acción (confirmación; el ajuste manual pide ahí la posición, el lote o las series) y precarga cantidad y motivo de la fila.
import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ModuleKeys, useCan, useModule } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, IconCart, IconCheck, IconPencil, Panel, Spinner, toast, type DataColumn } from '../../kernel/ui'
import {
  usePurchaseOrderShortageLines,
  usePurchaseOrderShortages,
  type PoShortageSummaryDto,
  type ShortageLineDto,
  type ShortageResolveResultDto,
} from './api'
import { formatNumber } from './lineRules'
import { ResolveShortageModal, type ShortageAction } from './ResolveShortageModal'
import './warehouse.css'

const NO_LINES: ShortageLineDto[] = []
const NO_ORDERS: PoShortageSummaryDto[] = []

type ResolveInitial = { quantity?: number | null; notes?: string }

interface Resolving {
  po: PoShortageSummaryDto
  line: ShortageLineDto
  action: ShortageAction
  initial?: ResolveInitial
}

/** Cantidad capturada en la fila: número finito o null (vacío o inválido; el modal la valida). */
function parseQty(text: string): number | null {
  const s = text.trim().replace(',', '.')
  if (!s) return null
  const n = Number(s)
  return Number.isFinite(n) ? n : null
}

/**
 * Celda Resolver (maqueta: Cerrar · Reordenar · Cant. · Motivo · Ajuste manual). La captura en línea vive aquí (estado
 * local: teclear no vuelve a armar las columnas de la tabla ni saca el foco del campo) y solo precarga el modal.
 */
function ResolveCell({
  line,
  canReorder,
  hasLotSerial,
  onOpen,
}: {
  line: ShortageLineDto
  canReorder: boolean
  hasLotSerial: boolean
  onOpen: (line: ShortageLineDto, action: ShortageAction, initial?: ResolveInitial) => void
}) {
  const t = useT()
  const [qty, setQty] = useState('')
  const [reason, setReason] = useState('')
  const sku = line.sku ?? ''
  return (
    <div className="adj-resolve">
      <button type="button" className="btn sm" onClick={() => onOpen(line, 'CLOSE')}>
        {t('warehouse.inventoryAdjustments.actions.close')}
      </button>
      {canReorder && (
        <button type="button" className="btn sm" onClick={() => onOpen(line, 'REORDER')}>
          <IconCart /> {t('warehouse.inventoryAdjustments.actions.reorder')}
        </button>
      )}
      {hasLotSerial && (
        <>
          <input
            className="adj-qty"
            inputMode="decimal"
            placeholder={t('warehouse.inventoryAdjustments.qtyPlaceholder')}
            aria-label={t('warehouse.inventoryAdjustments.qtyAria', { sku })}
            value={qty}
            onChange={(e) => setQty(e.target.value)}
          />
          <input
            className="adj-reason"
            maxLength={300}
            placeholder={t('warehouse.inventoryAdjustments.reasonPlaceholder')}
            aria-label={t('warehouse.inventoryAdjustments.reasonAria', { sku })}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          <button
            type="button"
            className="btn sm flow"
            onClick={() => onOpen(line, 'MANUAL_ADJUSTMENT', { quantity: parseQty(qty), notes: reason.trim() })}
          >
            {t('warehouse.inventoryAdjustments.actions.manual')}
          </button>
        </>
      )}
    </div>
  )
}

export default function InventoryAdjustmentsScreen() {
  const t = useT()
  const lang = useLang()
  const [params, setParams] = useSearchParams()
  const canAdjust = useCan('inventory.adjust')
  const canReorder = useCan('inventory.adjust', 'purchasing.manage')
  const hasLotSerial = useModule(ModuleKeys.WmsLotSerial)
  const [resolving, setResolving] = useState<Resolving | null>(null)

  const ordersQ = usePurchaseOrderShortages()
  const orders = ordersQ.data ?? NO_ORDERS
  // la elegida: la de la URL si sigue en la lista; si no, la primera (maqueta: pos.find(...) || pos[0] || null)
  const selParam = params.get('po')
  const sel = orders.find((p) => p.publicId === selParam) ?? orders[0] ?? null
  const select = (publicId: string) => {
    const next = new URLSearchParams(params)
    next.set('po', publicId)
    setParams(next, { replace: true })
  }

  const linesQ = usePurchaseOrderShortageLines(sel?.publicId ?? null)
  const lines = useMemo(() => (linesQ.data ?? NO_LINES).filter((l) => (l.qtyPending ?? 0) > 0), [linesQ.data])

  const open = useCallback(
    (line: ShortageLineDto, action: ShortageAction, initial?: ResolveInitial) => {
      if (sel) setResolving({ po: sel, line, action, initial })
    },
    [sel],
  )

  /** Aviso de la maqueta según lo que se resolvió (la acción puede cambiarse dentro del modal). */
  const onResolved = (result: ShortageResolveResultDto) => {
    const sku = result.line.sku ?? ''
    const resolutions = result.line.resolutions ?? []
    const last = resolutions[resolutions.length - 1]
    if (result.reorder) {
      toast.success(t('warehouse.inventoryAdjustments.toast.reordered', { number: result.reorder.number ?? '' }))
    } else if (last?.actionCode === 'MANUAL_ADJUSTMENT') {
      toast.success(t('warehouse.inventoryAdjustments.toast.adjusted', { qty: formatNumber(last.quantity ?? 0, lang), sku }))
    } else {
      toast.success(t('warehouse.inventoryAdjustments.toast.closed', { sku }))
    }
  }

  const columns = useMemo<DataColumn<ShortageLineDto>[]>(() => {
    const cols: DataColumn<ShortageLineDto>[] = [
      {
        id: 'sku',
        header: t('warehouse.purchaseOrders.shortages.columns.sku'),
        cell: (l) => <span className="ref">{l.sku}</span>,
        sortValue: (l) => l.sku,
        card: 'title',
      },
      { id: 'product', header: t('warehouse.purchaseOrders.shortages.columns.product'), cell: (l) => l.productName, sortValue: (l) => l.productName },
      {
        id: 'qtyOrdered',
        header: t('warehouse.purchaseOrders.shortages.columns.qtyOrdered'),
        cell: (l) => <span className="mono">{formatNumber(l.qtyOrdered, lang)}</span>,
        sortValue: (l) => l.qtyOrdered,
        align: 'end',
      },
      {
        id: 'qtyReceived',
        header: t('warehouse.purchaseOrders.shortages.columns.qtyReceived'),
        cell: (l) => <span className="mono">{formatNumber(l.qtyReceived, lang)}</span>,
        sortValue: (l) => l.qtyReceived,
        align: 'end',
      },
      {
        id: 'qtyPending',
        header: t('warehouse.inventoryAdjustments.columns.shortage'),
        cell: (l) => <span className="mono adj-short">{formatNumber(l.qtyPending, lang)}</span>,
        sortValue: (l) => l.qtyPending,
        align: 'end',
      },
    ]
    // Resolver: controles, no dato (sin orden). Sin inventory.adjust no hay nada que resolver: la columna no se pinta.
    if (canAdjust) {
      cols.push({
        id: 'resolve',
        header: t('warehouse.inventoryAdjustments.columns.resolve'),
        // la clave incluye el pendiente: tras un ajuste parcial la captura de la fila vuelve a quedar vacía
        cell: (l) => (
          <ResolveCell
            key={`${l.purchaseOrderLineId}:${l.qtyPending}`}
            line={l}
            canReorder={canReorder}
            hasLotSerial={hasLotSerial}
            onOpen={open}
          />
        ),
      })
    }
    return cols
  }, [t, lang, canAdjust, canReorder, hasLotSerial, open])

  let list
  if (ordersQ.isLoading) list = <Spinner block />
  else if (ordersQ.error)
    list = (
      <p className="pb ferr" role="alert">
        {ordersQ.error.message || t('errors.generic')}
      </p>
    )
  else if (orders.length === 0)
    list = (
      <div className="empty adj-empty sm">
        <div>
          <IconCheck />
          <p>{t('warehouse.inventoryAdjustments.emptyList')}</p>
        </div>
      </div>
    )
  else
    list = orders.map((po) => {
      const on = sel?.publicId === po.publicId
      return (
        <button
          key={po.publicId}
          type="button"
          className={on ? 'unrow on' : 'unrow'}
          aria-current={on ? 'true' : undefined}
          onClick={() => select(po.publicId ?? '')}
        >
          <div className="adj-po">
            <div className="adj-po-top">
              <span className="ref">{po.number}</span>
              <Chip tone="fail">{t('warehouse.inventoryAdjustments.short', { qty: formatNumber(po.qtyPending, lang) })}</Chip>
            </div>
            <div className="adj-supplier">{po.supplierName}</div>
            <div className="meta">{t('warehouse.inventoryAdjustments.lines', { count: po.linesWithShortage ?? 0 })}</div>
          </div>
        </button>
      )
    })

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('nav.inventoryAdjustments.title')}</h1>
          <p>{t('nav.inventoryAdjustments.subtitle')}</p>
        </div>
      </div>
      <div className="note adj-intro">
        <IconPencil />
        <span>{t('warehouse.inventoryAdjustments.intro')}</span>
      </div>

      <div className="adj-cols">
        <Panel
          flush
          icon={<IconCart />}
          title={t('warehouse.inventoryAdjustments.listTitle')}
          badge={ordersQ.isLoading || ordersQ.error ? undefined : orders.length}
        >
          <div className="adj-list" role="group" aria-label={t('warehouse.inventoryAdjustments.listTitle')}>
            {list}
          </div>
        </Panel>

        {sel ? (
          <Panel
            flush
            icon={<IconCart />}
            title={
              <>
                <span className="ref">{sel.number}</span> · {sel.supplierName}
              </>
            }
          >
            {linesQ.error ? (
              <p className="pb ferr" role="alert">
                {linesQ.error.message || t('errors.generic')}
              </p>
            ) : (
              <div className="adj-lines">
                <DataTable
                  label={t('warehouse.inventoryAdjustments.tableLabel')}
                  columns={columns}
                  rows={lines}
                  rowKey={(l) => l.purchaseOrderLineId ?? 0}
                  defaultSort={{ id: 'sku', desc: false }}
                  loading={linesQ.isLoading}
                />
              </div>
            )}
            <div className="note adj-note">{t('warehouse.inventoryAdjustments.reorderNote')}</div>
          </Panel>
        ) : (
          <Panel flush>
            <div className="empty adj-empty lg">
              <div>
                <IconPencil />
                <p>{t('warehouse.inventoryAdjustments.selectPrompt')}</p>
              </div>
            </div>
          </Panel>
        )}
      </div>

      {resolving && (
        <ResolveShortageModal
          open
          onClose={() => setResolving(null)}
          po={resolving.po}
          line={resolving.line}
          initialAction={resolving.action}
          initial={resolving.initial}
          onResolved={onResolved}
        />
      )}
    </div>
  )
}
