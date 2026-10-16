// Operación → Órdenes de venta (Lote 60, módulo SALES_ORDERS, permiso sales.view): el documento que viaja de Ventas a Almacén. Dos pestañas: las ÓRDENES
// (lista con filtro de estatus y buscador; «Nueva orden» con sales.create) y los BACKORDERS (lo pendiente, el más antiguo primero; surtir una línea, surtir todo
// o cerrar con faltante con sales.backorder). Una orden se abre en su ventana (borrador editable, o solicitada con lo reservado y lo pendiente).
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Panel, QBox, Spinner, Tabs, toast, type DataColumn } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { OrderModal } from './OrderModal'
import { ORDER_STATUSES } from './salesRules'
import { salesOrderPageReader, useBackorders, useSalesMutations, useSalesOrders, type SalesBackorderLineDto, type SalesOrderListItemDto } from './salesApi'
import '../tax/tax.css'
import './sales.css'

type TabKey = 'orders' | 'backorders'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

function OrdersTab({ onOpen }: { onOpen: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useSalesOrders(status, search, (page - 1) * pageSize, pageSize)

  const columns = useMemo<DataColumn<SalesOrderListItemDto>[]>(
    () => [
      { id: 'number', header: t('sales.col.number'), cell: (o) => <b>{o.number}</b>, exportValue: (o) => o.number ?? '', sortValue: (o) => o.number ?? '', card: 'title' },
      { id: 'client', header: t('sales.col.client'), cell: (o) => o.clientName, sortValue: (o) => o.clientName ?? '' },
      {
        id: 'status',
        header: t('sales.col.status'),
        cell: (o) => (
          <>
            <Chip color={o.statusColor ?? undefined}>{o.statusLabel}</Chip> {o.hasBackorder && <Chip tone="warn">{t('sales.withBackorder')}</Chip>}
          </>
        ),
        exportValue: (o) => `${o.statusLabel ?? ''}${o.hasBackorder ? ` (${t('sales.withBackorder')})` : ''}`,
        sortValue: (o) => o.statusLabel ?? '',
      },
      { id: 'date', header: t('sales.col.date'), cell: (o) => f.date(o.requestedDate), exportValue: (o) => o.requestedDate ?? '', sortValue: (o) => o.requestedDate ?? '' },
      { id: 'required', header: t('sales.col.required'), cell: (o) => (o.requiredDate ? f.date(o.requiredDate) : '—'), exportValue: (o) => o.requiredDate ?? '', sortValue: (o) => o.requiredDate ?? '' },
      { id: 'lines', header: t('sales.col.lines'), cell: (o) => o.lineCount, sortValue: (o) => o.lineCount ?? 0, align: 'end' },
      { id: 'total', header: t('sales.col.total'), cell: (o) => f.money(o.total ?? 0), exportValue: (o) => o.total ?? 0, sortValue: (o) => o.total ?? 0, align: 'end' },
      { id: 'by', header: t('sales.col.by'), cell: (o) => o.requestedByName ?? '—', sortValue: (o) => o.requestedByName ?? '' },
    ],
    [t, f],
  )

  return (
    <Panel flush icon={<IconCart />} title={t('sales.orders.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('sales.orders.search')} />
          <select aria-label={t('sales.orders.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('sales.orders.allStatuses')}</option>
            {ORDER_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`sales.statuses.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('sales.orders.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(o) => o.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(o) => onOpen(o.publicId!)}
            empty={t('sales.orders.empty')}
            exportRows={() => fetchAllPages<SalesOrderListItemDto>(salesOrderPageReader(status, search))}
          />
        )}
      </div>
    </Panel>
  )
}

function BackordersTab({ onOpen }: { onOpen: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [q, setQ] = useState('')
  const search = useDebounced(q.trim())
  const list = useBackorders(search)
  const { fulfillLine, fulfillAll, closeLine } = useSalesMutations()

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<SalesBackorderLineDto>[]>(
    () => [
      { id: 'order', header: t('sales.col.number'), cell: (b) => <b>{b.orderNumber}</b>, exportValue: (b) => b.orderNumber ?? '', sortValue: (b) => b.orderNumber ?? '', card: 'title' },
      { id: 'client', header: t('sales.col.client'), cell: (b) => b.clientName, sortValue: (b) => b.clientName ?? '' },
      { id: 'product', header: t('sales.col.product'), cell: (b) => (<><b>{b.sku}</b><div className="help">{b.productName}</div></>), exportValue: (b) => `${b.sku} ${b.productName}`, sortValue: (b) => b.sku ?? '' },
      { id: 'ordered', header: t('sales.col.ordered'), cell: (b) => f.number(b.ordered ?? 0), sortValue: (b) => b.ordered ?? 0, align: 'end' },
      { id: 'reserved', header: t('sales.col.reserved'), cell: (b) => f.number(b.reserved ?? 0), sortValue: (b) => b.reserved ?? 0, align: 'end' },
      { id: 'pending', header: t('sales.col.pending'), cell: (b) => <Chip tone="warn">{f.number(b.pending ?? 0)}</Chip>, exportValue: (b) => b.pending ?? 0, sortValue: (b) => b.pending ?? 0, align: 'end' },
      { id: 'available', header: t('sales.col.available'), cell: (b) => f.number(b.available ?? 0), sortValue: (b) => b.available ?? 0, align: 'end' },
      {
        id: 'fulfillable',
        header: t('sales.col.fulfillable'),
        cell: (b) => ((b.fulfillable ?? 0) > 0 ? <Chip tone="deliv">{f.number(b.fulfillable ?? 0)}</Chip> : '—'),
        exportValue: (b) => b.fulfillable ?? 0,
        sortValue: (b) => b.fulfillable ?? 0,
        align: 'end',
      },
      { id: 'waiting', header: t('sales.col.waiting'), cell: (b) => t('sales.days', { n: b.daysWaiting ?? 0 }), exportValue: (b) => b.daysWaiting ?? 0, sortValue: (b) => b.daysWaiting ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const fulfillEverything = async () => {
    try {
      const r = await fulfillAll.mutateAsync()
      toast.success((r.lines ?? []).length === 0 ? t('sales.backorders.nothing') : t('sales.backorders.fulfilledAll', { units: r.totalReserved ?? 0, lines: (r.lines ?? []).length }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel
      flush
      icon={<IconCart />}
      title={t('sales.backorders.panel')}
      badge={list.isPending ? undefined : (list.data ?? []).length}
      actions={
        <Can perm="sales.backorder">
          <button type="button" className="btn flow" disabled={fulfillAll.isPending} onClick={() => void fulfillEverything()}>{t('sales.backorders.fulfillAll')}</button>
        </Can>
      }
    >
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={setQ} placeholder={t('sales.backorders.search')} />
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('sales.backorders.panel')}
            columns={columns}
            rows={list.data}
            rowKey={(b) => `${b.orderPublicId}-${b.lineId}`}
            defaultSort={{ id: 'waiting', desc: true }}
            onRowClick={(b) => onOpen(b.orderPublicId!)}
            empty={t('sales.backorders.empty')}
            rowActions={[
              {
                key: 'fulfill',
                label: t('sales.fulfill'),
                perm: 'sales.backorder',
                visible: (b) => (b.fulfillable ?? 0) > 0,
                onClick: (b) => void run(fulfillLine.mutateAsync({ id: b.orderPublicId!, lineId: b.lineId! }), t('sales.fulfilled', { sku: b.sku ?? '' })),
              },
              {
                key: 'close',
                label: t('sales.closeLine'),
                perm: 'sales.backorder',
                onClick: (b) => void run(closeLine.mutateAsync({ id: b.orderPublicId!, lineId: b.lineId! }), t('sales.lineClosed', { sku: b.sku ?? '' })),
              },
            ]}
          />
        )}
        <p className="help">{t('sales.backorders.help')}</p>
      </div>
    </Panel>
  )
}

export default function SalesOrdersScreen() {
  const t = useT()
  const [tab, setTab] = useState<TabKey>('orders')
  // Lote 67: el aviso del Pulso «Necesita tu atención» abre la orden directo (?order=<publicId>)
  const [params] = useSearchParams()
  const [openId, setOpenId] = useState<string | null | 'new'>(() => params.get('order'))
  return (
    <div className="sales-screen">
      <div className="head">
        <div>
          <h1>{t('sales.title')}</h1>
          <p>{t('sales.subtitle')}</p>
        </div>
        <Can perm="sales.create">
          <button type="button" className="btn flow" onClick={() => setOpenId('new')}>{t('sales.new')}</button>
        </Can>
      </div>
      <Tabs<TabKey>
        label={t('sales.tabs.label')}
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'orders', label: t('sales.tabs.orders') },
          { key: 'backorders', label: t('sales.tabs.backorders') },
        ]}
      />
      {tab === 'orders' ? <OrdersTab onOpen={setOpenId} /> : <BackordersTab onOpen={setOpenId} />}
      {openId !== null && <OrderModal key={openId} publicId={openId === 'new' ? null : openId} onClose={() => setOpenId(null)} />}
    </div>
  )
}
