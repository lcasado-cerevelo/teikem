// Pieza "Recolección y empaque" (Lote 13) — panel derecho "Recolecciones" de `PickBatchListScreen` (maqueta `picking()`).
// Filtros dentro del panel, todos al API y a la página 1: Recolectada (rango), Estatus, Producto (`ProductMultiFilter` con
// inactivos), No. de orden (`OrderNumberFilter`: sugiere números existentes), No. de factura, Incluir eliminadas; y el
// `QBox` de la maqueta (búsqueda libre al API, se aplica después de los filtros). Tipo (2026-10-11): Todos / Empaques /
// Despachos manuales (`kind=ALL|PACK|MANUAL`): la lista trae TODO y los despachos manuales (DMA, sin empaque) llevan su motivo en la
// columna Tipo y estatus «Despachado». Tabla paginada en el servidor con 6
// columnas (Número con su estatus, Tipo, Productos "SKU ×cant", Orden · Factura, Cliente, Recolectada); en tarjetas si el panel
// mide menos de 640 px (`useElementWidth`). Acciones de fila con ícono: Empacar (warehouse.pick + orders.create, con
// `canPack`) → `PackModal`; Eliminar (warehouse.pick, y orders.cancel si está empacada; un manual exige warehouse.issue; con `canDelete`) →
// `DeletePickBatchDialog`. Clic en la fila → `PickBatchDetailModal`. `highlight` = recién recolectada (fondo de flujo).
import { useMemo, useRef, useState } from 'react'
import { useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Filters,
  IconBox,
  IconDoc,
  IconTrash,
  Panel,
  QBox,
  SearchSelect,
  SelectFilter,
  useElementWidth,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import { exportPickBatches, usePickBatches, type PickBatchDto } from './api'
import { DeletePickBatchDialog } from './DeletePickBatchDialog'
import { TextFilter, ToggleFilter } from './filterControls'
import { formatDateTime, useDebounced } from './lineRules'
import { OrderNumberFilter } from './OrderNumberFilter'
import { PackModal } from './PackModal'
import { PickBatchStatusChip } from './PickBatchStatusChip'
import { PickBatchDetailModal } from './PickBatchDetailModal'
import { batchProductsText, PICK_BATCH_STATUS_DOMAIN, usePickBatchCanDelete } from './pickBatchView'
import { ProductMultiFilter, type ProductFilterItem } from './pickers'
import './warehouse.css'

const PAGE_SIZE = 25
const NO_ROWS: never[] = []
/** Bajo este ancho del panel la tabla pasa a tarjetas. */
const CARDS_BELOW_PX = 640

export interface PickBatchesPanelProps {
  /** publicId de la recolección recién creada: se resalta y la lista vuelve a la página 1. */
  highlight?: string | null
}

export function PickBatchesPanel({ highlight }: PickBatchesPanelProps) {
  const t = useT()
  const lang = useLang()
  const canDelete = usePickBatchCanDelete()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [status, setStatus] = useState<string[]>([])
  // Tipo: '' = todo; PACK = recolecciones de empaque; MANUAL = despachos manuales (DMA). Va al API como `kind`.
  const [kind, setKind] = useState('')
  const [products, setProducts] = useState<ProductFilterItem[]>([])
  const [orderNumber, setOrderNumber] = useState('')
  const [invoiceNumber, setInvoiceNumber] = useState('')
  const [includeDeleted, setIncludeDeleted] = useState(false)
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [detail, setDetail] = useState<string | null>(null)
  const [packing, setPacking] = useState<PickBatchDto | null>(null)
  const [deleting, setDeleting] = useState<PickBatchDto | null>(null)
  const search = useDebounced(q.trim())
  const order = useDebounced(orderNumber.trim())
  const invoice = useDebounced(invoiceNumber.trim())

  // una recolección nueva va primero (el servidor ordena por fecha descendente): se vuelve a la página 1 para verla
  // (ajuste de estado al cambiar la prop, durante el render: sin efecto)
  const [seenHighlight, setSeenHighlight] = useState(highlight)
  if (highlight !== seenHighlight) {
    setSeenHighlight(highlight)
    if (highlight) setPage(1)
  }

  const { data: statuses = [] } = useStatuses(PICK_BATCH_STATUS_DOMAIN)
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])

  function reset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

  const query = useMemo(
    () => ({
      from: range.from || undefined,
      to: range.to || undefined,
      productPublicIds: products.length > 0 ? products.map((p) => p.publicId) : undefined,
      status: status.length > 0 ? status : undefined,
      orderNumber: order || undefined,
      invoiceNumber: invoice || undefined,
      search: search || undefined,
      includeDeleted: includeDeleted || undefined,
      kind: kind || undefined,
      skip: (page - 1) * pageSize,
      take: pageSize,
    }),
    [range, products, status, kind, order, invoice, search, includeDeleted, page, pageSize],
  )
  const { data, isLoading, error } = usePickBatches(query)

  const columns = useMemo<DataColumn<PickBatchDto>[]>(
    () => [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      {
        id: 'number',
        header: t('warehouse.pickBatches.columns.number'),
        cell: (b) => (
          <span className="collect-num">
            <span className="ref">{b.number}</span>
            <PickBatchStatusChip batch={b} />
            {b.isActive === false && !b.isManual && <Chip tone="fail">{t('warehouse.pickBatches.deletedChip')}</Chip>}
          </span>
        ),
        sortValue: (b) => b.number,
        exportValue: (b) => [b.number, b.status ?? b.statusCode, b.isActive === false ? t('warehouse.pickBatches.deletedChip') : null].filter(Boolean).join(' · '),
        card: 'title',
      },
      {
        id: 'kind',
        header: t('warehouse.pickBatches.columns.kind'),
        cell: (b) => (b.isManual ? <span>{t('warehouse.pickBatches.kinds.manual')} · {b.reasonLabel ?? b.reasonCode}</span> : t('warehouse.pickBatches.kinds.pack')),
        sortValue: (b) => (b.isManual ? `1 ${b.reasonLabel ?? ''}` : '0'),
        exportValue: (b) => (b.isManual ? `${t('warehouse.pickBatches.kinds.manual')} · ${b.reasonLabel ?? b.reasonCode ?? ''}` : t('warehouse.pickBatches.kinds.pack')),
      },
      {
        id: 'products',
        header: t('warehouse.pickBatches.columns.products'),
        cell: (b) => <span className="collect-prods">{batchProductsText(b, lang) || '—'}</span>,
        sortValue: (b) => batchProductsText(b, lang),
      },
      { id: 'numbers', header: t('warehouse.pickBatches.columns.numbers'), cell: (b) => b.displayNumbers ?? '—', sortValue: (b) => b.displayNumbers },
      {
        id: 'client',
        header: t('warehouse.pickBatches.columns.client'),
        cell: (b) => b.clientName ?? <span className="inv-own">{t('warehouse.pickBatches.own')}</span>,
        sortValue: (b) => b.clientName ?? t('warehouse.pickBatches.own'),
        exportValue: (b) => b.clientName ?? t('warehouse.pickBatches.own'),
      },
      {
        id: 'collectedAt',
        header: t('warehouse.pickBatches.columns.collectedAt'),
        cell: (b) => formatDateTime(b.collectedAtUtc, lang),
        sortValue: (b) => b.collectedAtUtc,
      },
    ],
    [t, lang],
  )

  const rowActions = useMemo<RowAction<PickBatchDto>[]>(
    () => [
      {
        key: 'pack',
        label: t('warehouse.pickBatches.detail.pack'),
        icon: <IconBox />,
        tone: 'flow',
        perm: ['warehouse.pick', 'orders.create'],
        visible: (b) => b.canPack === true,
        onClick: (b) => setPacking(b),
      },
      {
        key: 'delete',
        label: t('warehouse.pickBatches.detail.delete'),
        icon: <IconTrash />,
        tone: 'danger',
        // permiso dentro de canDelete: warehouse.pick (empaque) o warehouse.issue (despacho manual)
        visible: (b) => canDelete(b),
        onClick: (b) => setDeleting(b),
      },
    ],
    [t, canDelete],
  )

  return (
    <div ref={boxRef} className="collect-list">
      <Panel flush icon={<IconDoc />} title={t('warehouse.pickBatches.batchesTitle')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="collect-filters">
          <Filters
            onClear={() => {
              setPage(1)
              setRange(EMPTY_RANGE)
              setStatus([])
              setKind('')
              setProducts([])
              setOrderNumber('')
              setInvoiceNumber('')
              setIncludeDeleted(false)
              setQ('')
            }}
          >
            <DateRangeFilter label={t('warehouse.pickBatches.filters.collected')} value={range} onChange={reset(setRange)} />
            <SelectFilter
              label={t('warehouse.pickBatches.filters.kind')}
              value={kind}
              onChange={reset(setKind)}
              options={[
                { value: 'PACK', label: t('warehouse.pickBatches.filters.kindPack') },
                { value: 'MANUAL', label: t('warehouse.pickBatches.filters.kindManual') },
              ]}
            />
            <SearchSelect label={t('warehouse.pickBatches.filters.status')} options={statusOptions} value={status} onChange={reset(setStatus)} />
            <ProductMultiFilter label={t('warehouse.pickBatches.filters.product')} value={products} onChange={reset(setProducts)} includeInactive />
            <OrderNumberFilter label={t('warehouse.pickBatches.filters.orderNumber')} value={orderNumber} onChange={reset(setOrderNumber)} />
            <TextFilter label={t('warehouse.pickBatches.filters.invoiceNumber')} value={invoiceNumber} onChange={reset(setInvoiceNumber)} />
            <ToggleFilter label={t('warehouse.pickBatches.filters.includeDeleted')} checked={includeDeleted} onChange={reset(setIncludeDeleted)} />
          </Filters>
        </div>
        <div className="qrow">
          <QBox value={q} onChange={reset(setQ)} placeholder={t('warehouse.pickBatches.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.pickBatches.batchesTitle')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(b) => b.publicId ?? String(b.id)}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportPickBatches(query)}
            forceCards={width > 0 && width < CARDS_BELOW_PX}
            onRowClick={(b) => setDetail(b.publicId ?? null)}
            rowClassName={(b) => (b.publicId === highlight ? 'collect-new' : b.isActive === false ? 'dim' : undefined)}
            rowActions={rowActions}
          />
        )}
      </Panel>

      <PickBatchDetailModal publicId={detail} onClose={() => setDetail(null)} />
      {packing && <PackModal batch={packing} onClose={() => setPacking(null)} />}
      <DeletePickBatchDialog batch={deleting} onClose={() => setDeleting(null)} />
    </div>
  )
}
