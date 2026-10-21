// Lote 144 — pestaña «Avisos de salida» de Recibo (`?tab=exit-notices`): el dueño de la mercancía avisa qué quiere que el almacén saque (manual 40). Lista del servidor con buscador libre
// (número, referencia, destino, cliente, producto), filtro de estatus y «solo con pendiente»; «Nuevo aviso de salida» y «Reservar el pendiente» con exitnotice.create; la fila abre su ventana
// (`ExitNoticeModal`). Ordenar y exportar usan TODO lo que dice el filtro (`exportRows`).
import { useEffect, useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { useStatuses } from '../../kernel/catalogs'
import { ClientPicker, Chip, DataTable, DateRangeFilter, EMPTY_RANGE, Filters, Panel, SearchSelect, Spinner, toast, type DataColumn, type DateRange } from '../../kernel/ui'
import { IconCheckin } from '../../kernel/ui'
import { TextFilter } from './filterControls'
import { ExitNoticeCreateModal } from './ExitNoticeCreateModal'
import { ExitNoticeModal } from './ExitNoticeModal'
import { exitNoticePageReader, useExitNoticeMutations, useExitNotices, type ExitNoticeListItemDto } from './exitNoticeApi'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

export function ExitNoticesTab({ initialOpen }: { initialOpen?: string | null }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState<string[]>([])
  const [clientPublicId, setClientPublicId] = useState<string | null>(null)
  const [dates, setDates] = useState<DateRange>(EMPTY_RANGE)
  const [q, setQ] = useState('')
  const statuses = useStatuses('ExitNoticeStatus')
  const statusOptions = useMemo(() => (statuses.data ?? []).map((s) => ({ value: s.code, label: s.label })), [statuses.data])
  const [pendingOnly, setPendingOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [creating, setCreating] = useState(false)
  const [open, setOpen] = useState<string | null>(initialOpen ?? null)
  const search = useDebounced(q.trim())
  const filter = useMemo(
    () => ({ status: status.join(','), q: search, backorderOnly: pendingOnly, clientPublicId, from: dates.from || undefined, to: dates.to || undefined }),
    [status, search, pendingOnly, clientPublicId, dates],
  )
  const list = useExitNotices(filter, (page - 1) * pageSize, pageSize)
  const { fulfillAll } = useExitNoticeMutations()

  const columns = useMemo<DataColumn<ExitNoticeListItemDto>[]>(
    () => [
      { id: 'number', header: t('warehouse.exitNotices.col.number'), cell: (o) => <b>{o.number}</b>, exportValue: (o) => o.number ?? '', sortValue: (o) => o.number ?? '', card: 'title' },
      { id: 'client', header: t('warehouse.exitNotices.col.client'), cell: (o) => o.clientName, sortValue: (o) => o.clientName ?? '' },
      { id: 'warehouse', header: t('warehouse.exitNotices.col.warehouse'), cell: (o) => o.warehouseCode, sortValue: (o) => o.warehouseCode ?? '' },
      { id: 'reference', header: t('warehouse.exitNotices.col.reference'), cell: (o) => o.reference ?? '—', sortValue: (o) => o.reference ?? '' },
      {
        id: 'status',
        header: t('warehouse.exitNotices.col.status'),
        cell: (o) => (
          <>
            <Chip color={o.statusColor ?? undefined}>{o.status}</Chip> {o.hasBackorder && <Chip tone="warn">{t('warehouse.exitNotices.withPending')}</Chip>}
          </>
        ),
        exportValue: (o) => `${o.status ?? ''}${o.hasBackorder ? ` (${t('warehouse.exitNotices.withPending')})` : ''}`,
        sortValue: (o) => o.status ?? '',
      },
      { id: 'date', header: t('warehouse.exitNotices.col.date'), cell: (o) => f.date(o.requestedDate), exportValue: (o) => o.requestedDate ?? '', sortValue: (o) => o.requestedDate ?? '' },
      { id: 'required', header: t('warehouse.exitNotices.col.required'), cell: (o) => (o.requiredDate ? f.date(o.requiredDate) : '—'), exportValue: (o) => o.requiredDate ?? '', sortValue: (o) => o.requiredDate ?? '' },
      { id: 'lines', header: t('warehouse.exitNotices.col.lines'), cell: (o) => o.lineCount, sortValue: (o) => o.lineCount ?? 0, align: 'end' },
      { id: 'ordered', header: t('warehouse.exitNotices.col.ordered'), cell: (o) => f.number(o.qtyOrdered ?? 0), sortValue: (o) => o.qtyOrdered ?? 0, align: 'end' },
      { id: 'shipped', header: t('warehouse.exitNotices.col.shipped'), cell: (o) => f.number(o.qtyShipped ?? 0), sortValue: (o) => o.qtyShipped ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const reserveEverything = async () => {
    try {
      const r = await fulfillAll.mutateAsync()
      toast.success((r.lines ?? []).length === 0 ? t('warehouse.exitNotices.reserveNothing') : t('warehouse.exitNotices.reservedAll', { units: r.unitsReserved ?? 0, lines: r.linesReserved ?? 0 }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <>
      <Filters
        onClear={() => {
          setStatus([])
          setClientPublicId(null)
          setDates(EMPTY_RANGE)
          setQ('')
          setPendingOnly(false)
          setPage(1)
        }}
      >
        <TextFilter label={t('warehouse.exitNotices.filterSearch')} value={q} onChange={(v) => (setQ(v), setPage(1))} />
        <div className="f">
          <label>{t('warehouse.asns.filters.client')}</label>
          <ClientPicker value={clientPublicId} onChange={(v) => (setClientPublicId(v), setPage(1))} includeInactive placeholder={t('warehouse.asns.filters.anyClient')} filterLabel={t('warehouse.asns.filters.client')} />
        </div>
        <SearchSelect label={t('warehouse.exitNotices.filterStatus')} options={statusOptions} value={status} onChange={(v) => (setStatus(v), setPage(1))} />
        <DateRangeFilter label={t('warehouse.exitNotices.filterDates')} value={dates} onChange={(v) => (setDates(v), setPage(1))} />
        <label className="chk">
          <input type="checkbox" checked={pendingOnly} onChange={(e) => (setPendingOnly(e.target.checked), setPage(1))} /> {t('warehouse.exitNotices.pendingOnly')}
        </label>
      </Filters>
      <Panel
        flush
        icon={<IconCheckin />}
        title={t('warehouse.exitNotices.title')}
        badge={list.isPending ? undefined : list.data?.total}
        actions={
          <Can perm="exitnotice.create">
            <button type="button" className="btn sm" disabled={fulfillAll.isPending} onClick={() => void reserveEverything()}>
              {t('warehouse.exitNotices.reserveAll')}
            </button>
            <button type="button" className="btn flow sm" onClick={() => setCreating(true)}>
              {t('warehouse.exitNotices.new')}
            </button>
          </Can>
        }
      >
        <div className="pb">
          {list.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('warehouse.exitNotices.title')}
              columns={columns}
              rows={list.data?.items ?? []}
              rowKey={(o) => o.publicId ?? ''}
              page={page}
              pageSize={pageSize}
              total={list.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(s) => (setPageSize(s), setPage(1))}
              onRowClick={(o) => setOpen(o.publicId ?? null)}
              empty={t('warehouse.exitNotices.empty')}
              exportRows={() => fetchAllPages<ExitNoticeListItemDto>(exitNoticePageReader(filter))}
            />
          )}
          <p className="help">{t('warehouse.exitNotices.help')}</p>
        </div>
      </Panel>
      {creating && <ExitNoticeCreateModal onClose={() => setCreating(false)} onCreated={(id) => setOpen(id || null)} />}
      {open && <ExitNoticeModal publicId={open} onClose={() => setOpen(null)} />}
    </>
  )
}
