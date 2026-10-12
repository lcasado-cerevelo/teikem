// Órdenes (Lote F-C1; maqueta `ordenes()`). `/orders`, grupo Operación. Lectura: orders.view + LTL_GROUND (lo aplica la ruta).
// Arriba, el formulario de alta DETALLADA desplegable («Nueva orden» / «Ocultar formulario», `orders.create`); debajo los filtros de la
// maqueta (más Estatus) y la tabla paginada por el servidor con UNA FILA POR TIPO DE PAQUETE (la paginación cuenta órdenes). La
// papelera (`orders.cancel`) solo en el estatus inicial; la flecha abre la orden. `Cotizado` solo con `orders.view_price`.
// Ordenar una columna ordena TODO lo filtrado (`exportRows`, regla del CLAUDE.md).
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  CARDS_QUERY,
  ConfirmDialog,
  DataTable,
  DateRangeFilter,
  Filters,
  IconChevronRight,
  IconPlus,
  IconTrash,
  Panel,
  QBox,
  SearchSelect,
  toast,
  useMediaQuery,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { useClientOptions, useConsignees } from '../consignees/api'
import { TextFilter } from '../warehouse/filterControls'
import { useDebounced } from '../warehouse/lineRules'
import { exportOrders, useDeleteOrder, useOrders, type OrderDetailDto, type OrderListItemDto } from './api'
import { OrderEntryForm } from './OrderEntryForm'
import { buildOrderQuery, canDeleteOrder, EMPTY_ORDER_FILTERS, packageGroups, packageSummaryText, type OrderFilters } from './orderRules'
import './orders.css'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'OrderStatus'
const NO_ROWS: OrderListItemDto[] = []

interface Created {
  publicId: string
  number: string
  confirmed: boolean
}

export default function OrderListScreen() {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const canViewPrice = useCan('orders.view_price')
  const cards = useMediaQuery(CARDS_QUERY)
  const [filters, setFilters] = useState<OrderFilters>(EMPTY_ORDER_FILTERS)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [formOpen, setFormOpen] = useState(false)
  const [created, setCreated] = useState<Created | null>(null)
  const [deleting, setDeleting] = useState<OrderListItemDto | null>(null)
  const remove = useDeleteOrder()

  function patch(next: Partial<OrderFilters>) {
    setFilters((cur) => ({ ...cur, ...next }))
    setPage(1)
  }
  const range: DateRange = { from: filters.from, to: filters.to }

  // ---- Opciones de los filtros ----
  const { data: clients = [] } = useClientOptions(true)
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
  const { data: statuses = [] } = useStatuses(STATUS_DOMAIN)
  // pueblos: los de los consignatarios del directorio (una orden siempre entrega a uno de ellos)
  const { data: consignees = [] } = useConsignees(null, false)
  const clientOptions = useMemo(() => clients.map((c) => ({ value: c.publicId ?? '', label: [c.code, c.name].filter(Boolean).join(' · ') })), [clients])
  const serviceOptions = useMemo(() => serviceTypes.map((s) => ({ value: s.code, label: s.label })), [serviceTypes])
  const packageOptions = useMemo(() => packageTypes.map((p) => ({ value: p.code, label: p.label })), [packageTypes])
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])
  const townOptions = useMemo(() => {
    const names = new Set<string>(filters.cities)
    for (const c of consignees) if (c.city?.trim()) names.add(c.city.trim())
    return [...names].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' })).map((n) => ({ value: n, label: n }))
  }, [consignees, filters.cities])

  // ---- Consulta (los textos esperan 300 ms para no pedir por cada tecla) ----
  const applied = useDebounced(filters, 300)
  const query = useMemo(() => buildOrderQuery(applied, page, pageSize), [applied, page, pageSize])
  const { data, isLoading, error } = useOrders(query)
  const rows = data?.items ?? NO_ROWS

  // ---- Columnas: una fila por tipo de paquete (continuaciones) en tabla; todo junto en tarjetas ----
  const columns = useMemo<DataColumn<OrderListItemDto>[]>(() => {
    const cols: DataColumn<OrderListItemDto>[] = [
      {
        id: 'packBatch',
        header: t('orders.list.columns.packBatch'),
        cell: (o) => <span className="ref oe-small">{o.packBatchNumber || '—'}</span>,
        sortValue: (o) => o.packBatchNumber,
        exportValue: (o) => o.packBatchNumber,
        card: 'hidden',
      },
      {
        id: 'orderNumber',
        header: t('orders.list.columns.orderNumber'),
        cell: (o) => <span className="ref">{o.orderNumber}</span>,
        continuation: () => (
          <span className="oe-cont" title={t('orders.list.columns.continued')}>
            ↳
          </span>
        ),
        sortValue: (o) => o.orderNumber,
        card: 'title',
      },
      { id: 'date', header: t('orders.list.columns.date'), cell: (o) => <span className="mono">{f.date(o.createdAtUtc)}</span>, sortValue: (o) => o.createdAtUtc, exportValue: (o) => f.date(o.createdAtUtc) },
      { id: 'consignee', header: t('orders.list.columns.consignee'), cell: (o) => o.consigneeName, sortValue: (o) => o.consigneeName },
      { id: 'town', header: t('orders.list.columns.town'), cell: (o) => o.consigneeCity, sortValue: (o) => o.consigneeCity },
      { id: 'zip', header: t('orders.list.columns.zip'), cell: (o) => <span className="mono">{o.consigneePostalCode}</span>, sortValue: (o) => o.consigneePostalCode, exportValue: (o) => o.consigneePostalCode },
      { id: 'service', header: t('orders.list.columns.serviceType'), cell: (o) => o.serviceTypeLabel ?? o.serviceType, sortValue: (o) => o.serviceTypeLabel ?? o.serviceType },
      {
        id: 'package',
        header: t('orders.list.columns.package'),
        // tabla: el primer tipo en la fila de la orden y los demás en continuaciones; tarjeta: todos
        cell: (o) => {
          const groups = packageGroups(o)
          return cards ? groups.map((g, i) => <PackageCell key={i} label={g.label} descriptions={g.descriptions} pieces={g.pieces} showPieces />) : <PackageCell label={groups[0].label} descriptions={groups[0].descriptions} />
        },
        continuation: (o, i) => {
          const g = packageGroups(o)[i]
          return g ? <PackageCell label={g.label} descriptions={g.descriptions} /> : null
        },
        sortValue: (o) => packageSummaryText(packageGroups(o)),
        exportValue: (o) => {
          const groups = packageGroups(o)
          return `${packageSummaryText(groups)}${groups.some((g) => g.descriptions.length) ? ` (${groups.flatMap((g) => g.descriptions).join(' · ')})` : ''}`
        },
      },
      {
        id: 'pieces',
        header: t('orders.list.columns.pieces'),
        cell: (o) => (cards ? o.totalPieces : packageGroups(o)[0].pieces),
        continuation: (o, i) => packageGroups(o)[i]?.pieces,
        sortValue: (o) => o.totalPieces,
        exportValue: (o) => o.totalPieces,
        align: 'end',
      },
      {
        id: 'cod',
        header: t('orders.list.columns.cod'),
        cell: (o) => <span className={o.codAmount ? 'cash' : 'cash z'}>{o.codAmount ? f.money(o.codAmount) : '—'}</span>,
        sortValue: (o) => o.codAmount,
        exportValue: (o) => o.codAmount,
        align: 'end',
      },
    ]
    if (canViewPrice)
      cols.push({
        id: 'quoted',
        header: t('orders.list.columns.quoted'),
        cell: (o) => (o.quotedAmount != null ? f.money(o.quotedAmount) : '—'),
        sortValue: (o) => o.quotedAmount,
        exportValue: (o) => o.quotedAmount,
        align: 'end',
      })
    cols.push({
      id: 'status',
      header: t('orders.list.columns.status'),
      cell: (o) => <StatusChip domain={STATUS_DOMAIN} code={o.status} label={o.statusLabel} />,
      sortValue: (o) => o.statusLabel ?? o.status,
      exportValue: (o) => o.statusLabel ?? o.status,
    })
    return cols
  }, [t, f, canViewPrice, cards])

  // la exportación lleva además el cliente y la factura, que la maqueta no muestra en la tabla
  const exportColumns = useMemo<DataColumn<OrderListItemDto>[]>(() => {
    const extra: DataColumn<OrderListItemDto>[] = [
      { id: 'client', header: t('orders.list.columns.client'), cell: (o) => o.clientName, sortValue: (o) => o.clientName },
      { id: 'invoice', header: t('orders.list.filters.invoice'), cell: (o) => o.clientInvoiceNumber, sortValue: (o) => o.clientInvoiceNumber },
    ]
    return [columns[0], columns[1], extra[1], extra[0], ...columns.slice(2)]
  }, [columns, t])

  const actions = useMemo<RowAction<OrderListItemDto>[]>(
    () => [
      { key: 'delete', label: t('orders.list.delete'), icon: <IconTrash />, tone: 'danger', perm: 'orders.cancel', visible: canDeleteOrder, onClick: (o) => setDeleting(o) },
      { key: 'open', label: t('orders.list.open'), icon: <IconChevronRight />, onClick: (o) => navigate(`/orders/${o.publicId}`) },
    ],
    [t, navigate],
  )

  function onCreated(order: OrderDetailDto, confirmed: boolean) {
    setFormOpen(false)
    setCreated({ publicId: order.publicId ?? '', number: order.orderNumber ?? '', confirmed })
  }

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('orders.list.title')}</h1>
          <p>{t('orders.list.subtitle')}</p>
        </div>
        <div className="act">
          {!formOpen && (
            <Can perm="orders.create">
              <button type="button" className="btn flow" onClick={() => { setCreated(null); setFormOpen(true) }}>
                <IconPlus /> {t('orders.list.newOrder')}
              </button>
            </Can>
          )}
        </div>
      </div>

      {formOpen && (
        <Can perm="orders.create">
          <section className="oe-wrap" aria-label={t('orders.entry.title')}>
            <div className="oe-bar">
              <b>
                <IconPlus /> {t('orders.entry.title')}
              </b>
              <button type="button" className="btn sm oe-hide" onClick={() => setFormOpen(false)}>
                {t('orders.list.hideForm')}
              </button>
            </div>
            <OrderEntryForm onCreated={onCreated} />
          </section>
        </Can>
      )}

      {created && (
        <div className="note oe-created" role="status">
          <span>{t(created.confirmed ? 'orders.list.createdConfirmed' : 'orders.list.createdDraft', { number: created.number })}</span>
          <button type="button" className="btn sm" onClick={() => navigate(`/orders/${created.publicId}`)}>
            {t('orders.list.open')}
          </button>
          <button type="button" className="btn sm oe-x" onClick={() => setCreated(null)} aria-label={t('orders.list.dismiss')}>
            ✕
          </button>
        </div>
      )}

      <Filters
        onClear={() => {
          setFilters(EMPTY_ORDER_FILTERS)
          setPage(1)
        }}
      >
        <DateRangeFilter label={t('orders.list.filters.range')} value={range} onChange={(r) => patch({ from: r.from, to: r.to })} />
        <TextFilter label={t('orders.list.filters.orderNumber')} value={filters.orderNumber} onChange={(v) => patch({ orderNumber: v })} />
        <TextFilter label={t('orders.list.filters.consignee')} value={filters.consignee} onChange={(v) => patch({ consignee: v })} />
        <SearchSelect label={t('orders.list.filters.client')} options={clientOptions} value={filters.clientIds} onChange={(v) => patch({ clientIds: v })} placeholder={t('orders.list.filters.all')} />
        <SearchSelect label={t('orders.list.filters.service')} options={serviceOptions} value={filters.serviceTypes} onChange={(v) => patch({ serviceTypes: v })} placeholder={t('orders.list.filters.all')} />
        <SearchSelect label={t('orders.list.filters.package')} options={packageOptions} value={filters.packageTypes} onChange={(v) => patch({ packageTypes: v })} placeholder={t('orders.list.filters.all')} />
        <SearchSelect label={t('orders.list.filters.town')} options={townOptions} value={filters.cities} onChange={(v) => patch({ cities: v })} placeholder={t('orders.list.filters.all')} />
        <TextFilter label={t('orders.list.filters.zip')} value={filters.postalCodePrefix} onChange={(v) => patch({ postalCodePrefix: v })} placeholder={t('orders.list.filters.zipPlaceholder')} />
        <TextFilter label={t('orders.list.filters.invoice')} value={filters.invoice} onChange={(v) => patch({ invoice: v })} placeholder={t('orders.list.filters.invoicePlaceholder')} />
        <TextFilter label={t('orders.list.filters.pack')} value={filters.packBatch} onChange={(v) => patch({ packBatch: v })} placeholder={t('orders.list.filters.packPlaceholder')} />
        <SearchSelect label={t('orders.list.filters.status')} options={statusOptions} value={filters.statuses} onChange={(v) => patch({ statuses: v })} placeholder={t('orders.list.filters.all')} />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('orders.list.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={filters.search} onChange={(v) => patch({ search: v })} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('orders.list.title')}
            columns={columns}
            exportColumns={exportColumns}
            rows={rows}
            rowKey={(o) => o.publicId ?? String(o.id)}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportOrders(query)}
            continuationRows={cards ? undefined : (o) => Math.max(0, packageGroups(o).length - 1)}
            rowActions={actions}
            onRowClick={(o) => navigate(`/orders/${o.publicId}`)}
          />
        )}
      </Panel>

      <ConfirmDialog
        open={deleting !== null}
        tone="danger"
        title={t('orders.list.deleteTitle', { number: deleting?.orderNumber ?? '' })}
        message={t('orders.list.deleteMessage', { number: deleting?.orderNumber ?? '' })}
        confirmLabel={t('orders.list.deleteConfirm')}
        onConfirm={async () => {
          if (!deleting?.publicId) return
          await remove.mutateAsync(deleting.publicId)
          toast.success(t('orders.list.deleted', { number: deleting.orderNumber ?? '' }))
        }}
        onClose={() => setDeleting(null)}
      />
    </div>
  )
}

/** Un tipo de paquete con sus descripciones (y, en tarjeta, sus piezas). */
function PackageCell({ label, descriptions, pieces, showPieces = false }: { label: string; descriptions: string[]; pieces?: number; showPieces?: boolean }) {
  return (
    <div className="oe-pkgcell">
      {label}
      {showPieces && pieces != null ? ` ×${pieces}` : ''}
      {descriptions.length > 0 && <div className="meta oe-desc">{descriptions.join(' · ')}</div>}
    </div>
  )
}
