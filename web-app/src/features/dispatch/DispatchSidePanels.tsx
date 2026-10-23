// Lote F-E2a — Los paneles de apoyo de la Sala de despacho: «Choferes» (código, nombre, zona y «≤ máx. de paradas») y «Rutas de hoy» (tarjetas
// con referencia, chofer, zona, estatus, barra de avance y ⚠ si pasa el máximo de paradas).
import { StatusChip } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { IconAlert } from '../../kernel/ui/icons'
import { Chip, EmptyState, IconRoute, IconUsers, Panel, Spinner } from '../../kernel/ui'
import type { components } from '../../kernel/api/schema'
import type { DriverAvailability } from './fleetChoices'
import { percent, showOverStopLimit, type MonitorTrip, type TripListItem } from './dispatchRules'

type DriverListItem = components['schemas']['DriverListItemDto']

export function DriversPanel({
  drivers,
  loading,
  failed,
  allowed,
  availability,
}: {
  drivers: readonly DriverListItem[]
  loading: boolean
  failed: boolean
  /** `fleet.view` + CATALOG: sin ellos el panel lo explica en lugar de romper. */
  allowed: boolean
  availability: readonly DriverAvailability[]
}) {
  const t = useT()
  const unavailable = new Map(availability.filter((a) => a.available === false).map((a) => [a.publicId, a]))
  return (
    <Panel icon={<IconUsers />} title={t('dispatch.drivers.title')} badge={allowed && !failed ? drivers.length : undefined}>
      {!allowed ? (
        <p className="note" role="status">
          {t('dispatch.drivers.noAccess')}
        </p>
      ) : loading && drivers.length === 0 ? (
        <Spinner block label={t('common.loading')} />
      ) : failed ? (
        <EmptyState title={t('dispatch.drivers.error')} />
      ) : drivers.length === 0 ? (
        <EmptyState icon={<IconUsers />} title={t('dispatch.drivers.empty')} />
      ) : (
        <ul className="disp-drivers" style={{ listStyle: 'none', margin: 0, padding: 0 }} aria-label={t('dispatch.drivers.title')}>
          {drivers.map((d) => {
            const off = unavailable.get(d.publicId)
            const reasons = (off?.issues ?? []).filter((i) => i.blocking).map((i) => i.message).filter(Boolean).join(' ')
            return (
              <li key={d.publicId} className={d.zoneCode ? 'disp-driver' : 'disp-driver nozone'}>
                <span className="dr">{d.code}</span>
                <div className="body">
                  <b>{d.fullName}</b>
                  <div>{[d.area || t('dispatch.drivers.available'), d.zoneCode].filter(Boolean).join(' · ')}</div>
                </div>
                {off && (
                  <Chip tone="fail" title={reasons || undefined}>
                    {t('dispatch.drivers.unavailable')}
                  </Chip>
                )}
                <span className="max" title={t('dispatch.drivers.maxStops')}>
                  ≤{d.effectiveMaxStops ?? '—'}
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </Panel>
  )
}

export function TodayRoutesPanel({
  trips,
  loading,
  failed,
  selectedId,
  progress,
  onSelect,
}: {
  trips: readonly TripListItem[]
  loading: boolean
  failed: boolean
  selectedId: string | null
  /** Avance por ruta (del monitor): paradas atendidas. Sin dato, la barra queda en cero. */
  progress: ReadonlyMap<string, MonitorTrip>
  onSelect: (publicId: string) => void
}) {
  const t = useT()
  return (
    <Panel icon={<IconRoute />} title={t('dispatch.today.title')} badge={failed ? undefined : trips.length} flush>
      {loading && trips.length === 0 ? (
        <Spinner block label={t('common.loading')} />
      ) : failed ? (
        <EmptyState title={t('dispatch.today.error')} />
      ) : trips.length === 0 ? (
        <EmptyState icon={<IconRoute />} title={t('dispatch.today.empty')} />
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }} aria-label={t('dispatch.today.title')}>
          {trips.map((r) => {
            const id = r.publicId ?? ''
            const p = progress.get(id)
            const pct = p ? percent((p.completedStops ?? 0) + (p.failedStops ?? 0), p.totalStops ?? r.stopCount ?? 0) : 0
            return (
              <li key={id}>
              <button
                type="button"
                className={id === selectedId ? 'routecard on' : 'routecard'}
                aria-current={id === selectedId ? 'true' : undefined}
                onClick={() => onSelect(id)}
              >
                <div className="rt">
                  <div className="rr">
                    <span className="ref">{r.code}</span>
                    {r.driverCode && <span className="dr">{r.driverCode}</span>}
                    {r.zoneCode && <span className="tag">{r.zoneCode}</span>}
                    <StatusChip domain="TripStatus" code={r.statusCode} label={r.status} />
                  </div>
                  <div className="bar" aria-hidden="true">
                    <i style={{ width: `${pct}%` }} />
                  </div>
                </div>
                {showOverStopLimit(r.overStopLimit, r.statusCode) && (
                  <span className="warn" title={t('dispatch.overLimitShort')} role="img" aria-label={t('dispatch.overLimitShort')}>
                    <IconAlert />
                  </span>
                )}
              </button>
              </li>
            )
          })}
        </ul>
      )}
    </Panel>
  )
}
