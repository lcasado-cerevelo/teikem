// Pestaña «Vehículos» (maqueta `flota()`, panel Vehículos): a la izquierda la lista (contador, buscador `search`, filtros Tipo / Propiedad /
// Combustible / Estatus que van al servidor, «Mostrar inactivos»; fila con código, placa, tipo y chip) y a la derecha la ficha del elegido.
// Maestro-detalle con `SplitPane` (como Clientes). Selección en la URL (`?vehicle=<publicId>`); sin ella, el primero. El API no pagina
// (devuelve todos, ordenados por código), por eso no es un `DataTable`.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { CARDS_QUERY, Chip, EmptyState, FilterScope, IconTruck, Panel, QBox, SelectFilter, Spinner, SplitPane, useMediaQuery } from '../../kernel/ui'
import { useVehicleList, type VehicleListItem, type VehicleListQuery } from './api'
import { FILTER_ANY, vehicleMeta, vehicleState, VehicleStatuses } from './fleetRules'
import { VehicleDetailPanel } from './VehicleDetailPanel'

const NO_ROWS: VehicleListItem[] = []

/** Valor que se devuelve tras `ms` sin cambios (el buscador no pide al servidor en cada tecla). */
function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return v
}

export function VehiclesTab() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState('')
  const [type, setType] = useState(FILTER_ANY)
  const [ownership, setOwnership] = useState(FILTER_ANY)
  const [fuel, setFuel] = useState(FILTER_ANY)
  const [status, setStatus] = useState(FILTER_ANY)
  const [includeInactive, setIncludeInactive] = useState(false)
  const cards = useMediaQuery(CARDS_QUERY)
  const detailRef = useRef<HTMLDivElement>(null)
  const search = useDebounced(q.trim(), 250)
  const { data: types = [] } = useLookups('VehicleType')
  const { data: ownerships = [] } = useLookups('Ownership')
  const { data: fuels = [] } = useLookups('FuelType')

  const query = useMemo<VehicleListQuery>(
    () => ({
      includeInactive: includeInactive || status === VehicleStatuses.Inactive,
      ...(search ? { search } : {}),
      ...(type ? { vehicleType: [type] } : {}),
      ...(ownership ? { ownership: [ownership] } : {}),
      ...(fuel ? { fuelType: [fuel] } : {}),
      ...(status ? { status: [status] } : {}),
    }),
    [includeInactive, search, type, ownership, fuel, status],
  )
  const list = useVehicleList(query)
  const rows = list.data ?? NO_ROWS
  const filtered = Boolean(search || type || ownership || fuel || status)

  const selectedParam = params.get('vehicle')
  // sin `?vehicle=` se abre el primero y se queda ahí: si ese vehículo sale de la lista (se desactiva) la ficha no salta a otro
  const [firstId, setFirstId] = useState<string | null>(null)
  const selected = selectedParam ?? firstId ?? rows[0]?.publicId ?? null
  useEffect(() => {
    if (!selectedParam && !firstId && rows[0]?.publicId) setFirstId(rows[0].publicId)
  }, [selectedParam, firstId, rows])

  const choose = (publicId: string) => {
    const next = new URLSearchParams(params)
    next.set('vehicle', publicId)
    setParams(next, { replace: true })
    // bajo 720 px la ficha queda debajo de la lista: se lleva a la vista
    if (cards) detailRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }

  const options = (list: { code: string; label: string }[]) => list.map((o) => ({ value: o.code, label: o.label }))
  const statusOptions = useMemo(
    () => [
      { value: VehicleStatuses.Active, label: t('fleet.vehicle.available') },
      { value: VehicleStatuses.Maintenance, label: t('fleet.vehicle.maintenance') },
      { value: VehicleStatuses.Inactive, label: t('fleet.vehicle.retired') },
    ],
    [t],
  )

  const chip = (v: VehicleListItem) => {
    switch (vehicleState(v)) {
      case 'retired':
        return <Chip tone="fail">{t('fleet.vehicle.retired')}</Chip>
      case 'inactive':
        return <Chip tone="cap">{t('fleet.vehicle.inactive')}</Chip>
      case 'maintenance':
        return <Chip tone="warn">{t('fleet.vehicle.maintenance')}</Chip>
      default:
        return <Chip tone="deliv">{t('fleet.vehicle.available')}</Chip>
    }
  }

  return (
    <SplitPane storageKey="fleet-vehicles" defaultRatio={0.34} minRatio={0.26} maxRatio={0.5} minPx={[300, 440]} label={t('fleet.vehicle.splitLabel')}>
      <Panel flush icon={<IconTruck />} title={t('fleet.vehicle.list')} badge={list.isPending ? undefined : rows.length}>
        <FilterScope>
          <div className="flt-q">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
            <div className="flt-qf">
              <SelectFilter label={t('fleet.vehicle.type')} value={type} onChange={setType} options={options(types)} allLabel={t('fleet.vehicle.allTypes')} />
              <SelectFilter label={t('fleet.vehicle.ownership')} value={ownership} onChange={setOwnership} options={options(ownerships)} allLabel={t('fleet.vehicle.allOwnerships')} />
              <SelectFilter label={t('fleet.vehicle.fuel')} value={fuel} onChange={setFuel} options={options(fuels)} allLabel={t('fleet.vehicle.allFuels')} />
              <SelectFilter label={t('fleet.vehicle.status')} value={status} onChange={setStatus} options={statusOptions} allLabel={t('fleet.vehicle.allStatuses')} />
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('fleet.vehicle.showInactive')}</span>
            </label>
          </div>
        </FilterScope>
        {list.isError ? (
          <p className="pb ferr" role="alert">
            {applyProblemDetails(list.error).title}
          </p>
        ) : list.isPending ? (
          <Spinner block />
        ) : rows.length === 0 ? (
          <EmptyState icon={<IconTruck />} title={filtered ? t('fleet.vehicle.noResults') : t('fleet.vehicle.emptyList')} />
        ) : (
          <ul className="flt-list" aria-label={t('fleet.vehicle.list')}>
            {rows.map((v) => (
              <li key={v.publicId}>
                <button
                  type="button"
                  className={['unrow', 'flt-row', v.publicId === selected ? 'on' : '', v.isActive ? '' : 'dim'].filter(Boolean).join(' ')}
                  aria-current={v.publicId === selected ? 'true' : undefined}
                  onClick={() => choose(v.publicId)}
                >
                  <span className="flt-main">
                    <span className="flt-top">
                      <span className="ref">{v.code}</span>
                      <b className="flt-plate mono">{v.plateNumber || '—'}</b>
                      <span className="chips">{chip(v)}</span>
                    </span>
                    <span className="meta">{vehicleMeta(v)}</span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Panel>
      {/* ámbito propio: lo que haya en la ficha no depende del buscador de la lista */}
      <div ref={detailRef} className="flt-detail">
        <FilterScope>
          {selected ? (
            <VehicleDetailPanel key={selected} publicId={selected} />
          ) : (
            <Panel>
              <EmptyState icon={<IconTruck />} title={list.isPending ? t('common.loading') : rows.length === 0 && !filtered ? t('fleet.vehicle.noneYet') : t('fleet.vehicle.selectOne')} />
            </Panel>
          )}
        </FilterScope>
      </div>
    </SplitPane>
  )
}
