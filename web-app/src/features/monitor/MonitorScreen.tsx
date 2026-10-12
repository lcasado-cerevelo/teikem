// Lote F-E3 — Operación → Monitoreo de ruta (`/ops/monitor`, `trips.view`, LTL_GROUND; maqueta `monitoreo()`; manual 05 §9). SOLO LECTURA.
// Título con la insignia EN VIVO (solo si hay algún ping reciente), el «río» con los totales REALES del servidor, «Rutas activas» a la izquierda (300 px;
// en menos de 900 px se apila) y la ruta elegida a la derecha: plano, telemetría, último ping y paradas. La ruta se elige en `?trip=` y la fecha en `?date=`.
// Se refresca solo cada 30 s mientras la pestaña esté visible y «Siguiendo en vivo» esté encendido; «Actualizar» lo hace al instante.
import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCan, useModule } from '../../kernel/access'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconChart, IconClock, IconPin, IconRoute } from '../../kernel/ui'
import { IconRefreshCw } from '../../kernel/ui/actionIcons'
import { IconAlert } from '../../kernel/ui/icons'
import { useDispatchZones } from '../drivers/api'
import { useDebounced } from '../warehouse/lineRules'
import { MonitorRoutePanel } from './MonitorRoutePanel'
import { MonitorRoutesList, type ZoneOption } from './MonitorRoutesList'
import { useMonitor, useMonitorTrip, useNow, useVisibleInterval } from './monitorApi'
import { agoText, liveSummary, nextArrival, overallProgress, pickSelected, readFollow, REFRESH_MS, writeFollow, type MonitorTrip } from './monitorRules'
import '../analytics/pulse.css'
import './monitor.css'

const DAY = /^\d{4}-\d{2}-\d{2}$/
const NO_TRIPS: readonly MonitorTrip[] = []

/** localStorage si existe y se puede tocar (algunos navegadores lo bloquean). */
function safeStorage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage
  } catch {
    return null
  }
}

export default function MonitorScreen() {
  const t = useT()
  const f = useFormat()
  const [params, setParams] = useSearchParams()
  const today = tenantToday()
  const dateParam = params.get('date')
  const date = dateParam && DAY.test(dateParam) ? dateParam : today
  const tripParam = params.get('trip')

  const canView = useCan('trips.view')
  const canFleet = useCan('fleet.view')
  const catalogOn = useModule('CATALOG')
  const zoneAllowed = canFleet && catalogOn

  const [q, setQ] = useState('')
  const search = useDebounced(q, 300)
  const [zone, setZone] = useState('')
  const [includeCompleted, setIncludeCompleted] = useState(false)
  const [follow, setFollowState] = useState(() => readFollow(safeStorage()))
  const setFollow = (on: boolean) => {
    setFollowState(on)
    writeFollow(safeStorage(), on)
  }
  const now = useNow(1000)

  const zones = useDispatchZones(false, zoneAllowed)
  const zoneOptions = useMemo<ZoneOption[]>(() => (zones.data ?? []).map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') })), [zones.data])

  const monitor = useMonitor({ date, zoneId: zone, search, includeCompleted }, canView)
  const trips = monitor.data?.trips ?? NO_TRIPS
  const totals = monitor.data?.totals
  const selectedId = pickSelected(trips, tripParam, monitor.isPending)
  const selectedTrip = trips.find((x) => x.publicId === selectedId)
  const detail = useMonitorTrip(selectedId, canView)

  const { refetch: refetchMonitor } = monitor
  const { refetch: refetchDetail } = detail
  const refreshAll = useCallback(() => {
    void refetchMonitor()
    if (selectedId) void refetchDetail()
  }, [refetchMonitor, refetchDetail, selectedId])
  useVisibleInterval(refreshAll, REFRESH_MS, follow && canView)

  const update = (next: { date?: string; trip?: string | null }) => {
    const p = new URLSearchParams(params)
    if (next.date !== undefined) {
      if (next.date === today || next.date === '') p.delete('date')
      else p.set('date', next.date)
    }
    if (next.trip !== undefined) {
      if (next.trip) p.set('trip', next.trip)
      else p.delete('trip')
    }
    setParams(p, { replace: true })
  }
  const changeDate = (value: string) => {
    if (value !== '' && !DAY.test(value)) return
    update({ date: value, trip: null })
  }

  const live = liveSummary(trips, now)
  const next = nextArrival(trips)
  const totalTrips = totals?.trips ?? 0
  const searching = search.trim() !== ''
  const updatedAgo = monitor.dataUpdatedAt > 0 ? agoText(t, Math.max(0, Math.floor((now - monitor.dataUpdatedAt) / 1000))) : null
  const refreshing = monitor.isFetching || detail.isFetching
  const lastOk = monitor.dataUpdatedAt > 0 ? f.time(new Date(monitor.dataUpdatedAt).toISOString()) : ''

  return (
    <div className="wrap mon">
      <div className="head">
        <div>
          <div className="mon-head-title">
            <h1>{t('monitor.title')}</h1>
            {canView && monitor.data && (
              <span className={live.live ? 'livebadge' : 'livebadge idle'} role="status">
                <span className="dot" aria-hidden="true" />
                {live.live ? t('monitor.live') : t('monitor.noSignal')}
              </span>
            )}
          </div>
          <p>{t('monitor.sub')}</p>
        </div>
        {canView && (
          <div className="act">
            <div className="mon-date">
              <label htmlFor="monitor-date">{t('monitor.date')}</label>
              <input id="monitor-date" type="date" value={date} onChange={(e) => changeDate(e.target.value)} />
              {date !== today && (
                <button type="button" className="btn sm" onClick={() => changeDate(today)}>
                  {t('monitor.todayButton')}
                </button>
              )}
            </div>
            <button type="button" className="btn" disabled={refreshing} onClick={refreshAll}>
              <IconRefreshCw /> {refreshing ? t('monitor.refreshing') : t('monitor.refresh')}
            </button>
            {updatedAgo && <span className="mon-updated" role="status">{t('monitor.updated', { ago: updatedAgo })}</span>}
          </div>
        )}
      </div>

      {!canView ? (
        <EmptyState icon={<IconRoute />} title={t('monitor.noView')} />
      ) : (
        <>
          {monitor.isError && monitor.data && (
            <div className="mon-banner" role="alert">
              <IconAlert />
              <span>{lastOk ? t('monitor.staleBanner', { time: lastOk }) : t('monitor.staleBannerNoTime')}</span>
            </div>
          )}

          {monitor.data && (
            <div className="pulse">
              <div className="river" role="group" aria-label={t('monitor.riverAria')}>
                <div className="node flow">
                  <div className="ph">
                    <IconRoute /> <span>{t(includeCompleted ? 'monitor.routesAll' : 'monitor.activeRoutes')}</span>
                  </div>
                  <div className="big">{f.number(totalTrips)}</div>
                </div>
                <div className="pipe" />
                <div className="node flow">
                  <div className="ph">
                    <IconPin /> <span>{t('monitor.stopsLeft')}</span>
                  </div>
                  <div className="big">{f.number(totals?.pendingStops ?? 0)}</div>
                </div>
                <div className="pipe" />
                <div className="node flow">
                  <div className="ph">
                    <IconChart /> <span>{t('monitor.avgProgress')}</span>
                  </div>
                  <div className="big">{overallProgress(totals)}%</div>
                  <div className="sub">{t('monitor.avgProgressSub', { done: f.number((totals?.completedStops ?? 0) + (totals?.failedStops ?? 0)), total: f.number(totals?.totalStops ?? 0) })}</div>
                </div>
                <div className="pipe" />
                <div className="node money mon-eta">
                  <div className="ph">
                    <IconClock /> <span>{t('monitor.nextEta')}</span>
                  </div>
                  <div className="big">{next ? f.time(next.etaUtc) : '—'}</div>
                  <div className="sub">{next ? (next.trip.code ?? '') : t('monitor.nextEtaNone')}</div>
                </div>
                {typeof totals?.tripsWithoutPing === 'number' && (
                  <>
                    <div className="river-fork" aria-hidden="true" />
                    <div className={totals.tripsWithoutPing > 0 ? 'node mon-minor mon-alert' : 'node mon-minor'}>
                      <div className="ph">
                        <IconPin /> <span>{t('monitor.withoutPing')}</span>
                      </div>
                      <div className="big">{f.number(totals.tripsWithoutPing)}</div>
                    </div>
                  </>
                )}
                {typeof totals?.overStopLimitTrips === 'number' && (
                  <div className={totals.overStopLimitTrips > 0 ? 'node mon-minor mon-alert' : 'node mon-minor'}>
                    <div className="ph">
                      <IconAlert /> <span>{t('monitor.overLimit')}</span>
                    </div>
                    <div className="big">{f.number(totals.overStopLimitTrips)}</div>
                  </div>
                )}
              </div>
            </div>
          )}

          <div className="mon-cols">
            <MonitorRoutesList
              trips={trips}
              loading={monitor.isPending}
              failed={monitor.isError}
              onRetry={() => void refetchMonitor()}
              selectedId={selectedId}
              onSelect={(id) => update({ trip: id })}
              now={now}
              q={q}
              onQ={setQ}
              searching={searching}
              zone={zone}
              onZone={setZone}
              zoneOptions={zoneOptions}
              zoneAllowed={zoneAllowed}
              includeCompleted={includeCompleted}
              onIncludeCompleted={setIncludeCompleted}
              hasAny={totalTrips > 0}
              date={date}
            />
            <MonitorRoutePanel
              trip={selectedTrip}
              detail={detail.data}
              loading={detail.isPending && !!selectedId}
              failed={detail.isError}
              onRetry={() => void refetchDetail()}
              now={now}
              follow={follow}
              onFollow={setFollow}
              date={date}
            />
          </div>
        </>
      )}
    </div>
  )
}
