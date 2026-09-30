// Lote 13 — lista maestra de Recibo (maqueta `recibo()`, columna de 340 px): una fila `.unrow` por recibo con el número y
// su `StatusChip` (colores del catálogo) a la derecha; el remitente (proveedor o cliente; en ciegos y devoluciones, el
// tipo); y en tono tenue transporte · llegada esperada (o fecha de alta) · origen (Orden de compra / Cliente / Ciego /
// Devolución) con el documento · referencia. La elegida va con fondo de flujo (`.on`). Clic = elegir (`?receipt=`);
// DOBLE CLIC = modal del encabezado (el mismo del lápiz del detalle, que es la vía accesible con teclado). Buscador libre
// (`QBox`, va al API) arriba y el pie de lista del kit (`ListPager`: rango, filas por página, ‹ › y Exportar todo lo
// filtrado) abajo: la paginación es del servidor. Exportar saca cada recibo CON SUS LÍNEAS (`receiptExport.ts`: Excel/CSV
// una fila por línea; PDF un bloque por recibo); la usan Recibos y 'Acomodo pendiente'. Lote 16: etiqueta "Directo" en
// los recibos directos a posición.
import { useMemo } from 'react'
import type { FetchAllResult } from '../../kernel/api/fetchAllPages'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { IconCheckin, ListPager, Panel, QBox, Spinner } from '../../kernel/ui'
import type { ReceiptListItemDto } from './api'
import { formatDate } from './lineRules'
import { receiptExportChildren, receiptExportColumns } from './receiptExport'
import { RECEIPT_STATUS_DOMAIN, receiptOrigin, receiptSender } from './receiptFilters'
import { isDirectMode } from './receivingMode'

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

  // columnas del archivo exportado (la lista no es una tabla) y sus líneas: exportación agrupada (receiptExport.ts)
  const exportColumns = useMemo(() => receiptExportColumns(t, lang), [t, lang])
  const exportLines = useMemo(() => receiptExportChildren(t, lang), [t, lang])

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
              {/* Lote 16: recibo directo a posición */}
              {isDirectMode(r.receivingModeCode) && (
                <>
                  {' '}
                  <span className="tag" title={t('warehouse.receipts.chip.direct')}>
                    {t('warehouse.receipts.list.direct')}
                  </span>
                </>
              )}
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
        exportChildren={exportLines}
        exportFileName={t('warehouse.receipts.list.exportName')}
      />
    </Panel>
  )
}
