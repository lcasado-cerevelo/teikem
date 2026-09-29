// Pantalla E (Lote F6) — Consulta de órdenes (solo lectura). `/orders`, grupo Operación. Lectura: orders.view + LTL_GROUND
// (aplicado por la ruta). Este lote no da de alta ni edita órdenes: sin botón 'Nuevo' ni acciones de fila.
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { parseApiDate } from '../../kernel/api/dates'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
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
import { useOrdersReadonly, type OrderListItemDto } from '../warehouse/api'
import { IconLayers } from '../../kernel/ui/screenIcons'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'OrderStatus'

function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
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
  const lang = useLang()
  const navigate = useNavigate()
  const [clientId, setClientId] = useState<string | null>(null)
  const [status, setStatus] = useState('')
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)

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
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [clientId, status, range, search, page],
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
      { id: 'cod', header: t('orders.list.columns.cod'), cell: (o) => (o.codAmount != null ? o.codAmount.toFixed(2) : '—'), sortValue: (o) => o.codAmount, align: 'end' },
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
          <label>{t('orders.list.filters.client')}</label>
          <ClientPicker value={clientId} onChange={changeClient} includeInactive />
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
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            onRowClick={(o) => navigate(`/orders/${o.publicId}`)}
          />
        )}
      </Panel>
    </div>
  )
}
