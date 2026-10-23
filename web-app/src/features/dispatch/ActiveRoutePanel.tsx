// Lote F-E2a — «Ruta activa» (maqueta `dispatch()`, sin el mapa: llega en F-E2b): código y estatus REAL, zona y paradas, chofer y vehículo con su
// disponibilidad, avance, avisos y bloqueantes de la ruta, resumen de la última optimización con lo que «no cupo», y «Órdenes en esta ruta»
// con ▲ ▼ y quitar. Cada acción se pinta solo con su permiso Y con el estatus que el servidor permite (`isEditable`, `canEditHeader`).
import { useId, useMemo, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { zonedInputFromUtc } from '../../kernel/api/tenantZone'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconPin, IconRoute, IconTrash, Panel, RouteMap, Spinner, toast } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { CommentDialog } from '../drivers/CommentDialog'
import { useCancelTrip, useCompleteTrip, usePatchTrip, useRemoveOrder, useSetSequence, useSetStopLocation, useStartTrip } from './dispatchApi'
import {
  attendedStops,
  buildHeaderPatch,
  durationText,
  firstApproximateStop,
  mapStops,
  moveStop,
  orderedStops,
  percent,
  problemMessage,
  round1,
  stopHasPoint,
  stopsLabel,
  tripActionsOf,
  type DispatchPerms,
  type OptimizeDelta,
  type RouteStop,
  type TripDetail,
} from './dispatchRules'
import { withCurrent, type Choice } from './fleetChoices'
import { PinModal } from './PinModal'
import { RouteStopsList } from './RouteStopsList'
import { TripIssues } from './TripIssues'

/** Mensaje de la ruta activa (un error de cualquier acción, o el resultado de optimizar). */
export interface RouteMessage {
  tripId: string
  kind: 'error' | 'info'
  text: string
}

/** Resultado de la última optimización (de esta sesión de la pantalla). */
export interface OptimizeNote {
  tripId: string
  assigned: number
  unassigned: number
  delta: OptimizeDelta | null
}

type Dialog = 'start' | 'close' | 'delete' | null

/** Orden pedido y aún sin confirmar (arrastre o ▲ ▼): vale mientras la ruta siga en la versión (`rowVersion`) con la que se pidió. */
interface PendingOrder {
  tripId: string
  base: string
  ids: number[]
  /** Hay una cadena de peticiones en curso: el orden pedido manda aunque la versión ya haya cambiado con la primera respuesta. */
  active: boolean
}

export function ActiveRoutePanel({
  detail,
  loading,
  failed,
  hasTrips,
  perms,
  canFleet,
  drivers,
  vehicles,
  fleetFailed,
  message,
  onMessage,
  note,
  overLimit,
  onDeleted,
}: {
  detail: TripDetail | undefined
  loading: boolean
  failed: boolean
  hasTrips: boolean
  perms: DispatchPerms
  canFleet: boolean
  drivers: readonly Choice[]
  vehicles: readonly Choice[]
  fleetFailed: boolean
  message: RouteMessage | null
  onMessage: (m: RouteMessage | null) => void
  note: OptimizeNote | null
  /** Texto del aviso «pasa el máximo de paradas» (solo si la ruta se está armando); null = sin aviso. */
  overLimit: string | null
  onDeleted: () => void
}) {
  const t = useT()
  const f = useFormat()
  const driverId = useId()
  const vehicleId = useId()
  const patch = usePatchTrip()
  const sequence = useSetSequence()
  const remove = useRemoveOrder()
  const start = useStartTrip()
  const complete = useCompleteTrip()
  const cancel = useCancelTrip()
  const setLocation = useSetStopLocation()
  const [dialog, setDialog] = useState<Dialog>(null)
  const [selectedStop, setSelectedStop] = useState<number | null>(null)
  const [pinStop, setPinStop] = useState<number | null>(null)
  const [pending, setPending] = useState<PendingOrder | null>(null)
  // reordenaciones en cola: una petición a la vez; lo pedido mientras viaja una se junta en UNA sola (la última secuencia) que sale con la versión devuelta
  const chain = useRef<{ running: boolean; queued: number[] | null }>({ running: false, queued: null })
  // última versión que devolvió el servidor al reordenar: la pantalla tarda un instante en recibirla en `detail`, y un movimiento en ese instante no debe salir con la versión vieja
  const known = useRef<{ tripId: string; base: string; version: string | null | undefined } | null>(null)
  // paradas en el orden mostrado: el pedido (optimista) mientras viaja la petición, si no el del servidor
  const shownStops = useMemo(() => {
    const server = orderedStops(detail?.stops)
    if (!detail || !pending || pending.tripId !== (detail.publicId ?? '') || (!pending.active && pending.base !== (detail.rowVersion ?? ''))) return server
    const byId = new Map(server.map((s) => [s.id, s]))
    const next = pending.ids.map((id) => byId.get(id)).filter((s): s is RouteStop => !!s)
    return next.length === server.length ? next : server
  }, [detail, pending])
  const mapData = useMemo(() => mapStops(shownStops, t), [shownStops, t])
  // lo pedido en un select mientras viaja la petición (el clic no debe parecer ignorado)
  const [asked, setAsked] = useState<{ driver?: string; vehicle?: string }>({})

  if (!detail) {
    if (loading) return <Panel icon={<IconPin />} title={t('dispatch.active.title')}><Spinner block label={t('common.loading')} /></Panel>
    return (
      <Panel icon={<IconPin />} title={t('dispatch.active.title')}>
        <EmptyState
          icon={<IconRoute />}
          title={failed ? t('dispatch.active.loadFailed') : hasTrips ? t('dispatch.active.noneSelected') : t('dispatch.active.noRoutes')}
        />
      </Panel>
    )
  }

  const tripId = detail.publicId ?? ''
  const actions = tripActionsOf(detail, perms)
  const busy = patch.isPending || remove.isPending || setLocation.isPending // reordenar no bloquea: se encola
  const canPin = actions.edit
  const selected = shownStops.find((s) => s.id === selectedStop) ?? null
  const selectedIndex = selected ? shownStops.indexOf(selected) : -1
  const pinTarget = shownStops.find((s) => s.id === pinStop) ?? null
  const firstApprox = canPin ? firstApproximateStop(shownStops) : undefined
  const mine = message && message.tripId === tripId ? message : null
  const total = detail.stopCount ?? 0
  const done = attendedStops(detail.stops)
  const fail = (err: unknown) => {
    const text = problemMessage(applyProblemDetails(err))
    onMessage({ tripId, kind: 'error', text })
  }

  const changeHeader = async (change: { driver?: string; vehicle?: string }) => {
    onMessage(null)
    setAsked(change)
    try {
      await patch.mutateAsync({ publicId: tripId, body: buildHeaderPatch(change, detail.rowVersion) })
      toast.success(t('dispatch.active.saved'))
    } catch (err) {
      fail(err)
    } finally {
      setAsked({})
    }
  }

  // ▲ ▼, teclado en el asa o arrastre: la lista y el plano cambian al instante; si el servidor niega, vuelven al orden anterior con su texto exacto
  const applyOrder = async (next: number[]) => {
    onMessage(null)
    setPending({ tripId, base: detail.rowVersion ?? '', ids: next, active: true })
    if (chain.current.running) {
      chain.current.queued = next // el segundo movimiento espera a la respuesta del primero y usa su versión
      return
    }
    chain.current = { running: true, queued: null }
    const k = known.current
    let version = k && k.tripId === tripId && k.base === (detail.rowVersion ?? '') ? k.version : detail.rowVersion
    const base = detail.rowVersion ?? ''
    let ids: number[] | null = next
    try {
      while (ids) {
        const trip = await sequence.mutateAsync({ publicId: tripId, routeStopIds: ids, rowVersion: version })
        version = trip.rowVersion
        known.current = { tripId, base, version }
        ids = chain.current.queued
        chain.current.queued = null
      }
      setPending((p) => (p ? { ...p, active: false } : p))
    } catch (err) {
      chain.current.queued = null
      setPending(null)
      fail(err)
    } finally {
      chain.current.running = false
    }
  }
  const move = (index: number, delta: -1 | 1) => {
    const next = moveStop(shownStops.map((s) => s.id ?? 0), index, delta)
    if (next) void applyOrder(next)
  }

  const removeStop = async (stop: RouteStop) => {
    if (!stop.orderPublicId) return
    onMessage(null)
    try {
      await remove.mutateAsync({ publicId: tripId, orderPublicId: stop.orderPublicId, rowVersion: detail.rowVersion })
      toast.success(t('dispatch.stops.removed', { number: stop.orderNumber ?? '' }))
    } catch (err) {
      fail(err)
    }
  }

  const driverValue = asked.driver ?? detail.driverPublicId ?? ''
  const vehicleValue = asked.vehicle ?? detail.vehiclePublicId ?? ''
  const driverOptions = withCurrent(drivers, detail.driverPublicId ? { value: detail.driverPublicId, label: [detail.driverCode, detail.driverName].filter(Boolean).join(' · ') } : null)
  const vehicleOptions = withCurrent(vehicles, detail.vehiclePublicId ? { value: detail.vehiclePublicId, label: [detail.vehicleCode, detail.vehiclePlate].filter(Boolean).join(' · ') } : null)
  // el servidor no deja QUITAR chofer ni vehículo de una ruta despachada: solo se cambian
  const canClear = detail.isEditable === true
  const startTime = detail.plannedStartUtc ? zonedInputFromUtc(detail.plannedStartUtc).slice(11) : ''
  const facts: string[] = [
    ...(detail.totalDistanceKm != null ? [`${t('dispatch.active.distance')}: ${f.number(round1(detail.totalDistanceKm))} km`] : []),
    ...(durationText(detail.totalDurationMin) ? [`${t('dispatch.active.duration')}: ${durationText(detail.totalDurationMin)}`] : []),
    ...((detail.totalWeightKg ?? 0) > 0 ? [`${t('dispatch.active.weight')}: ${f.number(round1(detail.totalWeightKg ?? 0))} kg`] : []),
    ...((detail.totalVolumeM3 ?? 0) > 0 ? [`${t('dispatch.active.volume')}: ${f.number(round1(detail.totalVolumeM3 ?? 0))} m³`] : []),
  ]
  const inNote = note && note.tripId === tripId ? note : null
  const unfit = detail.lastRunUnassigned ?? []

  return (
    <Panel
      icon={<IconPin />}
      title={
        <>
          {t('dispatch.active.title')} · <span className="ref">{detail.code}</span> <StatusChip domain="TripStatus" code={detail.statusCode} label={detail.status} />
        </>
      }
      badge={`${detail.kind === 'PICKUP' ? `${t('dispatch.tripKind.PICKUP')} · ` : ''}${detail.zoneCode ?? t('dispatch.active.noZone')} · ${stopsLabel(t, total)}`}
      actions={
        overLimit || actions.remove ? (
          <>
            {overLimit && (
              <span className="disp-over-icon" tabIndex={0} role="img" aria-label={overLimit} title={overLimit}>
                <IconAlert />
              </span>
            )}
            {actions.remove && (
              <button type="button" className="rowbtn" aria-label={t('dispatch.delete.action')} title={t('dispatch.delete.action')} onClick={() => setDialog('delete')}>
                <IconTrash />
              </button>
            )}
          </>
        ) : undefined
      }
      flush
    >
      {mine && (
        <div className={mine.kind === 'error' ? 'form-alert disp-alert' : 'note disp-alert'} role={mine.kind === 'error' ? 'alert' : 'status'} style={{ margin: 12 }}>
          {mine.text}
        </div>
      )}

      {overLimit && (
        <div className="alertbanner inpanel" role="alert">
          <IconAlert />
          <span>{overLimit}</span>
        </div>
      )}

      <div className="disp-sec disp-map-sec">
        <RouteMap
          stops={mapData}
          selectedId={selected?.id ?? null}
          onSelect={(id) => setSelectedStop(Number(id))}
          label={t('dispatch.map.label', { code: detail.code ?? '' })}
          emptyHint={t(canPin ? 'dispatch.map.emptyCanPin' : 'dispatch.map.emptyHint')}
        />
        {selected && (
          <div className="disp-map-sel" role="status">
            <span>
              {t('dispatch.map.selected', { number: selectedIndex + 1, order: selected.orderNumber ?? '' })} ·{' '}
              {[selected.consigneeName, selected.city].filter(Boolean).join(' · ')} ·{' '}
              {t(!stopHasPoint(selected) ? 'dispatch.stops.noPin' : `routeMap.accuracy.${accuracyKey(selected)}`)}
            </span>
            {canPin && (
              <button type="button" className="btn sm" onClick={() => setPinStop(selected.id ?? null)}>
                <IconPin /> {t(stopHasPoint(selected) ? 'dispatch.pin.move' : 'dispatch.pin.set')}
              </button>
            )}
          </div>
        )}
      </div>

      <div className="disp-sec">
        <h3>{t('dispatch.active.assign')}</h3>
        <div className="disp-assign">
          <div className="f">
            <label htmlFor={driverId}>{t('dispatch.active.driver')}</label>
            {canFleet && actions.editHeader ? (
              <select id={driverId} value={driverValue} disabled={patch.isPending} onChange={(e) => void changeHeader({ driver: e.target.value })}>
                {canClear && <option value="">{t('dispatch.active.noDriver')}</option>}
                {driverOptions.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            ) : (
              <span id={driverId} className="disp-note" style={{ margin: 0, color: 'var(--text)' }}>
                {[detail.driverCode, detail.driverName].filter(Boolean).join(' · ') || t('dispatch.active.noDriver')}
              </span>
            )}
          </div>
          <div className="f">
            <label htmlFor={vehicleId}>{t('dispatch.active.vehicle')}</label>
            {canFleet && actions.editHeader ? (
              <select id={vehicleId} value={vehicleValue} disabled={patch.isPending} onChange={(e) => void changeHeader({ vehicle: e.target.value })}>
                {canClear && <option value="">{t('dispatch.active.noVehicle')}</option>}
                {vehicleOptions.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            ) : (
              <span id={vehicleId} className="disp-note" style={{ margin: 0, color: 'var(--text)' }}>
                {[detail.vehicleCode, detail.vehiclePlate].filter(Boolean).join(' · ') || t('dispatch.active.noVehicle')}
              </span>
            )}
          </div>
        </div>
        {perms.plan && actions.editHeader && !canFleet && <p className="disp-note" role="status" style={{ marginTop: 8 }}>{t('dispatch.active.noFleetAccess')}</p>}
        {perms.plan && actions.editHeader && canFleet && fleetFailed && <p className="disp-note" role="status" style={{ marginTop: 8 }}>{t('dispatch.active.availabilityFailed')}</p>}
        {perms.plan && !actions.editHeader && !detail.isTerminal && <p className="disp-note" role="status" style={{ marginTop: 8 }}>{t('dispatch.active.headerLocked')}</p>}
        <div className="disp-progress">
          <span className="mono">{t('dispatch.active.progress', { done, total })}</span>
          <div className="bar" role="progressbar" aria-label={t('dispatch.active.progressLabel')} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent(done, total)}>
            <i style={{ width: `${percent(done, total)}%` }} />
          </div>
          {detail.plannedEndUtc && (
            <span className="mono">
              {t('dispatch.active.eta')} {f.time(detail.plannedEndUtc)}
            </span>
          )}
        </div>
        <div className="disp-facts">
          {startTime && (
            <span>
              {t('dispatch.active.start')}: <b>{f.timeOfDay(startTime)}</b>
            </span>
          )}
          {facts.map((x) => (
            <span key={x}>{x}</span>
          ))}
        </div>
        {(actions.start || actions.close) && (
          <div className="disp-actions">
            {actions.start && (
              <button type="button" className="btn flow" onClick={() => setDialog('start')}>
                {t('dispatch.start.action')}
              </button>
            )}
            {actions.close && (
              <button type="button" className="btn flow" onClick={() => setDialog('close')}>
                {t('dispatch.close.action')}
              </button>
            )}
          </div>
        )}
      </div>

      {(detail.issues ?? []).length > 0 && (
        <div className="disp-sec">
          <h3>{t('dispatch.issues.title')}</h3>
          <TripIssues issues={detail.issues} onApproximate={firstApprox ? () => { setSelectedStop(firstApprox.id ?? null); setPinStop(firstApprox.id ?? null) } : undefined} />
        </div>
      )}

      {inNote && (
        <div className="disp-sec" role="status">
          <h3>{t('dispatch.optimize.title')}</h3>
          <p className="disp-note" style={{ color: 'var(--text)' }}>
            {t('dispatch.optimize.result', { assigned: inNote.assigned, unassigned: inNote.unassigned })}
            {inNote.delta?.distancePct != null && ` ${t('dispatch.optimize.distance', { pct: signed(inNote.delta.distancePct) })}`}
            {inNote.delta?.durationMin != null && ` ${t('dispatch.optimize.minutes', { min: signed(inNote.delta.durationMin) })}`}
            {inNote.delta === null && ` ${t('dispatch.optimize.firstRun')}`}
            {inNote.delta !== null && inNote.delta.distancePct === null && inNote.delta.durationMin === null && ` ${t('dispatch.optimize.noMetrics')}`}
          </p>
        </div>
      )}

      {unfit.length > 0 && (
        <div className="disp-sec">
          <h3>{t('dispatch.optimize.unfitTitle', { count: unfit.length })}</h3>
          <p className="disp-note">{t('dispatch.optimize.unfitHint')}</p>
          <ul className="disp-unfit">
            {unfit.map((u) => (
              <li key={u.orderPublicId}>
                <span className="ref">{u.orderNumber}</span> — {u.reason}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="disp-sec">
        <h3>{t('dispatch.stops.title')}</h3>
        <RouteStopsList
          stops={shownStops}
          editable={actions.edit}
          canPin={canPin}
          busy={busy}
          selectedId={selected?.id ?? null}
          onSelect={setSelectedStop}
          onMove={move}
          onReorder={(ids) => void applyOrder(ids)}
          onPin={(s) => setPinStop(s.id ?? null)}
          onRemove={(s) => void removeStop(s)}
        />
      </div>

      {pinTarget && (
        <PinModal
          key={pinTarget.id}
          stop={pinTarget}
          reference={mapData}
          onClose={() => setPinStop(null)}
          onSave={async ({ lat, lng }) => {
            await setLocation.mutateAsync({ publicId: tripId, routeStopId: pinTarget.id ?? 0, lat, lng, rowVersion: detail.rowVersion })
            toast.success(t('dispatch.pin.done', { number: pinTarget.orderNumber ?? '' }))
          }}
        />
      )}

      <CommentDialog
        open={dialog === 'start'}
        title={t('dispatch.start.title', { code: detail.code ?? '' })}
        confirmLabel={t('dispatch.start.confirm')}
        tone="flow"
        onClose={() => setDialog(null)}
        onConfirm={async (comment) => {
          await start.mutateAsync({ publicId: tripId, comment, rowVersion: detail.rowVersion })
          toast.success(t('dispatch.start.done', { code: detail.code ?? '' }))
        }}
      >
        {t('dispatch.start.body')}
      </CommentDialog>
      <CommentDialog
        open={dialog === 'close'}
        title={t('dispatch.close.title', { code: detail.code ?? '' })}
        confirmLabel={t('dispatch.close.confirm')}
        tone="flow"
        onClose={() => setDialog(null)}
        onConfirm={async (comment) => {
          await complete.mutateAsync({ publicId: tripId, comment, rowVersion: detail.rowVersion })
          toast.success(t('dispatch.close.done', { code: detail.code ?? '' }))
        }}
      >
        {t('dispatch.close.body')}
      </CommentDialog>
      <CommentDialog
        open={dialog === 'delete'}
        title={t('dispatch.delete.title', { code: detail.code ?? '' })}
        confirmLabel={t('dispatch.delete.confirm')}
        onClose={() => setDialog(null)}
        onConfirm={async (comment) => {
          await cancel.mutateAsync({ publicId: tripId, comment, rowVersion: detail.rowVersion })
          toast.success(t('dispatch.delete.done', { code: detail.code ?? '' }))
          onDeleted()
        }}
      >
        {t('dispatch.delete.body')}
      </CommentDialog>
    </Panel>
  )
}

/** manual | approximate | exact de una parada con punto (mismas claves de `routeMap.accuracy.*`). */
function accuracyKey(s: RouteStop): 'manual' | 'approximate' | 'exact' {
  return (s.geocodeAccuracyCode ?? '').toUpperCase() === 'MANUAL' ? 'manual' : s.isApproximate ? 'approximate' : 'exact'
}

/** «+3» / «-12» / «0». */
function signed(n: number): string {
  return n > 0 ? `+${n}` : String(n)
}
