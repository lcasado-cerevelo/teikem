// Lote F-E3 — Panel de la ruta elegida (maqueta `monitoreo()`): plano esquemático (`RouteMap`) con los pines por secuencia y el estado REAL de cada parada y
// el punto vivo del último ping si lo hay; telemetría SOLO con lo que existe (velocidad y rumbo; no hay señal GPS ni batería en el API); «Último ping hace N s»;
// el interruptor «Siguiendo en vivo»; los avisos de la ruta y «Paradas de la ruta». Solo lectura: lo único que enlaza es «Abrir en Sala de despacho».
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, EmptyState, IconPin, IconRoute, IconTruck, isGeoPoint, Panel, RouteMap, Spinner, stateOf } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { mapStops, orderedStops, type RouteStop, type TripDetail } from '../dispatch/dispatchRules'
import { agoText, compassOf, hasPing, isRecentPing, pingAgeSeconds, speedOf, type MonitorTrip } from './monitorRules'

export function MonitorRoutePanel({
  trip,
  detail,
  loading,
  failed,
  onRetry,
  now,
  follow,
  onFollow,
  date,
}: {
  /** La fila del monitor de la ruta elegida (avance, avisos y último ping). */
  trip: MonitorTrip | undefined
  detail: TripDetail | undefined
  loading: boolean
  failed: boolean
  onRetry: () => void
  now: number
  follow: boolean
  onFollow: (on: boolean) => void
  date: string
}) {
  const t = useT()
  const f = useFormat()
  const [picked, setPicked] = useState<{ tripId: string; stopId: number } | null>(null)
  const stops = useMemo(() => orderedStops(detail?.stops), [detail])
  const mapData = useMemo(() => mapStops(stops, t), [stops, t])
  const tripId = detail?.publicId ?? trip?.publicId ?? ''
  const selectedStop = picked && picked.tripId === tripId ? picked.stopId : null

  if (!trip) {
    return (
      <Panel icon={<IconPin />} title={t('monitor.panelTitle')}>
        <EmptyState icon={<IconPin />} title={t('monitor.selectRoute')} />
      </Panel>
    )
  }

  const ping = hasPing(trip.lastPing) ? trip.lastPing : hasPing(detail?.lastPing) ? detail?.lastPing : undefined
  const age = pingAgeSeconds(ping, now)
  const recent = isRecentPing(ping, now)
  const speed = speedOf(ping?.speedKmh)
  const compass = compassOf(ping?.headingDeg)
  const livePoint = ping && isGeoPoint(ping.point) ? { lat: ping.point.lat as number, lng: ping.point.lng as number, label: t('monitor.liveLabel', { ago: agoText(t, age) ?? '' }) } : null
  const total = trip.totalStops ?? stops.length
  const done = (trip.completedStops ?? 0) + (trip.failedStops ?? 0)
  const approx = trip.approximateStops ?? 0
  const label = t('monitor.mapLabel', { code: trip.code ?? '' })

  return (
    <Panel
      icon={<IconPin />}
      title={
        <>
          <span className="ref">{trip.code}</span> · {trip.driverName || trip.driverCode || t('monitor.noDriver')}
        </>
      }
      badge={`${trip.zoneCode ?? t('monitor.noZone')} · ${done}/${total}`}
      actions={
        <Can perm="trips.dispatch">
          <Link className="btn sm" to={`/ops/dispatch?date=${date}&trip=${tripId}`}>
            <IconRoute /> {t('monitor.openDispatch')}
          </Link>
        </Can>
      }
      flush
    >
      <div className="mon-map">
        {loading && !detail ? (
          <Spinner block label={t('common.loading')} />
        ) : failed && !detail ? (
          <EmptyState
            title={t('monitor.detailError')}
            action={
              <button type="button" className="btn sm" onClick={onRetry}>
                {t('monitor.retry')}
              </button>
            }
          />
        ) : (
          <RouteMap
            stops={mapData}
            selectedId={selectedStop}
            onSelect={(id) => setPicked({ tripId, stopId: Number(id) })}
            live={livePoint}
            height={340}
            label={label}
            emptyHint={t('monitor.map.emptyHint')}
          />
        )}
      </div>

      <div className="telemrow">
        <div className="telemcell">
          <span className="tl">
            <IconTruck />
            {t('monitor.speed')}
          </span>
          <span className={speed === null ? 'tv none' : 'tv'}>{speed === null ? t('monitor.none') : `${f.number(speed)} km/h`}</span>
        </div>
        <div className="telemcell">
          <span className="tl">
            <IconRoute />
            {t('monitor.heading')}
          </span>
          <span className={compass === null ? 'tv none' : 'tv'}>
            {compass === null ? t('monitor.none') : `${f.number(Math.round(ping?.headingDeg ?? 0))}° ${t(`monitor.compass.${compass}`)}`}
          </span>
        </div>
      </div>

      <div className="mon-pingrow">
        <span className="ping" role="status">
          {age === null ? t('monitor.noPing') : `${t('monitor.lastPing')} ${agoText(t, age)}`}
        </span>
        {age !== null && !recent && <Chip tone="warn">{t('monitor.staleChip')}</Chip>}
        <label className="sw">
          <input type="checkbox" role="switch" checked={follow} onChange={(e) => onFollow(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('monitor.follow')}</span>
        </label>
      </div>

      {(age === null || ping?.linkedToTrip === false || approx > 0 || trip.overStopLimit) && (
      <div className="mon-notes">
        {age === null && <p className="hint">{t('monitor.noPingHint')}</p>}
        {ping && ping.linkedToTrip === false && <p className="hint">{t('monitor.pingNotLinked')}</p>}
        {approx > 0 && (
          <p>
            <Chip tone="warn">{t('monitor.warn.approx', { count: approx })}</Chip>
            <span>{t('monitor.warn.approxHint')}</span>
          </p>
        )}
        {trip.overStopLimit && (
          <p role="alert">
            <Chip tone="fail">
              <IconAlert /> {t('dispatch.overLimitShort')}
            </Chip>
            <span>{t('monitor.warn.overLimitHint', { count: total })}</span>
          </p>
        )}
      </div>
      )}

      <h3 className="mon-stops-t">{t('monitor.stopsTitle')}</h3>
      {stops.length === 0 ? (
        <p className="note" style={{ margin: 12 }}>
          {t('monitor.noStops')}
        </p>
      ) : (
        <ul className="mon-stops" aria-label={t('monitor.stopsTitle')}>
          {stops.map((s, i) => (
            <StopRow key={s.id} stop={s} index={i} selected={s.id === selectedStop} onSelect={() => setPicked({ tripId, stopId: s.id ?? 0 })} />
          ))}
        </ul>
      )}
    </Panel>
  )
}

function StopRow({ stop, index, selected, onSelect }: { stop: RouteStop; index: number; selected: boolean; onSelect: () => void }) {
  const t = useT()
  const f = useFormat()
  const state = stateOf(stop.statusCode)
  const town = [stop.city, stop.postalCode].filter(Boolean).join(' · ')
  const win =
    stop.windowStartUtc && stop.windowEndUtc
      ? t('monitor.stop.window', { from: f.time(stop.windowStartUtc), to: f.time(stop.windowEndUtc) })
      : stop.windowStartUtc
        ? t('monitor.stop.windowFrom', { from: f.time(stop.windowStartUtc) })
        : stop.windowEndUtc
          ? t('monitor.stop.windowTo', { to: f.time(stop.windowEndUtc) })
          : null
  const eta = stop.actualArrivalUtc
    ? t('monitor.stop.arrived', { time: f.time(stop.actualArrivalUtc) })
    : stop.plannedArrivalUtc
      ? t('monitor.stop.eta', { time: f.time(stop.plannedArrivalUtc) })
      : null
  return (
    <li className={selected ? 'tlstop on' : 'tlstop'} data-stop={stop.id} data-state={state}>
      <span className={`tlseq ${state}`} aria-hidden="true">
        {index + 1}
      </span>
      <button type="button" className="tl-pick" aria-pressed={selected} aria-label={t('monitor.stop.select', { number: index + 1, name: stop.consigneeName || stop.orderNumber || '' })} onClick={onSelect}>
        <span className="tl-name">{stop.consigneeName || stop.orderNumber}</span>
        <span className="tl-meta">{[stop.orderNumber, town].filter(Boolean).join(' · ')}</span>
        {(win || eta) && <span className="tl-meta">{[win, eta].filter(Boolean).join(' · ')}</span>}
      </button>
      {stop.lateForWindow && <Chip tone="fail">{t('dispatch.stops.late')}</Chip>}
      <StatusChip domain="RouteStopStatus" code={stop.statusCode} label={stop.status} />
    </li>
  )
}
