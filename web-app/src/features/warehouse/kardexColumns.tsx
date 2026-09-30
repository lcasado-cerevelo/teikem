// Lote 14 — columnas de un Kárdex (`DataColumn<KardexRowDto>`) compartidas por 'Kárdex de movimientos', 'Transferencias y
// ajustes', la genealogía de lote, el rastro de serie y los descuadres. Cada pantalla elige cuáles y en qué orden:
//   const columns = useKardexColumns(['date', 'time', 'type', 'sku', 'product', 'qty', 'position', 'lotSerial'])
// Fecha y hora van por separado (maqueta `ledger()`), el tipo como chip de color (`txnTypeTone`), la cantidad con signo y
// color (entra = flujo, sale = peligro). Todas llevan `sortValue` (orden en el cliente sobre la página visible) y se exportan
// como texto (la cantidad como número con signo).
import { useMemo } from 'react'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, type DataColumn } from '../../kernel/ui'
import type { KardexRowDto } from './api'
import { formatNumber } from './lineRules'
import { fromToText, lotSerialText, movementOrigin, movementQtyView, sideText, splitDateTime, txnTypeTone } from './kardexView'

export type KardexColumnId =
  | 'date'
  | 'time'
  | 'type'
  | 'sku'
  | 'product'
  | 'owner'
  | 'category'
  | 'qty'
  /** Cantidad sin signo (transferencias: lo que se movió). */
  | 'amount'
  | 'position'
  | 'from'
  | 'to'
  | 'lotSerial'
  | 'reason'
  | 'notes'
  | 'origin'
  | 'ref'
  | 'user'

export const KARDEX_COLUMNS: readonly KardexColumnId[] = [
  'date',
  'time',
  'type',
  'sku',
  'product',
  'owner',
  'category',
  'qty',
  'position',
  'lotSerial',
  'reason',
  'ref',
  'user',
]

const C = 'warehouse.kardexView.columns'

export function useKardexColumns(ids: readonly KardexColumnId[] = KARDEX_COLUMNS): DataColumn<KardexRowDto>[] {
  const t = useT()
  const lang = useLang()
  const key = ids.join(',')
  return useMemo(() => {
    const all: Record<KardexColumnId, DataColumn<KardexRowDto>> = {
      date: {
        id: 'date',
        header: t(`${C}.date`),
        cell: (r) => <span className="mono">{splitDateTime(r.createdAtUtc, lang).date}</span>,
        // Excel/CSV: la fecha y hora reales en una sola celda de FECHA (la columna Hora no se exporta)
        exportValue: (r) => r.createdAtUtc,
        sortValue: (r) => r.createdAtUtc,
        card: 'title',
      },
      time: {
        id: 'time',
        header: t(`${C}.time`),
        cell: (r) => <span className="mono">{splitDateTime(r.createdAtUtc, lang).time}</span>,
        exportable: false,
        sortValue: (r) => r.createdAtUtc,
      },
      type: {
        id: 'type',
        header: t(`${C}.type`),
        cell: (r) => <Chip tone={txnTypeTone(r.typeCode)}>{r.type ?? r.typeCode}</Chip>,
        exportValue: (r) => r.type ?? r.typeCode ?? '',
        sortValue: (r) => r.type ?? r.typeCode,
      },
      sku: { id: 'sku', header: t(`${C}.sku`), cell: (r) => <span className="ref">{r.sku}</span>, exportValue: (r) => r.sku ?? '', sortValue: (r) => r.sku },
      product: { id: 'product', header: t(`${C}.product`), cell: (r) => r.productName ?? '', sortValue: (r) => r.productName },
      owner: { id: 'owner', header: t(`${C}.owner`), cell: (r) => r.ownerName ?? '', sortValue: (r) => r.ownerName },
      category: { id: 'category', header: t(`${C}.category`), cell: (r) => r.categoryName ?? '', sortValue: (r) => r.categoryName, card: 'hidden' },
      qty: {
        id: 'qty',
        header: t(`${C}.quantity`),
        cell: (r) => {
          const q = movementQtyView(r, lang)
          return <span className={`mono ${q.className}`}>{q.text}</span>
        },
        exportValue: (r) => movementQtyView(r, lang).value,
        sortValue: (r) => movementQtyView(r, lang).value,
        align: 'end',
      },
      amount: {
        id: 'amount',
        header: t(`${C}.quantity`),
        cell: (r) => <span className="mono">{formatNumber(r.quantity, lang)}</span>,
        exportValue: (r) => r.quantity ?? 0,
        sortValue: (r) => r.quantity,
        align: 'end',
      },
      position: {
        id: 'position',
        header: t(`${C}.position`),
        cell: (r) => <span className="mono">{fromToText(r)}</span>,
        exportValue: (r) => fromToText(r),
        sortValue: (r) => fromToText(r),
      },
      from: {
        id: 'from',
        header: t(`${C}.from`),
        cell: (r) => <span className="mono">{sideText(r.fromWarehouseCode, r.fromBinCode)}</span>,
        exportValue: (r) => sideText(r.fromWarehouseCode, r.fromBinCode),
        sortValue: (r) => sideText(r.fromWarehouseCode, r.fromBinCode),
      },
      to: {
        id: 'to',
        header: t(`${C}.to`),
        cell: (r) => <span className="mono">{sideText(r.toWarehouseCode, r.toBinCode)}</span>,
        exportValue: (r) => sideText(r.toWarehouseCode, r.toBinCode),
        sortValue: (r) => sideText(r.toWarehouseCode, r.toBinCode),
      },
      lotSerial: {
        id: 'lotSerial',
        header: t(`${C}.lotSerial`),
        cell: (r) => <span className="mono">{lotSerialText(r)}</span>,
        exportValue: (r) => lotSerialText(r),
        sortValue: (r) => lotSerialText(r),
      },
      reason: {
        id: 'reason',
        header: t(`${C}.reason`),
        cell: (r) => <span title={r.notes ?? undefined}>{r.reason ?? r.reasonCode ?? ''}</span>,
        exportValue: (r) => r.reason ?? r.reasonCode ?? '',
        sortValue: (r) => r.reason ?? r.reasonCode,
      },
      notes: { id: 'notes', header: t(`${C}.notes`), cell: (r) => r.notes ?? '', sortValue: (r) => r.notes, card: 'hidden' },
      origin: {
        id: 'origin',
        header: t(`${C}.origin`),
        cell: (r) => (
          <span>
            {t(`warehouse.kardexView.origin.${movementOrigin(r.refEntityCode)}`)}
            {r.refLabel ? <span className="kx-ref"> · {r.refLabel}</span> : null}
          </span>
        ),
        exportValue: (r) => [t(`warehouse.kardexView.origin.${movementOrigin(r.refEntityCode)}`), r.refLabel].filter(Boolean).join(' · '),
        sortValue: (r) => movementOrigin(r.refEntityCode),
      },
      ref: {
        id: 'ref',
        header: t(`${C}.ref`),
        cell: (r) => (r.refLabel ? <span className="kx-ref">{r.refLabel}</span> : t('warehouse.kardexView.origin.manual')),
        exportValue: (r) => r.refLabel ?? t('warehouse.kardexView.origin.manual'),
        sortValue: (r) => r.refLabel,
      },
      user: {
        id: 'user',
        header: t(`${C}.user`),
        cell: (r) => <span className="kx-ref">{r.userName ?? ''}</span>,
        exportValue: (r) => r.userName ?? '',
        sortValue: (r) => r.userName,
      },
    }
    return key.split(',').map((id) => all[id as KardexColumnId])
  }, [t, lang, key])
}
