// Lote 249 — bandeja «Por aprobar» de Órdenes de venta (sales.modify.approve): las modificaciones y adiciones que esperan la segunda aprobación de la compañía, paginadas
// por el SERVIDOR (buscador libre incluido). La tabla lee toda la consulta con `exportRows` para ordenar y exportar TODO lo filtrado, no solo la página.
import { useMemo, useState } from 'react'
import { fetchAllPages } from '../../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Chip, DataTable, Panel, QBox, Spinner, type DataColumn } from '../../../kernel/ui'
import { IconCart } from '../../../kernel/ui/screenIcons'
import { ChangeDetailModal } from './ChangeDetailModal'
import { pendingChangesPageReader, usePendingChanges, type ChangeListItemDto } from './changesApi'
import './changes.css'

export function PendingApprovalsTab() {
  const t = useT()
  const f = useFormat()
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [open, setOpen] = useState<{ order: string; change: string } | null>(null)
  const list = usePendingChanges(q, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<ChangeListItemDto>[]>(
    () => [
      { id: 'order', header: t('salesChanges.pendingTab.order'), cell: (c) => <b>{c.orderNumber}</b>, exportValue: (c) => c.orderNumber ?? '', sortValue: (c) => c.orderNumber ?? '', card: 'title' },
      { id: 'client', header: t('sales.col.client'), cell: (c) => c.clientName ?? '—', sortValue: (c) => c.clientName ?? '' },
      { id: 'no', header: t('salesChanges.history.no'), cell: (c) => `#${c.changeNo}`, exportValue: (c) => c.changeNo ?? 0, sortValue: (c) => c.changeNo ?? 0 },
      { id: 'kind', header: t('salesChanges.history.kind'), cell: (c) => <Chip tone={c.kind === 'ADDITION' ? 'cap' : 'neutral'}>{t(`salesChanges.kinds.${c.kind}`)}</Chip>, exportValue: (c) => t(`salesChanges.kinds.${c.kind}`), sortValue: (c) => c.kind ?? '' },
      { id: 'by', header: t('salesChanges.history.by'), cell: (c) => c.registeredByName ?? '—', sortValue: (c) => c.registeredByName ?? '' },
      { id: 'authorized', header: t('salesChanges.history.authorized'), cell: (c) => c.authorizedByName ?? '—', sortValue: (c) => c.authorizedByName ?? '' },
      { id: 'when', header: t('salesChanges.history.when'), cell: (c) => f.dateTime(c.registeredAtUtc), exportValue: (c) => c.registeredAtUtc ?? '', sortValue: (c) => c.registeredAtUtc ?? '' },
      { id: 'delta', header: t('salesChanges.history.delta'), cell: (c) => f.money(c.totalDelta ?? 0, { signed: true }), exportValue: (c) => c.totalDelta ?? 0, sortValue: (c) => c.totalDelta ?? 0, align: 'end' },
    ],
    [t, f],
  )
  return (
    <Panel flush icon={<IconCart />} title={t('salesChanges.pendingTab.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow">
          <QBox value={q} onChange={(v) => { setQ(v); setPage(1) }} placeholder={t('salesChanges.pendingTab.search')} />
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('salesChanges.pendingTab.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(c) => c.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(c) => setOpen({ order: c.orderPublicId ?? '', change: c.publicId ?? '' })}
            empty={q ? t('salesChanges.pendingTab.emptyFiltered') : t('salesChanges.pendingTab.empty')}
            exportRows={() => fetchAllPages<ChangeListItemDto>(pendingChangesPageReader(q))}
            rowActions={[{ key: 'review', label: t('salesChanges.pendingTab.review'), perm: 'sales.modify.approve', onClick: (c) => setOpen({ order: c.orderPublicId ?? '', change: c.publicId ?? '' }) }]}
          />
        )}
        <p className="help">{t('salesChanges.pendingTab.help')}</p>
      </div>
      {open && <ChangeDetailModal orderPublicId={open.order} changePublicId={open.change} onClose={() => setOpen(null)} />}
    </Panel>
  )
}
