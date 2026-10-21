// Lote 177 — lista de recolecciones MAESTRAS (varios documentos del mismo cliente recolectados juntos, un solo envío): número, cliente, documentos, cantidad, estatus y fecha; clic = ficha
// con TODOS los movimientos (`PickGroupModal`). Filtros al API: Estatus y el buscador libre (número, cliente o número de un documento). Tabla paginada por el servidor con `exportRows`.
import { useMemo, useRef, useState } from 'react'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Filters, IconDoc, Panel, QBox, SelectFilter, useElementWidth, type DataColumn } from '../../kernel/ui'
import { exportPickGroups, usePickGroups, type PickGroupDto } from './api'
import { formatDateTime, useDebounced } from './lineRules'
import { PickGroupModal } from './PickGroupModal'
import './warehouse.css'

const PAGE_SIZE = 25
const NO_ROWS: never[] = []
const CARDS_BELOW_PX = 640

export function PickGroupsPanel({ openId, onOpened }: { openId?: string | null; onOpened?: () => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [open, setOpen] = useState<string | null>(openId ?? null)
  const search = useDebounced(q.trim())
  const reset = <T,>(setter: (v: T) => void) => (v: T) => {
    setPage(1)
    setter(v)
  }
  const query = useMemo(() => ({ status: status || undefined, search: search || undefined, skip: (page - 1) * pageSize, take: pageSize }), [status, search, page, pageSize])
  const { data, isLoading, error } = usePickGroups(query)

  const columns = useMemo<DataColumn<PickGroupDto>[]>(
    () => [
      {
        id: 'number',
        header: t('warehouse.pickBatches.groups.columns.number'),
        cell: (g) => <span className="ref">{g.number}</span>,
        sortValue: (g) => g.number ?? '',
        card: 'title',
      },
      { id: 'client', header: t('warehouse.pickBatches.docs.columns.client'), cell: (g) => g.clientName ?? '—', sortValue: (g) => g.clientName ?? '' },
      {
        id: 'documents',
        header: t('warehouse.pickBatches.groups.columns.documents'),
        cell: (g) => (g.documents ?? []).map((d) => d.number).join(', '),
        sortValue: (g) => g.documents?.length ?? 0,
        exportValue: (g) => (g.documents ?? []).map((d) => d.number).join(' '),
      },
      { id: 'qty', header: t('warehouse.pickBatches.groups.columns.qty'), cell: (g) => f.number(g.totalQty ?? 0), sortValue: (g) => g.totalQty ?? 0, align: 'end' },
      {
        id: 'status',
        header: t('warehouse.pickBatches.docs.columns.status'),
        cell: (g) => <Chip>{t(`warehouse.pickBatches.groups.status.${g.statusCode}`)}</Chip>,
        sortValue: (g) => g.statusCode ?? '',
        exportValue: (g) => t(`warehouse.pickBatches.groups.status.${g.statusCode}`),
      },
      { id: 'shipment', header: t('warehouse.pickBatches.groups.shipment'), cell: (g) => g.orderNumber ?? '—', sortValue: (g) => g.orderNumber ?? '' },
      { id: 'at', header: t('warehouse.pickBatches.groups.collected'), cell: (g) => formatDateTime(g.collectedAtUtc, lang), sortValue: (g) => g.collectedAtUtc ?? '' },
    ],
    [t, lang, f],
  )

  return (
    <div ref={boxRef} className="collect-list">
      <Panel flush icon={<IconDoc />} title={t('warehouse.pickBatches.groups.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="collect-filters">
          <Filters
            onClear={() => {
              setPage(1)
              setStatus('')
              setQ('')
            }}
          >
            <SelectFilter
              label={t('warehouse.pickBatches.docs.columns.status')}
              value={status}
              onChange={reset(setStatus)}
              options={['COLLECTED', 'PACKED', 'CANCELLED'].map((s) => ({ value: s, label: t(`warehouse.pickBatches.groups.status.${s}`) }))}
            />
          </Filters>
        </div>
        <div className="qrow">
          <QBox value={q} onChange={reset(setQ)} placeholder={t('warehouse.pickBatches.groups.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.pickBatches.groups.title')}
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
            exportRows={() => exportPickGroups(query)}
            forceCards={width > 0 && width < CARDS_BELOW_PX}
            onRowClick={(g) => setOpen(g.publicId ?? null)}
            empty={t('warehouse.pickBatches.groups.empty')}
          />
        )}
      </Panel>
      <PickGroupModal
        publicId={open}
        onClose={() => {
          setOpen(null)
          onOpened?.()
        }}
      />
    </div>
  )
}
