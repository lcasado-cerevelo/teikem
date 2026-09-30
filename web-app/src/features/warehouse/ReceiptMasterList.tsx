// Lote 13 — lista maestra de Recibo (maqueta `recibo()`, columna de 340 px): una fila `.unrow` por recibo con el número y
// su `StatusChip` (colores del catálogo) a la derecha; el remitente (proveedor o cliente; en ciegos y devoluciones, el
// tipo); y en tono tenue transporte · llegada esperada (o fecha de alta) · origen (Orden de compra / Cliente / Ciego /
// Devolución) con el documento · referencia. La elegida va con fondo de flujo (`.on`). Clic = elegir (`?receipt=`);
// DOBLE CLIC = modal del encabezado (el mismo del lápiz del detalle, que es la vía accesible con teclado). Buscador libre
// (`QBox`, va al API) arriba y el pie de lista del kit (`ListPager`: rango, filas por página, ‹ › y Exportar todo lo
// filtrado) abajo: la paginación es del servidor.
import { useMemo } from 'react'
import type { FetchAllResult } from '../../kernel/api/fetchAllPages'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { IconCheckin, ListPager, Panel, QBox, Spinner, type DataColumn } from '../../kernel/ui'
import type { ReceiptListItemDto } from './api'
import { formatDate, formatDateTime, formatNumber } from './lineRules'
import { RECEIPT_STATUS_DOMAIN, receiptOrigin, receiptSender } from './receiptFilters'

export interface ReceiptMasterListProps {
  title: string
  items: readonly ReceiptListItemDto[]
  total: number | undefined
  loading?: boolean
  error?: Error | null
  selectedId: string | null
  onSelect: (publicId: string) => void
  /** Doble clic: abre el modal del encabezado. */
  onOpen: (publicId: string) => void
  q: string
  onQ: (q: string) => void
  page: number
  pageSize: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
  exportRows: () => Promise<readonly ReceiptListItemDto[] | FetchAllResult<ReceiptListItemDto>>
}

export function ReceiptMasterList(props: ReceiptMasterListProps) {
  const { title, items, total, loading, error, selectedId, onSelect, onOpen } = props
  const t = useT()
  const lang = useLang()

  // columnas del archivo exportado (la lista no es una tabla)
  const exportColumns = useMemo<DataColumn<ReceiptListItemDto>[]>(
    () => [
      { id: 'number', header: t('warehouse.receipts.list.columns.number'), cell: (r) => r.number ?? '' },
      { id: 'status', header: t('warehouse.receipts.list.columns.status'), cell: (r) => r.status ?? r.statusCode ?? '' },
      { id: 'type', header: t('warehouse.receipts.list.columns.type'), cell: (r) => r.type ?? r.typeCode ?? '' },
      { id: 'origin', header: t('warehouse.receipts.list.columns.origin'), cell: (r) => t(`warehouse.receipts.originLong.${receiptOrigin(r.origin)}`) },
      { id: 'originRef', header: t('warehouse.receipts.list.columns.originRef'), cell: (r) => r.originRef ?? '' },
      { id: 'sender', header: t('warehouse.receipts.list.columns.sender'), cell: (r) => r.senderName ?? '' },
      { id: 'warehouse', header: t('warehouse.receipts.list.columns.warehouse'), cell: (r) => r.warehouseCode ?? '' },
      { id: 'carrier', header: t('warehouse.receipts.list.columns.carrier'), cell: (r) => r.carrier ?? '' },
      { id: 'reference', header: t('warehouse.receipts.list.columns.reference'), cell: (r) => r.reference ?? '' },
      { id: 'expectedDate', header: t('warehouse.receipts.list.columns.expectedDate'), cell: (r) => formatDate(r.expectedDate, lang), exportValue: (r) => r.expectedDate ?? null },
      { id: 'createdAt', header: t('warehouse.receipts.list.columns.createdAt'), cell: (r) => formatDateTime(r.createdAtUtc, lang) },
      { id: 'lines', header: t('warehouse.receipts.list.columns.lines'), cell: (r) => formatNumber(r.lineCount, lang), exportValue: (r) => r.lineCount ?? 0 },
      { id: 'variance', header: t('warehouse.receipts.list.columns.variance'), cell: (r) => formatNumber(r.varianceQty, lang), exportValue: (r) => r.varianceQty ?? 0 },
    ],
    [t, lang],
  )

  let body
  if (loading && items.length === 0) body = <Spinner block />
  else if (error)
    body = (
      <p className="pb ferr" role="alert">
        {error.message || t('errors.generic')}
      </p>
    )
  else if (items.length === 0)
    body = (
      <div className="empty rcp-empty sm">
        <div>
          <IconCheckin />
          <p>{t('warehouse.receipts.list.empty')}</p>
        </div>
      </div>
    )
  else
    body = items.map((r) => {
      const on = r.publicId === selectedId
      const origin = receiptOrigin(r.origin)
      const date = r.expectedDate ? formatDate(r.expectedDate, lang) : formatDate(r.createdAtUtc, lang)
      const lead = [r.carrier, date].filter(Boolean).join(' · ')
      return (
        <button
          key={r.publicId}
          type="button"
          className={on ? 'unrow on' : 'unrow'}
          aria-current={on ? 'true' : undefined}
          onClick={() => r.publicId && onSelect(r.publicId)}
          onDoubleClick={() => r.publicId && onOpen(r.publicId)}
        >
          <span className="rcp-item">
            <span className="rcp-top">
              <span className="ref">{r.number}</span>
              <StatusChip domain={RECEIPT_STATUS_DOMAIN} code={r.statusCode} label={r.status} />
            </span>
            <span className="rcp-from">{receiptSender(r)}</span>
            <span className="meta rcp-line">
              {lead && <span>{lead} · </span>}
              <span className={origin === 'PO' ? 'tag m' : 'tag'}>{t(`warehouse.receipts.originLong.${origin}`)}</span>
              {r.originRef && <span className="ref rcp-doc">{r.originRef}</span>}
              {r.reference && <span> · {r.reference}</span>}
              {(r.pendingPutawayCount ?? 0) > 0 && <span> · {t('warehouse.receipts.list.pendingPutaway', { count: r.pendingPutawayCount ?? 0 })}</span>}
            </span>
          </span>
        </button>
      )
    })

  return (
    <Panel flush icon={<IconCheckin />} title={title} badge={total ?? undefined} className="rcp-master">
      <div className="qrow">
        <QBox value={props.q} onChange={props.onQ} placeholder={t('warehouse.receipts.searchPlaceholder')} />
      </div>
      <div className="rcp-list" role="group" aria-label={t('warehouse.receipts.list.aria')} aria-busy={loading || undefined}>
        {body}
      </div>
      <ListPager
        page={props.page}
        pageSize={props.pageSize}
        total={total ?? 0}
        onPage={props.onPage}
        onPageSize={props.onPageSize}
        exportColumns={exportColumns}
        exportRows={props.exportRows}
        exportFileName={t('warehouse.receipts.list.exportName')}
      />
    </Panel>
  )
}
