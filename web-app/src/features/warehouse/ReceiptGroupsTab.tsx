// Lote 183 (C4) — pestaña «Maestros» de Recibo: las recepciones maestras (varios avisos u órdenes de compra del mismo dueño que llegan juntos): número, dueño, documentos, líneas, cantidades,
// estatus y fecha; clic = ficha con sus recibos (`ReceiptGroupModal`). «Recibir juntos» abre uno nuevo. Filtros al API: Estatus y el buscador libre. Tabla paginada por el servidor con `exportRows`.
import { useMemo, useRef, useState } from 'react'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Filters, IconDoc, Panel, QBox, SelectFilter, useElementWidth, type DataColumn } from '../../kernel/ui'
import { exportReceiptGroups, useReceiptGroups, type ReceiptGroupDto } from './api'
import { formatDateTime, useDebounced } from './lineRules'
import { ReceiptGroupModal } from './ReceiptGroupModal'
import './warehouse.css'

type GroupRow = ReceiptGroupDto
const PAGE_SIZE = 25
const NO_ROWS: never[] = []
const CARDS_BELOW_PX = 640

export function ReceiptGroupsTab({ onOpenReceipt, initialOpen }: { onOpenReceipt: (receiptPublicId: string) => void; initialOpen?: string | null }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [open, setOpen] = useState<string | null>(initialOpen ?? null)
  const search = useDebounced(q.trim())
  const reset = <T,>(setter: (v: T) => void) => (v: T) => {
    setPage(1)
    setter(v)
  }
  const query = useMemo(() => ({ status: status || undefined, search: search || undefined, skip: (page - 1) * pageSize, take: pageSize }), [status, search, page, pageSize])
  const { data, isLoading, error } = useReceiptGroups(query)

  const columns = useMemo<DataColumn<GroupRow>[]>(
    () => [
      { id: 'number', header: t('warehouse.receipts.groups.columns.number'), cell: (g) => <span className="ref">{g.number}</span>, sortValue: (g) => g.number ?? '', card: 'title' },
      { id: 'owner', header: t('warehouse.receipts.groups.columns.owner'), cell: (g) => g.ownerName ?? t('warehouse.receipts.groups.own'), sortValue: (g) => g.ownerName ?? '' },
      {
        id: 'documents',
        header: t('warehouse.receipts.groups.columns.documents'),
        cell: (g) => (g.members ?? []).map((m) => m.originRef).join(', '),
        sortValue: (g) => g.memberCount ?? 0,
        exportValue: (g) => (g.members ?? []).map((m) => m.originRef).join(' '),
      },
      { id: 'qty', header: t('warehouse.receipts.groups.columns.qty'), cell: (g) => `${f.number(g.receivedQty ?? 0)} / ${f.number(g.expectedQty ?? 0)}`, sortValue: (g) => g.receivedQty ?? 0, align: 'end' },
      {
        id: 'status',
        header: t('warehouse.receipts.groups.columns.status'),
        cell: (g) => <Chip>{t(`warehouse.receipts.groups.status.${g.statusCode}`)}</Chip>,
        sortValue: (g) => g.statusCode ?? '',
        exportValue: (g) => t(`warehouse.receipts.groups.status.${g.statusCode}`),
      },
      { id: 'at', header: t('warehouse.receipts.groups.columns.created'), cell: (g) => formatDateTime(g.createdAtUtc, lang), sortValue: (g) => g.createdAtUtc ?? '' },
    ],
    [t, lang, f],
  )

  return (
    <div ref={boxRef} className="collect-list">
      <Panel
        flush
        icon={<IconDoc />}
        title={t('warehouse.receipts.groups.title')}
        badge={data ? (data.total ?? 0) : undefined}
      >
        <div className="collect-filters">
          <Filters
            onClear={() => {
              setPage(1)
              setStatus('')
              setQ('')
            }}
          >
            <SelectFilter
              label={t('warehouse.receipts.groups.columns.status')}
              value={status}
              onChange={reset(setStatus)}
              options={['OPEN', 'CONFIRMED', 'CANCELLED'].map((s) => ({ value: s, label: t(`warehouse.receipts.groups.status.${s}`) }))}
            />
          </Filters>
        </div>
        <div className="qrow">
          <QBox value={q} onChange={reset(setQ)} placeholder={t('warehouse.receipts.groups.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.receipts.groups.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(g) => g.publicId ?? ''}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportReceiptGroups(query)}
            forceCards={width > 0 && width < CARDS_BELOW_PX}
            onRowClick={(g) => setOpen(g.publicId ?? null)}
            empty={t('warehouse.receipts.groups.empty')}
          />
        )}
      </Panel>
      <ReceiptGroupModal publicId={open} onClose={() => setOpen(null)} onOpenReceipt={onOpenReceipt} />
    </div>
  )
}
