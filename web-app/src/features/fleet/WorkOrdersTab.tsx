// Pestaña «Órdenes de trabajo» (maqueta `flota()`, panel «Órdenes de trabajo»; manual 04 §6). Columnas de la maqueta (Orden, Vehículo, Tipo,
// Proveedor, Labor, Partes, Total, Estado con los estatus reales) más la fecha; filtros Estado, Tipo, Vehículo y Fechas, buscador y «Nueva
// orden de trabajo». El API no devuelve total: se leen TODAS las órdenes del filtro de a 200 y la tabla ordena y pagina aquí, así el orden vale
// para todo lo filtrado. Un clic en la fila abre la ficha (`?workOrder=<publicId>`) arriba de la lista.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  DataTable,
  DateRangeFilter,
  FilterScope,
  Filters,
  IconChevronRight,
  IconPlus,
  matchesQ,
  Panel,
  QBox,
  SearchSelect,
  SelectFilter,
  Spinner,
  type DataColumn,
  type DateRange,
} from '../../kernel/ui'
import { IconClip } from '../../kernel/ui/screenIcons'
import { useWorkOrderList, WORK_ORDER_STATUS_DOMAIN, type WorkOrderListItem } from './maintenanceApi'
import { buildWorkOrderQuery } from './maintenanceRules'
import { VehicleCell } from './maintenanceShared'
import { useVehicleChoices, vehicleText } from './vehicleChoices'
import { WorkOrderCreateModal } from './WorkOrderCreateModal'
import { WorkOrderDetailPanel } from './WorkOrderDetailPanel'

const NO_ROWS: WorkOrderListItem[] = []
const NO_RANGE: DateRange = { from: '', to: '' }
export const WORK_ORDER_PARAM = 'workOrder'

/** Fecha que se muestra de la orden: la de cierre si ya cerró; si no, la programada. */
const dateOf = (r: WorkOrderListItem): string | null => r.completedDate ?? r.scheduledDate ?? null

export function WorkOrdersTab() {
  const t = useT()
  const f = useFormat()
  const [params, setParams] = useSearchParams()
  const vehicles = useVehicleChoices()
  const { data: statuses = [] } = useStatuses(WORK_ORDER_STATUS_DOMAIN)
  const { data: types = [] } = useLookups('MaintenanceType')
  const [statusCodes, setStatusCodes] = useState<string[]>([])
  const [type, setType] = useState('')
  const [vehicle, setVehicle] = useState('')
  const [range, setRange] = useState<DateRange>(NO_RANGE)
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const detailRef = useRef<HTMLDivElement>(null)
  const selected = params.get(WORK_ORDER_PARAM)

  const query = useMemo(() => buildWorkOrderQuery({ statuses: statusCodes, type, vehicle, from: range.from, to: range.to }), [statusCodes, type, vehicle, range])
  const list = useWorkOrderList(query)
  const rows = useMemo(
    () => (list.data ?? NO_ROWS).filter((r) => matchesQ(q, r.number, r.vehicleCode, vehicles.plateOf(r.vehiclePublicId), r.maintenanceType, r.vendor, r.status, r.scheduleName)),
    [list.data, q, vehicles],
  )

  const open = (publicId: string | null | undefined) => {
    if (!publicId) return
    const next = new URLSearchParams(params)
    next.set(WORK_ORDER_PARAM, publicId)
    setParams(next, { replace: true })
  }
  const closeDetail = () => {
    const next = new URLSearchParams(params)
    next.delete(WORK_ORDER_PARAM)
    setParams(next, { replace: true })
  }
  // al elegir una orden la ficha queda a la vista (está arriba de la lista)
  useEffect(() => {
    if (selected) detailRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }, [selected])

  const columns = useMemo<DataColumn<WorkOrderListItem>[]>(
    () => [
      { id: 'number', header: t('fleet.workOrders.number'), cell: (r) => <span className="ref">{r.number}</span>, sortValue: (r) => r.number ?? '', card: 'title' },
      {
        id: 'vehicle',
        header: t('fleet.workOrders.vehicle'),
        cell: (r) => <VehicleCell plate={vehicles.plateOf(r.vehiclePublicId)} code={r.vehicleCode} />,
        sortValue: (r) => vehicles.plateOf(r.vehiclePublicId) || r.vehicleCode || '',
        exportValue: (r) => vehicleText(vehicles.plateOf(r.vehiclePublicId), r.vehicleCode),
      },
      { id: 'type', header: t('fleet.workOrders.type'), cell: (r) => r.maintenanceType || r.maintenanceTypeCode, sortValue: (r) => r.maintenanceType ?? r.maintenanceTypeCode ?? '' },
      { id: 'vendor', header: t('fleet.workOrders.vendor'), cell: (r) => r.vendor || '—', sortValue: (r) => r.vendor ?? '' },
      {
        id: 'date',
        header: t('fleet.workOrders.date'),
        cell: (r) => <span className="mono">{dateOf(r) ? f.date(dateOf(r) as string) : '—'}</span>,
        exportValue: (r) => (dateOf(r) ? f.date(dateOf(r) as string) : ''),
        sortValue: (r) => dateOf(r) ?? '',
      },
      { id: 'labor', header: t('fleet.workOrders.labor'), align: 'end', cell: (r) => <span className="mono">{r.laborCost != null ? f.money(r.laborCost) : '—'}</span>, exportValue: (r) => r.laborCost ?? '', sortValue: (r) => r.laborCost ?? null },
      { id: 'parts', header: t('fleet.workOrders.parts'), align: 'end', cell: (r) => <span className="mono">{r.partsCost != null ? f.money(r.partsCost) : '—'}</span>, exportValue: (r) => r.partsCost ?? '', sortValue: (r) => r.partsCost ?? null },
      { id: 'total', header: t('fleet.workOrders.total'), align: 'end', cell: (r) => <span className="mono cash">{f.money(r.totalCost ?? 0)}</span>, exportValue: (r) => r.totalCost ?? 0, sortValue: (r) => r.totalCost ?? 0 },
      {
        id: 'status',
        header: t('fleet.workOrders.status'),
        cell: (r) => <StatusChip domain={WORK_ORDER_STATUS_DOMAIN} code={r.statusCode} label={r.status} />,
        exportValue: (r) => r.status ?? r.statusCode ?? '',
        sortValue: (r) => r.status ?? r.statusCode ?? '',
      },
    ],
    [t, f, vehicles],
  )

  const clear = () => {
    setStatusCodes([])
    setType('')
    setVehicle('')
    setRange(NO_RANGE)
    setQ('')
  }
  const filtered = Boolean(q || statusCodes.length || type || vehicle || range.from || range.to)

  return (
    <div className="flt-stack">
      <div ref={detailRef}>{selected && <WorkOrderDetailPanel key={selected} publicId={selected} onClose={closeDetail} />}</div>
      <FilterScope>
        <Panel
          flush
          icon={<IconClip />}
          title={t('fleet.workOrders.title')}
          badge={list.isPending ? undefined : rows.length}
          actions={
            <Can perm="fleet.maintenance">
              <button type="button" className="btn sm flow" onClick={() => setCreating(true)}>
                <IconPlus /> {t('fleet.workOrders.new')}
              </button>
            </Can>
          }
        >
          <div className="pb" style={{ paddingBottom: 0 }}>
            <p className="help" style={{ marginTop: 0 }}>
              {t('fleet.workOrders.help')}
            </p>
            <div className="flt-bar">
              <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
            </div>
            <Filters onClear={clear}>
              <SearchSelect label={t('fleet.workOrders.status')} options={statuses.map((s) => ({ value: s.code, label: s.label }))} value={statusCodes} onChange={setStatusCodes} placeholder={t('fleet.workOrders.allStatuses')} />
              <SelectFilter label={t('fleet.workOrders.type')} value={type} onChange={setType} options={types.map((o) => ({ value: o.code, label: o.label }))} allLabel={t('fleet.workOrders.allTypes')} />
              <SelectFilter label={t('fleet.workOrders.vehicle')} value={vehicle} onChange={setVehicle} options={vehicles.options} allLabel={t('fleet.workOrders.allVehicles')} />
              <DateRangeFilter label={t('fleet.workOrders.dates')} value={range} onChange={setRange} />
            </Filters>
          </div>
          <div className="pb" style={{ paddingTop: 10 }}>
            {list.isError ? (
              <p className="ferr" role="alert">
                {applyProblemDetails(list.error).title}
              </p>
            ) : list.isPending ? (
              <Spinner block />
            ) : (
              <DataTable
                label={t('fleet.workOrders.title')}
                columns={columns}
                rows={rows}
                rowKey={(r) => r.publicId ?? String(r.id)}
                onRowClick={(r) => open(r.publicId)}
                empty={filtered ? t('fleet.workOrders.noResults') : t('fleet.workOrders.empty')}
                rowActions={[{ key: 'open', label: t('fleet.workOrders.open'), icon: <IconChevronRight />, onClick: (r) => open(r.publicId) }]}
              />
            )}
          </div>
        </Panel>
      </FilterScope>
      {creating && <WorkOrderCreateModal onClose={() => setCreating(false)} onCreated={(publicId) => open(publicId)} />}
    </div>
  )
}
