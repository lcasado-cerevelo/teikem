// Pestaña «Combustible» (maqueta `flota()`, panel «Bitácora de combustible»; manual 04 §7). Tabla paginada por el SERVIDOR (skip/take, con total)
// con las columnas de la maqueta (Fecha, Vehículo, Litros, Costo, Km recorridos, Km/L, Costo/km) más Chofer y Estación; el km/L y el costo/km los
// calcula el servidor. Filtros Vehículo, Chofer y Fechas; arriba, el resumen por vehículo de TODO el filtro. Ordenar o exportar lee el filtro
// completo (`exportRows`), no solo la página. «Registrar carga», «Editar» y «Quitar» exigen `fleet.maintenance`; las cargas no se borran.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  ConfirmDialog,
  DataTable,
  DateRangeFilter,
  FilterScope,
  Filters,
  IconEdit,
  IconPlus,
  IconTrash,
  Panel,
  SelectFilter,
  Spinner,
  toast,
  type DataColumn,
  type DateRange,
} from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import { useDrivers } from '../drivers/api'
import { FuelLogModal } from './FuelLogModal'
import { readAllFuelLogs, useFuelLogs, useFuelMutations, type FuelLog, type FuelVehicleSummary } from './maintenanceApi'
import { buildFuelQuery } from './maintenanceRules'
import { VehicleCell } from './maintenanceShared'
import { useVehicleChoices, vehicleText } from './vehicleChoices'

const NO_ROWS: FuelLog[] = []
const NO_SUMMARY: FuelVehicleSummary[] = []
const NO_RANGE: DateRange = { from: '', to: '' }

export function FuelTab() {
  const t = useT()
  const f = useFormat()
  const canMaintain = useCan('fleet.maintenance')
  const vehicles = useVehicleChoices()
  const drivers = useDrivers(true)
  const mutations = useFuelMutations()
  const [vehicle, setVehicle] = useState('')
  const [driver, setDriver] = useState('')
  const [range, setRange] = useState<DateRange>(NO_RANGE)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [editing, setEditing] = useState<{ log: FuelLog | null } | null>(null)
  const [removing, setRemoving] = useState<FuelLog | null>(null)

  // el filtro sin paginar (Exportar y «ordenar todo lo filtrado» lo leen completo) y la página que se pinta
  const base = useMemo(() => buildFuelQuery({ vehicle, driver, from: range.from, to: range.to }), [vehicle, driver, range])
  const query = useMemo(() => ({ ...base, skip: (page - 1) * pageSize, take: pageSize }), [base, page, pageSize])
  const list = useFuelLogs(query)
  const rows = list.data?.items ?? NO_ROWS
  const summary = list.data?.summary ?? NO_SUMMARY
  const total = list.data?.total ?? 0

  // si se quita la última carga de la última página, se vuelve a la anterior
  useEffect(() => {
    if (list.data && (list.data.items?.length ?? 0) === 0 && page > 1) setPage(page - 1)
  }, [list.data, page])

  const filter = <T,>(setter: (v: T) => void) => (v: T) => {
    setter(v)
    setPage(1)
  }

  const driverOptions = (drivers.data ?? []).map((d) => ({ value: d.publicId ?? '', label: [d.code, d.fullName].filter(Boolean).join(' · ') }))
  const filtered = Boolean(vehicle || driver || range.from || range.to)

  const columns = useMemo<DataColumn<FuelLog>[]>(
    () => [
      { id: 'date', header: t('fleet.fuel.date'), cell: (r) => <span className="mono">{f.dateTime(r.fillDateUtc)}</span>, exportValue: (r) => f.dateTime(r.fillDateUtc), sortValue: (r) => r.fillDateUtc ?? '', card: 'title' },
      {
        id: 'vehicle',
        header: t('fleet.fuel.vehicle'),
        cell: (r) => <VehicleCell plate={vehicles.plateOf(r.vehiclePublicId)} code={r.vehicleCode} />,
        exportValue: (r) => vehicleText(vehicles.plateOf(r.vehiclePublicId), r.vehicleCode),
        sortValue: (r) => vehicles.plateOf(r.vehiclePublicId) || r.vehicleCode || '',
      },
      { id: 'driver', header: t('fleet.fuel.driver'), cell: (r) => r.driverName || '—', sortValue: (r) => r.driverName ?? '' },
      { id: 'station', header: t('fleet.fuel.station'), cell: (r) => r.station || '—', sortValue: (r) => r.station ?? '' },
      { id: 'liters', header: t('fleet.fuel.liters'), align: 'end', cell: (r) => <span className="mono">{f.number(r.liters ?? 0)}</span>, exportValue: (r) => r.liters ?? 0, sortValue: (r) => r.liters ?? 0 },
      { id: 'cost', header: t('fleet.fuel.cost'), align: 'end', cell: (r) => <span className="mono">{f.money(r.totalCost ?? 0)}</span>, exportValue: (r) => r.totalCost ?? 0, sortValue: (r) => r.totalCost ?? 0 },
      { id: 'distance', header: t('fleet.fuel.distance'), align: 'end', cell: (r) => <span className="mono">{r.distanceKm != null ? f.number(r.distanceKm) : '—'}</span>, exportValue: (r) => r.distanceKm ?? '', sortValue: (r) => r.distanceKm ?? null },
      { id: 'kml', header: t('fleet.fuel.kmPerLiter'), align: 'end', cell: (r) => <span className="mono">{r.kmPerLiter != null ? f.number(r.kmPerLiter, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—'}</span>, exportValue: (r) => r.kmPerLiter ?? '', sortValue: (r) => r.kmPerLiter ?? null },
      { id: 'costkm', header: t('fleet.fuel.costPerKm'), align: 'end', cell: (r) => <span className="mono">{r.costPerKm != null ? f.money(r.costPerKm) : '—'}</span>, exportValue: (r) => r.costPerKm ?? '', sortValue: (r) => r.costPerKm ?? null },
    ],
    [t, f, vehicles],
  )

  const summaryColumns = useMemo<DataColumn<FuelVehicleSummary>[]>(
    () => [
      {
        id: 'vehicle',
        header: t('fleet.fuel.vehicle'),
        cell: (r) => <VehicleCell plate={vehicles.plateOf(r.vehiclePublicId)} code={r.vehicleCode} />,
        sortValue: (r) => vehicles.plateOf(r.vehiclePublicId) || r.vehicleCode || '',
        card: 'title',
      },
      { id: 'fills', header: t('fleet.fuel.fills'), align: 'end', cell: (r) => <span className="mono">{f.number(r.fills ?? 0)}</span>, sortValue: (r) => r.fills ?? 0 },
      { id: 'liters', header: t('fleet.fuel.liters'), align: 'end', cell: (r) => <span className="mono">{f.number(r.totalLiters ?? 0)}</span>, sortValue: (r) => r.totalLiters ?? 0 },
      { id: 'cost', header: t('fleet.fuel.cost'), align: 'end', cell: (r) => <span className="mono">{f.money(r.totalCost ?? 0)}</span>, sortValue: (r) => r.totalCost ?? 0 },
      { id: 'distance', header: t('fleet.fuel.distance'), align: 'end', cell: (r) => <span className="mono">{r.distanceKm != null ? f.number(r.distanceKm) : '—'}</span>, sortValue: (r) => r.distanceKm ?? null },
      { id: 'kml', header: t('fleet.fuel.kmPerLiter'), align: 'end', cell: (r) => <span className="mono">{r.kmPerLiter != null ? f.number(r.kmPerLiter, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—'}</span>, sortValue: (r) => r.kmPerLiter ?? null },
      { id: 'costkm', header: t('fleet.fuel.costPerKm'), align: 'end', cell: (r) => <span className="mono">{r.costPerKm != null ? f.money(r.costPerKm) : '—'}</span>, sortValue: (r) => r.costPerKm ?? null },
    ],
    [t, f, vehicles],
  )

  const clear = () => {
    setVehicle('')
    setDriver('')
    setRange(NO_RANGE)
    setPage(1)
  }

  return (
    <div className="flt-stack">
      <FilterScope>
        <Panel
          flush
          icon={<IconCash />}
          title={t('fleet.fuel.title')}
          badge={list.isPending ? undefined : total}
          actions={
            <Can perm="fleet.maintenance">
              <button type="button" className="btn sm flow" onClick={() => setEditing({ log: null })}>
                <IconPlus /> {t('fleet.fuel.register')}
              </button>
            </Can>
          }
        >
          <div className="pb" style={{ paddingBottom: 0 }}>
            <p className="help" style={{ marginTop: 0 }}>
              {t('fleet.fuel.help')}
            </p>
            <Filters onClear={clear}>
              <SelectFilter label={t('fleet.fuel.vehicle')} value={vehicle} onChange={filter(setVehicle)} options={vehicles.options} allLabel={t('fleet.fuel.allVehicles')} />
              <SelectFilter label={t('fleet.fuel.driver')} value={driver} onChange={filter(setDriver)} options={driverOptions} allLabel={t('fleet.fuel.allDrivers')} />
              <DateRangeFilter label={t('fleet.fuel.dates')} value={range} onChange={filter(setRange)} />
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
                label={t('fleet.fuel.title')}
                columns={columns}
                rows={rows}
                rowKey={(r) => String(r.id)}
                page={page}
                pageSize={pageSize}
                total={total}
                onPage={setPage}
                onPageSize={(n) => {
                  setPageSize(n)
                  setPage(1)
                }}
                exportRows={() => readAllFuelLogs(base)}
                empty={filtered ? t('fleet.fuel.noResults') : t('fleet.fuel.empty')}
                rowActions={[
                  { key: 'edit', label: t('fleet.edit'), icon: <IconEdit />, onClick: (r) => setEditing({ log: r }), visible: () => canMaintain },
                  { key: 'remove', label: t('fleet.fuel.remove'), icon: <IconTrash />, tone: 'danger', onClick: (r) => setRemoving(r), visible: () => canMaintain },
                ]}
              />
            )}
          </div>
        </Panel>
      </FilterScope>

      <FilterScope>
        <Panel flush icon={<IconCash />} title={t('fleet.fuel.summary.title')} badge={list.isPending ? undefined : summary.length}>
          <div className="pb">
            <p className="help" style={{ marginTop: 0 }}>
              {t('fleet.fuel.summary.help')}
            </p>
            <DataTable
              label={t('fleet.fuel.summary.title')}
              columns={summaryColumns}
              rows={summary}
              rowKey={(r) => r.vehiclePublicId ?? r.vehicleCode ?? ''}
              pagination={false}
              exportable={false}
              empty={t('fleet.fuel.summary.empty')}
            />
          </div>
        </Panel>
      </FilterScope>

      {editing && <FuelLogModal key={editing.log?.id ?? 'new'} log={editing.log} vehicles={vehicles} defaultVehicle={vehicle || undefined} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('fleet.fuel.removeTitle')}
        message={t('fleet.fuel.removeBody', {
          liters: removing ? f.number(removing.liters ?? 0) : '',
          date: removing ? f.date(removing.fillDateUtc) : '',
          vehicle: removing ? vehicleText(vehicles.plateOf(removing.vehiclePublicId), removing.vehicleCode) : '',
        })}
        confirmLabel={t('fleet.fuel.remove')}
        onConfirm={async () => {
          if (!removing) return
          await mutations.remove.mutateAsync(removing.id ?? 0)
          toast.success(t('fleet.fuel.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </div>
  )
}
