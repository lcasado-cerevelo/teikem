// Operación → Órdenes de venta (Lote 60, módulo SALES_ORDERS, permiso sales.view): el documento que viaja de Ventas a Almacén. Dos pestañas: las ÓRDENES
// (lista con filtros y buscador; «Nueva orden» con sales.create) y los BACKORDERS (lo pendiente, el más antiguo primero; surtir una línea, surtir todo
// o cerrar con faltante con sales.backorder). Una orden se abre en su ventana (borrador editable, o solicitada con lo reservado y lo pendiente).
// 2026-11-09 (pedido de Luis): filtros de la pestaña Órdenes, en este orden: buscador libre, fechas (Solicitada o Requerida + rango con sugerencias y
// personalizado, `DateRangeFilter`), Cliente (`ClientSearchSelect`), Producto (`ProductSearchSelect`) y Estatus (`SearchSelect`), todos de varias opciones
// con búsqueda, y «Solo los asignados a mí»; botón Limpiar. Todo va al servidor (también el Exportar y el orden, que leen TODO lo filtrado con `exportRows`).
// Los filtros viven en la pantalla (no en la pestaña): se conservan al ir a Backorders y volver.
import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useEntryMode, wizardPath } from './entryMode'
import { Can, permAllowed, useAccess, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ClientSearchSelect,
  DataTable,
  DateRangeFilter,
  Filters,
  FilterScope,
  IconUserPlus,
  IconUsers,
  Panel,
  QBox,
  SearchSelect,
  SelectFilter,
  Spinner,
  Tabs,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { AssignDocumentModal } from '../warehouse/AssignDocumentModal'
import { ToggleFilter } from '../warehouse/filterControls'
import { ProductSearchSelect } from '../warehouse/pickers'
import { EMPTY_ORDERS_FILTERS, hasOrdersFilters, ordersApiFilters, type OrdersFilterState } from './ordersFilters'
import { IconCart } from '../../kernel/ui/screenIcons'
import { OrderBadges } from './OrderBadges'
import { OrderModal } from './OrderModal'
import { QuickBooksTab } from './QuickBooksTab'
import { ReturnEditor, ReturnModal, ReturnsTab } from './ReturnsParts'
import { ORDER_STATUSES } from './salesRules'
import {
  salesOrderPageReader,
  useBackorders,
  useSalesMutations,
  useSalesOrders,
  type SalesBackorderLineDto,
  type SalesOrderListItemDto,
} from './salesApi'
import '../tax/tax.css'
import './sales.css'

type TabKey = 'orders' | 'backorders' | 'returns' | 'quickbooks'
const TAB_KEYS: readonly TabKey[] = ['orders', 'backorders', 'returns', 'quickbooks']
/** Lote 225: la pestaña QuickBooks la ven los tres permisos de QuickBooks (quien marca o exporta necesita ver la lista, como el API). */
const QUICKBOOKS_PERMS = 'sales.quickbooks.view|sales.quickbooks.mark|sales.quickbooks.export'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

function OrdersTab({ onOpen, filters, onFilters }: { onOpen: (id: string, status?: string | null) => void; filters: OrdersFilterState; onFilters: (next: OrdersFilterState) => void }) {
  const t = useT()
  const f = useFormat()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(filters.q.trim())
  const [toAssign, setToAssign] = useState<SalesOrderListItemDto | null>(null)
  const { assign, take } = useSalesMutations()
  const apiFilters = useMemo(() => ordersApiFilters(filters, search), [filters, search])
  const list = useSalesOrders(apiFilters, (page - 1) * pageSize, pageSize)
  // cambiar un filtro vuelve a la página 1
  const set = <K extends keyof OrdersFilterState>(key: K) => (v: OrdersFilterState[K]) => {
    setPage(1)
    onFilters({ ...filters, [key]: v })
  }
  const statusOptions = useMemo(() => ORDER_STATUSES.map((s) => ({ value: s, label: t(`sales.statuses.${s}`) })), [t])
  const requiredDate = filters.dateField === 'REQUIRED'

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
            <OrderBadges info={o} />
          </>
        ),
        exportValue: (o) => `${o.statusLabel ?? ''}${o.hasBackorder ? ` (${t('sales.withBackorder')})` : ''}`,
        sortValue: (o) => o.statusLabel ?? '',
      },
      { id: 'date', header: t('sales.col.date'), cell: (o) => f.date(o.requestedDate), exportValue: (o) => o.requestedDate ?? '', sortValue: (o) => o.requestedDate ?? '' },
      { id: 'required', header: t('sales.col.required'), cell: (o) => (o.requiredDate ? f.date(o.requiredDate) : '—'), exportValue: (o) => o.requiredDate ?? '', sortValue: (o) => o.requiredDate ?? '' },
      { id: 'lines', header: t('sales.col.lines'), cell: (o) => o.lineCount, sortValue: (o) => o.lineCount ?? 0, align: 'end' },
      { id: 'total', header: t('sales.col.total'), cell: (o) => f.money(o.total ?? 0), exportValue: (o) => o.total ?? 0, sortValue: (o) => o.total ?? 0, align: 'end' },
      { id: 'assignee', header: t('warehouse.assign.column'), cell: (o) => o.assignedToName ?? '—', exportValue: (o) => o.assignedToName ?? '', sortValue: (o) => o.assignedToName ?? '' },
      { id: 'by', header: t('sales.col.by'), cell: (o) => o.requestedByName ?? '—', sortValue: (o) => o.requestedByName ?? '' },
    ],
    [t, f],
  )

  // Lote 195: iconos con su ayuda en la tabla (solo en una orden solicitada o despachada en parte)
  const open1 = (o: SalesOrderListItemDto) => o.status === 'REQUESTED' || o.status === 'PARTIAL'
  const rowActions = useMemo<RowAction<SalesOrderListItemDto>[]>(
    () => [
      {
        key: 'take',
        label: t('warehouse.assign.take'),
        perm: 'warehouse.pick',
        visible: (o) => open1(o) && o.assignedToUserId == null,
        onClick: (o) => {
          take.mutateAsync(o.publicId ?? '').then(
            () => toast.success(t('warehouse.assign.taken')),
            (e) => toast.error(applyProblemDetails(e).title),
          )
        },
        icon: <IconUserPlus />,
      },
      { key: 'assign', label: t('warehouse.assign.action'), perm: 'warehouse.manage', visible: open1, onClick: (o) => setToAssign(o), icon: <IconUsers /> },
    ],
    [t, take],
  )

  return (
    <Panel flush icon={<IconCart />} title={t('sales.orders.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <Filters
          label={t('sales.orders.filters.label')}
          onClear={() => {
            setPage(1)
            onFilters(EMPTY_ORDERS_FILTERS)
          }}
        >
          <QBox value={filters.q} onChange={set('q')} placeholder={t('sales.orders.search')} />
          {/* fechas: cuál (Solicitada por omisión, o Requerida) y el rango con sugerencias y «Personalizado»; la requerida puede ser futura. El
              selector no se anota en la línea de filtros de las exportaciones: ya lo dice la etiqueta del rango («Requerida del … al …») */}
          <FilterScope off>
            <SelectFilter
              label={t('sales.orders.filters.dateField')}
              value={requiredDate ? 'REQUIRED' : ''}
              onChange={(v) => set('dateField')(v === 'REQUIRED' ? 'REQUIRED' : 'REQUESTED')}
              allLabel={t('sales.orders.filters.requested')}
              options={[{ value: 'REQUIRED', label: t('sales.orders.filters.required') }]}
            />
          </FilterScope>
          <DateRangeFilter
            label={requiredDate ? t('sales.orders.filters.required') : t('sales.orders.filters.requested')}
            value={filters.range}
            onChange={set('range')}
            mode={requiredDate ? 'both' : 'past'}
          />
          <ClientSearchSelect label={t('sales.orders.filters.client')} value={filters.clients} onChange={set('clients')} includeInactive />
          <ProductSearchSelect label={t('sales.orders.filters.product')} value={filters.products} onChange={set('products')} includeInactive />
          <SearchSelect label={t('sales.orders.filters.status')} options={statusOptions} value={filters.statuses} onChange={set('statuses')} />
          <ToggleFilter label={t('warehouse.assign.mineOnly')} checked={filters.mine} onChange={set('mine')} />
        </Filters>
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
            rowActions={rowActions}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(o) => onOpen(o.publicId!, o.status)}
            empty={hasOrdersFilters(filters) ? t('sales.orders.emptyFiltered') : t('sales.orders.empty')}
            exportRows={() => fetchAllPages<SalesOrderListItemDto>(salesOrderPageReader(apiFilters))}
          />
        )}
      </div>
      {toAssign && (
        <AssignDocumentModal
          title={t('warehouse.assign.title', { number: toAssign.number ?? '' })}
          currentUserId={toAssign.assignedToUserId}
          onSave={(userId) => assign.mutateAsync({ id: toAssign.publicId ?? '', userId })}
          onClose={() => setToAssign(null)}
        />
      )}
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
  // Lote 225: la pestaña va en ?tab= (orders = sin parámetro) para que el Pulso y «Necesita tu atención» abran QuickBooks directo (?tab=quickbooks)
  const [params, setParams] = useSearchParams()
  const { permissions } = useAccess()
  const canQuickBooks = permAllowed(QUICKBOOKS_PERMS, permissions)
  const raw = params.get('tab') as TabKey | null
  const tab: TabKey = raw && TAB_KEYS.includes(raw) && (raw !== 'quickbooks' || canQuickBooks) ? raw : 'orders'
  const setTab = (k: TabKey) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete('qb')
        if (k === 'orders') next.delete('tab')
        else next.set('tab', k)
        return next
      },
      { replace: true },
    )
  // Lote 67: el aviso del Pulso «Necesita tu atención» abre la orden directo (?order=<publicId>)
  const [openId, setOpenId] = useState<string | null | 'new'>(() => params.get('order') ?? (params.get('new') ? 'new' : null))
  const navigate = useNavigate()
  const [entryMode, setEntryMode] = useEntryMode()
  const canCreate = useCan('sales.create')
  // un borrador se retoma en el modo de esta persona: con el asistente, en el paso donde iba; si no, en la ventana
  const openOrder = (id: string, status?: string | null) => (status === 'DRAFT' && canCreate && entryMode === 'wizard' ? navigate(wizardPath(id)) : setOpenId(id))
  // Lote 82: devoluciones (la ficha por su id y el editor, opcionalmente sobre una orden)
  const [returnId, setReturnId] = useState<string | null>(null)
  const [newReturn, setNewReturn] = useState<{ orderId: string | null } | null>(null)
  // 2026-11-09: los filtros de la pestaña Órdenes viven aquí para que sobrevivan al cambio de pestaña
  const [orderFilters, setOrderFilters] = useState<OrdersFilterState>(EMPTY_ORDERS_FILTERS)
  return (
    <div className="sales-screen">
      <div className="head">
        <div>
          <h1>{t('sales.title')}</h1>
          <p>{t('sales.subtitle')}</p>
        </div>
        <Can perm="sales.create">
          <div className="act">
            {/* 2026-11-11: UNA sola forma de crear la orden con dos modos (asistente por pasos o formulario completo); se abre el último modo de esta persona
                y dentro del borrador se puede cambiar al otro */}
            <button type="button" className="btn flow" onClick={() => (entryMode === 'wizard' ? navigate(wizardPath()) : setOpenId('new'))}>{t('sales.new')}</button>
            <button type="button" className="btn" onClick={() => { const m = entryMode === 'wizard' ? 'full' : 'wizard'; setEntryMode(m); if (m === 'wizard') navigate(wizardPath()); else setOpenId('new') }}>
              {t(entryMode === 'wizard' ? 'sales.modeFull' : 'sales.modeWizard')}
            </button>
          </div>
        </Can>
      </div>
      <Tabs<TabKey>
        label={t('sales.tabs.label')}
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'orders', label: t('sales.tabs.orders') },
          { key: 'backorders', label: t('sales.tabs.backorders') },
          { key: 'returns', label: t('sales.tabs.returns') },
          ...(canQuickBooks ? [{ key: 'quickbooks' as const, label: t('sales.tabs.quickbooks') }] : []),
        ]}
      />
      {tab === 'orders' ? (
        <OrdersTab onOpen={openOrder} filters={orderFilters} onFilters={setOrderFilters} />
      ) : tab === 'backorders' ? (
        <BackordersTab onOpen={setOpenId} />
      ) : tab === 'quickbooks' ? (
        <QuickBooksTab />
      ) : (
        <ReturnsTab onOpen={setReturnId} onNew={() => setNewReturn({ orderId: null })} />
      )}
      {openId !== null && (
        <OrderModal
          key={openId}
          publicId={openId === 'new' ? null : openId}
          onClose={() => setOpenId(null)}
          onReturn={(orderId) => {
            setOpenId(null)
            setNewReturn({ orderId })
          }}
        />
      )}
      {newReturn && (
        <ReturnEditor
          orderPublicId={newReturn.orderId}
          onClose={() => setNewReturn(null)}
          onSaved={(id) => {
            setNewReturn(null)
            setTab('returns')
            setReturnId(id)
          }}
        />
      )}
      {returnId !== null && <ReturnModal key={returnId} id={returnId} onClose={() => setReturnId(null)} />}
    </div>
  )
}
