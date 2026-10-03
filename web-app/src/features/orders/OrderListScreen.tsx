// Pantalla E (Lote F6) — Consulta de órdenes (solo lectura). `/orders`, grupo Operación. Lectura: orders.view + LTL_GROUND
// (aplicado por la ruta). Este lote no da de alta ni edita órdenes: sin botón 'Nuevo' ni acciones de fila.
import { useEffect, useId, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { formatMoney, useLang, useT } from '../../kernel/i18n'
import {
  ClientPicker,
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Filters,
  Panel,
  QBox,
  SelectFilter,
  type DataColumn,
  type DateRange,
} from '../../kernel/ui'
import { exportOrders, useOrdersReadonly, type OrderListItemDto } from '../warehouse/api'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { formatDateTime as formatCompanyDateTime } from '../../kernel/format'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'OrderStatus'

function formatDateTime(iso: string | null | undefined, lang: string): string {
  // fecha corta y hora de la compañía (Región y formatos), en su zona
  return formatCompanyDateTime(iso, lang)
}

/** `to` del filtro es exclusivo sobre CreatedAtUtc en el servidor: se manda el día siguiente para incluir el día elegido. */
function exclusiveTo(day: string): string | undefined {
  if (!day) return undefined
  const d = new Date(`${day}T00:00:00Z`)
  if (Number.isNaN(d.getTime())) return undefined
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString().slice(0, 10)
}

export default function OrderListScreen() {
  const t = useT()
  const clientFieldId = useId()
  const lang = useLang()
  const navigate = useNavigate()
  const [clientId, setClientId] = useState<string | null>(null)
  const [status, setStatus] = useState('')
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)

  useEffect(() => {
    const h = setTimeout(() => {
      setSearch(text.trim())
      setPage(1)
    }, 250)
    return () => clearTimeout(h)
  }, [text])

  const { data: statusOptions = [] } = useStatuses(STATUS_DOMAIN)
  const statusSelectOptions = useMemo(() => statusOptions.map((s) => ({ value: s.code, label: s.label })), [statusOptions])

  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }
  const changeClient = withPageReset(setClientId)
  const changeStatus = withPageReset(setStatus)
  const changeRange = withPageReset(setRange)

  const query = useMemo(
    () => ({
      clientId: clientId || undefined,
      status: status || undefined,
      from: range.from || undefined,
      to: exclusiveTo(range.to),
      search: search || undefined,
      skip: (page - 1) * pageSize,
      take: pageSize,
    }),
    [clientId, status, range, search, page, pageSize],
  )
  const { data, isLoading, error } = useOrdersReadonly(query)

  const columns = useMemo<DataColumn<OrderListItemDto>[]>(
    () => [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'orderNumber', header: t('orders.list.columns.orderNumber'), cell: (o) => <span className="ref">{o.orderNumber}</span>, sortValue: (o) => o.orderNumber, card: 'title' },
      { id: 'client', header: t('orders.list.columns.client'), cell: (o) => o.clientName, sortValue: (o) => o.clientName },
      {
        id: 'consignee',
        header: t('orders.list.columns.consignee'),
        cell: (o) => [o.consigneeName, o.consigneeCity].filter(Boolean).join(' · '),
        sortValue: (o) => o.consigneeName,
      },
      { id: 'serviceType', header: t('orders.list.columns.serviceType'), cell: (o) => o.serviceTypeLabel, sortValue: (o) => o.serviceTypeLabel ?? o.serviceType },
      {
        id: 'status',
        header: t('orders.list.columns.status'),
        cell: (o) => <StatusChip domain={STATUS_DOMAIN} code={o.status} label={o.statusLabel} />,
        sortValue: (o) => o.statusLabel ?? o.status,
      },
      { id: 'pieces', header: t('orders.list.columns.pieces'), cell: (o) => o.totalPieces, sortValue: (o) => o.totalPieces, align: 'end' },
      { id: 'cod', header: t('orders.list.columns.cod'), cell: (o) => (o.codAmount != null ? formatMoney(o.codAmount, lang) : '—'), exportValue: (o) => o.codAmount, sortValue: (o) => o.codAmount, align: 'end' },
      {
        id: 'createdAt',
        header: t('orders.list.columns.createdAt'),
        cell: (o) => formatDateTime(o.createdAtUtc, lang),
        sortValue: (o) => o.createdAtUtc,
        card: 'hidden',
      },
    ],
    [t, lang],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('orders.list.title')}</h1>
          <p>{t('orders.list.subtitle')}</p>
        </div>
      </div>

      <Filters
        onClear={() => {
          setClientId(null)
          setStatus('')
          setRange(EMPTY_RANGE)
          setText('')
        }}
      >
        <div className="f">
          <label htmlFor={clientFieldId}>{t('orders.list.filters.client')}</label>
          <ClientPicker id={clientFieldId} value={clientId} onChange={changeClient} includeInactive filterLabel={t('orders.list.filters.client')} />
        </div>
        <SelectFilter
          label={t('orders.list.filters.status')}
          value={status}
          onChange={changeStatus}
          options={statusSelectOptions}
          allLabel={t('orders.list.filters.anyStatus')}
        />
        <DateRangeFilter label={t('orders.list.filters.range')} value={range} onChange={changeRange} />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('orders.list.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('orders.list.title')}
            columns={columns}
            rows={data?.items ?? []}
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
            onRowClick={(o) => navigate(`/orders/${o.publicId}`)}
          />
        )}
      </Panel>
    </div>
  )
}
