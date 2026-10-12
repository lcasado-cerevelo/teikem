// Lote F-E2a — «Ruta activa» (maqueta `dispatch()`, sin el mapa: llega en F-E2b): código y estatus REAL, zona y paradas, chofer y vehículo con su
// disponibilidad, avance, avisos y bloqueantes de la ruta, resumen de la última optimización con lo que «no cupo», y «Órdenes en esta ruta»
// con ▲ ▼ y quitar. Cada acción se pinta solo con su permiso Y con el estatus que el servidor permite (`isEditable`, `canEditHeader`).
import { useId, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { zonedInputFromUtc } from '../../kernel/api/tenantZone'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconPin, IconRoute, IconTrash, Panel, Spinner, toast } from '../../kernel/ui'
import { CommentDialog } from '../drivers/CommentDialog'
import { useCancelTrip, useCompleteTrip, usePatchTrip, useRemoveOrder, useSetSequence, useStartTrip } from './dispatchApi'
import {
  attendedStops,
  buildHeaderPatch,
  durationText,
  moveStop,
  percent,
  problemMessage,
  round1,
  stopIds,
  stopsLabel,
  tripActionsOf,
  type DispatchPerms,
  type OptimizeDelta,
  type RouteStop,
  type TripDetail,
} from './dispatchRules'
import { withCurrent, type Choice } from './fleetChoices'
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
  const [dialog, setDialog] = useState<Dialog>(null)
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
  const busy = patch.isPending || sequence.isPending || remove.isPending
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

  const move = async (index: number, delta: -1 | 1) => {
    const next = moveStop(stopIds(detail.stops), index, delta)
    if (!next) return
    onMessage(null)
    try {
      await sequence.mutateAsync({ publicId: tripId, routeStopIds: next, rowVersion: detail.rowVersion })
    } catch (err) {
      fail(err)
    }
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
      badge={`${detail.zoneCode ?? t('dispatch.active.noZone')} · ${stopsLabel(t, total)}${detail.overStopLimit ? ' ⚠' : ''}`}
      actions={
        actions.remove ? (
          <button type="button" className="rowbtn" aria-label={t('dispatch.delete.action')} title={t('dispatch.delete.action')} onClick={() => setDialog('delete')}>
            <IconTrash />
          </button>
        ) : undefined
      }
      flush
    >
      {mine && (
        <div className={mine.kind === 'error' ? 'form-alert disp-alert' : 'note disp-alert'} role={mine.kind === 'error' ? 'alert' : 'status'} style={{ margin: 12 }}>
          {mine.text}
        </div>
      )}

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
          <TripIssues issues={detail.issues} />
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
        <RouteStopsList stops={detail.stops} editable={actions.edit} busy={busy} onMove={(i, d) => void move(i, d)} onRemove={(s) => void removeStop(s)} />
      </div>

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

/** «+3» / «-12» / «0». */
function signed(n: number): string {
  return n > 0 ? `+${n}` : String(n)
}
