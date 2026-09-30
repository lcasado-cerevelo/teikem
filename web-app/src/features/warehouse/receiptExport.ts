// Exportación de la lista de Recibo (pestañas Recibos y 'Acomodo pendiente') CON SUS LÍNEAS, pedido del dueño del
// producto: el pie de la lista (`ListPager`) exporta todo lo filtrado con `exportReceiptsWithLines(query)` (el API trae
// las líneas de cada página en lote) y la exportación agrupada del kit (`exportChildren`):
// - Excel/CSV: una fila por línea repitiendo los datos del recibo; un recibo sin líneas = una fila con las de línea vacías.
// - PDF (carta horizontal): un bloque por recibo con banda (número y estatus de título; tipo, origen, documento,
//   remitente, almacén, transporte, referencia, llegada esperada, creado, confirmado y diferencia) y la tablita de sus
//   líneas (SKU, Producto, Esperado, Recibido, Diferencia con signo, Lote/Serie); sin líneas = "Sin líneas".
// Ya no va la columna "Líneas" (se ven las líneas). Las columnas son puras (reciben `t` y el idioma) para probarlas sin DOM.
import type { components } from '../../kernel/api/schema'
import type { TParams } from '../../kernel/i18n'
import { exportChildren, type DataColumn, type ExportChildren } from '../../kernel/ui'
import type { ReceiptListItemDto } from './api'
import { formatDate, formatDateTime, formatNumber } from './lineRules'
import { receiptOrigin } from './receiptFilters'

type LineDto = components['schemas']['ReceiptLineDto']
type Translate = (key: string, params?: TParams) => string

/** Columnas del recibo en el archivo (madre de la exportación agrupada; las dos primeras son el título de la banda del PDF). */
export function receiptExportColumns(t: Translate, lang: string): DataColumn<ReceiptListItemDto>[] {
  const c = (k: string) => t(`warehouse.receipts.list.columns.${k}`)
  return [
    { id: 'number', header: c('number'), cell: (r) => r.number ?? '' },
    { id: 'status', header: c('status'), cell: (r) => r.status ?? r.statusCode ?? '' },
    { id: 'type', header: c('type'), cell: (r) => r.type ?? r.typeCode ?? '' },
    { id: 'origin', header: c('origin'), cell: (r) => t(`warehouse.receipts.originLong.${receiptOrigin(r.origin)}`) },
    { id: 'originRef', header: c('originRef'), cell: (r) => r.originRef ?? '' },
    { id: 'sender', header: c('sender'), cell: (r) => r.senderName ?? '' },
    { id: 'warehouse', header: c('warehouse'), cell: (r) => r.warehouseCode ?? '' },
    { id: 'carrier', header: c('carrier'), cell: (r) => r.carrier ?? '' },
    { id: 'reference', header: c('reference'), cell: (r) => r.reference ?? '' },
    { id: 'expectedDate', header: c('expectedDate'), cell: (r) => formatDate(r.expectedDate, lang), exportValue: (r) => r.expectedDate ?? null },
    { id: 'createdAt', header: c('createdAt'), cell: (r) => formatDateTime(r.createdAtUtc, lang) },
    { id: 'receivedAt', header: c('receivedAt'), cell: (r) => formatDateTime(r.receivedAtUtc, lang) },
    {
      id: 'variance',
      header: c('varianceTotal'),
      align: 'end',
      signed: true,
      cell: (r) => formatNumber(r.varianceQty, lang),
      exportValue: (r) => r.varianceQty ?? 0,
    },
  ]
}

/** Lote de la línea o, si no tiene, sus series separadas por coma. */
export function lotSerialText(l: Pick<LineDto, 'lotNumber' | 'serialNumbers'>): string {
  if (l.lotNumber) return l.lotNumber
  return (l.serialNumbers ?? []).filter(Boolean).join(', ')
}

/** Columnas de las líneas (hijas de la exportación agrupada). */
export function receiptLineExportColumns(t: Translate, lang: string): DataColumn<LineDto>[] {
  const c = (k: string) => t(`warehouse.receipts.list.exportLines.${k}`)
  return [
    { id: 'sku', header: c('sku'), cell: (l) => l.sku ?? '' },
    { id: 'product', header: c('product'), cell: (l) => l.productName ?? '' },
    { id: 'expected', header: c('expected'), align: 'end', cell: (l) => formatNumber(l.expectedQty, lang), exportValue: (l) => l.expectedQty ?? null },
    { id: 'received', header: c('received'), align: 'end', cell: (l) => formatNumber(l.receivedQty, lang), exportValue: (l) => l.receivedQty ?? 0 },
    {
      id: 'variance',
      header: c('variance'),
      align: 'end',
      signed: true,
      cell: (l) => formatNumber(l.varianceQty, lang),
      exportValue: (l) => l.varianceQty ?? 0,
    },
    { id: 'lotSerial', header: c('lotSerial'), cell: (l) => lotSerialText(l) },
  ]
}

/** Hijas de la exportación: las líneas que trae cada recibo (`includeLines`); número y estatus = título de la banda. */
export function receiptExportChildren(t: Translate, lang: string): ExportChildren<ReceiptListItemDto> {
  return exportChildren<ReceiptListItemDto, LineDto>({
    children: (r) => r.lines,
    columns: receiptLineExportColumns(t, lang),
    emptyText: t('warehouse.receipts.list.exportLines.noLines'),
    titleColumns: 2,
  })
}
