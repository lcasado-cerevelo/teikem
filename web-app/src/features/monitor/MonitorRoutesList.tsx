// Lote F-E3 — «Rutas activas» (maqueta `monitoreo()`): buscador (al servidor), filtro de zona, «Incluir completadas» y una tarjeta por ruta con código,
// chofer, zona, estatus REAL del catálogo, % y barra de avance reales, ⚠ si pasa el máximo de paradas y «Último ping hace N s» o «Sin ping».
import { Link } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconRoute, Panel, QBox, SelectFilter, Spinner } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { agoText, isRecentPing, pingAgeSeconds, tripProgress, type MonitorTrip } from './monitorRules'

export interface ZoneOption {
  value: string
  label: string
}

export function MonitorRoutesList({
  trips,
  loading,
  failed,
  onRetry,
  selectedId,
  onSelect,
  now,
  q,
  onQ,
  searching,
  zone,
  onZone,
  zoneOptions,
  zoneAllowed,
  includeCompleted,
  onIncludeCompleted,
  hasAny,
  date,
}: {
  trips: readonly MonitorTrip[]
  loading: boolean
  failed: boolean
  onRetry: () => void
  selectedId: string | null
  onSelect: (publicId: string) => void
  now: number
  q: string
  onQ: (value: string) => void
  /** ¿Hay una búsqueda aplicada? (cambia el texto del vacío). */
  searching: boolean
  zone: string
  onZone: (value: string) => void
  zoneOptions: readonly ZoneOption[]
  /** `fleet.view` + CATALOG: sin ellos no hay filtro de zona y se explica. */
  zoneAllowed: boolean
  includeCompleted: boolean
  onIncludeCompleted: (value: boolean) => void
  /** ¿El servidor dice que hay rutas en esa fecha (y zona), aunque la búsqueda no deje ninguna? */
  hasAny: boolean
  date: string
}) {
  const t = useT()
  return (
    <Panel icon={<IconRoute />} title={t('monitor.activeRoutes')} badge={failed && trips.length === 0 ? undefined : trips.length} flush>
      <div className="mon-tools">
        <QBox value={q} onChange={onQ} placeholder={t('monitor.search')} label={t('monitor.search')} />
        {zoneAllowed && zoneOptions.length > 0 && (
          <SelectFilter label={t('monitor.zone')} value={zone} onChange={onZone} options={zoneOptions} allLabel={t('monitor.allZones')} />
        )}
        <label className="sw">
          <input type="checkbox" role="switch" checked={includeCompleted} onChange={(e) => onIncludeCompleted(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('monitor.includeCompleted')}</span>
        </label>
        {!zoneAllowed && <p className="disp-note" role="status">{t('monitor.noZoneAccess')}</p>}
      </div>
      {loading && trips.length === 0 ? (
        <Spinner block label={t('common.loading')} />
      ) : failed && trips.length === 0 ? (
        <EmptyState
          title={t('monitor.listError')}
          action={
            <button type="button" className="btn sm" onClick={onRetry}>
              {t('monitor.retry')}
            </button>
          }
        />
      ) : trips.length === 0 ? (
        <EmptyState
          icon={<IconRoute />}
          title={searching && hasAny ? t('monitor.noMatch') : t('monitor.noRoutes')}
          body={searching && hasAny ? undefined : t('monitor.noRoutesHint')}
          action={
            searching && hasAny ? undefined : (
              <Can perm="trips.dispatch">
                <Link className="btn sm" to={`/ops/dispatch?date=${date}`}>
                  {t('monitor.openDispatchEmpty')}
                </Link>
              </Can>
            )
          }
        />
      ) : (
        <ul className="mon-list" aria-label={t('monitor.activeRoutes')}>
          {trips.map((r) => {
            const id = r.publicId ?? ''
            const pct = tripProgress(r)
            const age = pingAgeSeconds(r.lastPing, now)
            const ago = agoText(t, age)
            const stale = age !== null && !isRecentPing(r.lastPing, now)
            return (
              <li key={id}>
                <button type="button" className={id === selectedId ? 'routecard on' : 'routecard'} aria-current={id === selectedId ? 'true' : undefined} onClick={() => onSelect(id)}>
                  <div className="rt">
                    <div className="rr">
                      <span className="ref">{r.code}</span>
                      {r.driverCode && <span className="dr">{r.driverCode}</span>}
                      {r.zoneCode && <span className="tag">{r.zoneCode}</span>}
                      <span className="pct">{pct}%</span>
                    </div>
                    <div className="rr" style={{ marginTop: 5 }}>
                      <StatusChip domain="TripStatus" code={r.statusCode} label={r.status} />
                    </div>
                    <div className="bar" role="progressbar" aria-label={t('monitor.progressOf', { code: r.code ?? '' })} aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct}>
                      <i style={{ width: `${pct}%` }} />
                    </div>
                    <div className="meta">
                      {r.driverName && <>{r.driverName} · </>}
                      <span className={stale ? 'stale' : undefined}>{ago ? t('monitor.card.ping', { ago }) : t('monitor.card.noPing')}</span>
                    </div>
                  </div>
                  {r.overStopLimit && (
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
