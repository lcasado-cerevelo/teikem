// Lote F-E2a — Operación → Sala de despacho (`/ops/dispatch`, `trips.dispatch`, LTL_GROUND; maqueta `dispatch()`; manual 05). Tres columnas como la
// maqueta: «Órdenes sin asignar» | «Ruta activa» + «Choferes» | «Rutas de hoy». SIN mapa (el mapa esquemático, el arrastre y el pin manual son de
// F-E2b; el monitoreo, de F-E3). La ruta se elige en `?trip=` y la fecha en `?date=` (hoy de la compañía por omisión). Todo botón es real y se
// pinta solo con su permiso; lo que el servidor niega por estatus se pinta según el DTO (`isEditable`, `canEditHeader`).
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can, useCan, useModule } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { IconAlert } from '../../kernel/ui/icons'
import { ClientPicker, EmptyState, Filters, IconPlus, IconRoute, IconTruck, IconUsers, SelectFilter, SplitPane, toast } from '../../kernel/ui'
import { useDispatchZones } from '../drivers/api'
import { useDrivers } from '../drivers/api'
import { useDebounced } from '../warehouse/lineRules'
import { useUnassignedOrders } from '../scan/scanApi'
import { ActiveRoutePanel, type OptimizeNote, type RouteMessage } from './ActiveRoutePanel'
import { useAddOrders, useOptimizeTrip, useTrip, useTripProgress, useTrips } from './dispatchApi'
import {
  buildUnassignedQuery,
  hasUnassignedFilters,
  maxStopsSource,
  NO_UNASSIGNED_FILTERS,
  optimizeDelta,
  problemMessage,
  tripActionsOf,
  type MonitorTrip,
  type UnassignedFilters,
  type UnassignedOrder,
} from './dispatchRules'
import { DispatchModal } from './DispatchModal'
import { DriversPanel, TodayRoutesPanel } from './DispatchSidePanels'
import { useFleetChoices } from './fleetChoices'
import { PlanDayModal } from './PlanDayModal'
import { ReassignZoneModal } from './ReassignZoneModal'
import { TripCreateModal } from './TripCreateModal'
import { UnassignedPanel, type AddProblem } from './UnassignedPanel'
import './dispatch.css'

type Modal = 'create' | 'planDay' | 'reassign' | 'dispatch' | null

const NO_UNASSIGNED: readonly UnassignedOrder[] = []
const DAY = /^\d{4}-\d{2}-\d{2}$/

export default function DispatchScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const today = tenantToday()
  const dateParam = params.get('date')
  const date = dateParam && DAY.test(dateParam) ? dateParam : today
  const tripParam = params.get('trip')

  const canView = useCan('trips.view')
  const canPlan = useCan('trips.plan')
  const canOptimize = useCan('trips.optimize')
  const canDispatch = useCan('trips.dispatch')
  const canClose = useCan('trips.close')
  const canFleet = useCan('fleet.view')
  const canClients = useCan('clients.read')
  const catalogOn = useModule('CATALOG')
  const fleetOn = canFleet && catalogOn
  const perms = { plan: canPlan, optimize: canOptimize, dispatch: canDispatch, close: canClose }

  // ---- rutas del día y ruta activa ----
  const trips = useTrips(date, canView)
  const list = trips.data ?? []
  const selectedId = list.some((x) => x.publicId === tripParam) || (trips.isFetching && tripParam) ? tripParam : (list[0]?.publicId ?? null)
  const detailQuery = useTrip(selectedId, canView)
  const detail = detailQuery.data
  const actions = tripActionsOf(detail, perms)
  const progressQuery = useTripProgress(date, canView)
  const progress = useMemo(() => new Map<string, MonitorTrip>((progressQuery.data?.trips ?? []).map((x) => [x.publicId ?? '', x])), [progressQuery.data])

  // ---- choferes, vehículos y zonas (fleet.view + CATALOG; sin ellos se explica) ----
  const zones = useDispatchZones(false, fleetOn)
  const drivers = useDrivers(fleetOn)
  const choices = useFleetChoices(date, fleetOn)
  const zoneList = useMemo(() => zones.data ?? [], [zones.data])

  // ---- sin asignar ----
  const [filters, setFilters] = useState<UnassignedFilters>(NO_UNASSIGNED_FILTERS)
  const [clientPublicId, setClientPublicId] = useState<string | null>(null)
  const settled = useDebounced(filters, 300)
  const settledClient = useDebounced(clientPublicId, 300)
  const query = useMemo(() => buildUnassignedQuery(settled, settledClient), [settled, settledClient])
  const unassigned = useUnassignedOrders(query, canView)
  const items = unassigned.data?.items ?? NO_UNASSIGNED
  const [checked, setChecked] = useState<string[]>([])
  const liveChecked = useMemo(() => {
    const ids = new Set(items.map((o) => o.publicId ?? ''))
    return checked.filter((id) => ids.has(id))
  }, [checked, items])
  const [addProblem, setAddProblem] = useState<AddProblem | null>(null)
  const add = useAddOrders()
  const showZoneFilter = fleetOn && !zones.isError && zoneList.length > 0
  const zoneOptions = useMemo(
    () => [{ value: 'none', label: t('dispatch.filters.noZone') }, ...zoneList.map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') }))],
    [zoneList, t],
  )

  // ---- estado de la pantalla ----
  const [modal, setModal] = useState<Modal>(null)
  const [message, setMessage] = useState<RouteMessage | null>(null)
  const [note, setNote] = useState<OptimizeNote | null>(null)
  const optimize = useOptimizeTrip()

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
    setChecked([])
    setAddProblem(null)
  }

  const addTarget = detail && actions.edit ? (detail.code ?? '') : null
  const runAdd = async () => {
    if (!detail?.publicId || liveChecked.length === 0) return
    setAddProblem(null)
    try {
      await add.mutateAsync({ publicId: detail.publicId, orderPublicIds: liveChecked, rowVersion: detail.rowVersion })
      toast.success(t('dispatch.unassigned.added', { count: liveChecked.length, code: detail.code ?? '' }))
      setChecked([])
    } catch (err) {
      const p = applyProblemDetails(err)
      setAddProblem({ title: problemMessage(p), errors: p.errors })
    }
  }

  const runOptimize = async () => {
    if (!detail?.publicId) return
    const tripId = detail.publicId
    setMessage(null)
    setNote(null)
    try {
      const { result, runs } = await optimize.mutateAsync({ publicId: tripId, rowVersion: detail.rowVersion })
      const delta = optimizeDelta(runs)
      setNote({ tripId, assigned: result.assignedCount ?? 0, unassigned: result.unassignedCount ?? 0, delta })
      toast.success(t('dispatch.optimize.toast', { assigned: result.assignedCount ?? 0, unassigned: result.unassignedCount ?? 0 }))
    } catch (err) {
      setMessage({ tripId, kind: 'error', text: problemMessage(applyProblemDetails(err)) })
    }
  }

  // aviso rojo de la maqueta: la ruta activa pasa el máximo de paradas
  const overDriver = detail?.driverPublicId ? drivers.data?.find((d) => d.publicId === detail.driverPublicId) : undefined
  const source = maxStopsSource(overDriver?.maxStopsPerRoute, !!overDriver)

  return (
    <div className="wrap disp">
      <div className="head">
        <div>
          <h1>{t('dispatch.title')}</h1>
          <p>{t('dispatch.sub')}</p>
        </div>
        <div className="act">
          <div className="disp-date">
            <label htmlFor="dispatch-date">{t('dispatch.date')}</label>
            <input id="dispatch-date" type="date" value={date} onChange={(e) => changeDate(e.target.value)} />
            {date !== today && (
              <button type="button" className="btn sm" onClick={() => changeDate(today)}>
                {t('dispatch.todayButton')}
              </button>
            )}
          </div>
          <Can perm="trips.plan">
            <button type="button" className="btn" onClick={() => setModal('planDay')}>
              <IconRoute /> {t('dispatch.planDay.action')}
            </button>
            <button type="button" className="btn" onClick={() => setModal('create')}>
              <IconPlus /> {t('dispatch.create.action')}
            </button>
            <button type="button" className="btn" onClick={() => setModal('reassign')}>
              <IconUsers /> {t('dispatch.reassign.action')}
            </button>
          </Can>
          <Can perm="trips.optimize">
            <button
              type="button"
              className="btn"
              disabled={!actions.optimize || (detail?.stopCount ?? 0) === 0 || optimize.isPending}
              title={detail && !actions.optimize ? t('dispatch.optimize.locked') : (detail?.stopCount ?? 0) === 0 ? t('dispatch.optimize.noStops') : undefined}
              onClick={() => void runOptimize()}
            >
              <IconRoute /> {optimize.isPending ? t('dispatch.optimize.running') : t('dispatch.optimize.action')}
            </button>
          </Can>
          <Can perm="trips.dispatch">
            <button type="button" className="btn flow" onClick={() => setModal('dispatch')}>
              <IconTruck /> {t('dispatch.dispatch.action')}
            </button>
          </Can>
        </div>
      </div>

      {canView && (
        <div className="zonebar">
          <Filters
            label={t('dispatch.filters.label')}
            onClear={hasUnassignedFilters(filters, clientPublicId) ? () => { setFilters(NO_UNASSIGNED_FILTERS); setClientPublicId(null) } : undefined}
          >
            <div className="f">
              <label htmlFor="dispatch-postal">{t('dispatch.filters.postal')}</label>
              <input id="dispatch-postal" type="text" inputMode="numeric" value={filters.postalCode} onChange={(e) => setFilters((x) => ({ ...x, postalCode: e.target.value }))} />
            </div>
            <div className="f">
              <label htmlFor="dispatch-city">{t('dispatch.filters.city')}</label>
              <input id="dispatch-city" type="text" value={filters.city} onChange={(e) => setFilters((x) => ({ ...x, city: e.target.value }))} />
            </div>
            {canClients && catalogOn && (
              <div className="f">
                <label htmlFor="dispatch-client">{t('dispatch.filters.client')}</label>
                <ClientPicker id="dispatch-client" value={clientPublicId} onChange={(id) => setClientPublicId(id)} placeholder={t('dispatch.filters.all')} filterLabel={t('dispatch.filters.client')} />
              </div>
            )}
            {showZoneFilter && (
              <SelectFilter label={t('dispatch.filters.zone')} value={filters.zone} onChange={(zone) => setFilters((x) => ({ ...x, zone }))} options={zoneOptions} allLabel={t('dispatch.filters.all')} />
            )}
          </Filters>
        </div>
      )}

      {detail?.overStopLimit && (
        <div className="alertbanner" role="alert">
          <IconAlert />
          <span>
            <b>{detail.code}</b> {t('dispatch.overLimit')}: {detail.stopCount} &gt; {detail.effectiveMaxStops}
            {source && ` (${t(source === 'driver' ? 'dispatch.maxSource.driver' : 'dispatch.maxSource.tenant')})`}
          </span>
        </div>
      )}

      {!canView ? (
        <EmptyState icon={<IconRoute />} title={t('dispatch.noView')} />
      ) : (
        <SplitPane storageKey="dispatch.main" defaultRatio={0.27} minPx={[270, 540]} label={t('dispatch.resize')} className="disp-cols">
          <UnassignedPanel
            items={items}
            total={unassigned.data?.total ?? 0}
            loading={unassigned.isLoading}
            failed={unassigned.isError}
            filtered={hasUnassignedFilters(settled, settledClient)}
            onRetry={() => void unassigned.refetch()}
            checked={liveChecked}
            onChecked={setChecked}
            addTarget={addTarget}
            adding={add.isPending}
            onAdd={() => void runAdd()}
            problem={addProblem}
            onDismissProblem={() => setAddProblem(null)}
          />
          <SplitPane storageKey="dispatch.side" defaultRatio={0.66} minPx={[340, 260]} label={t('dispatch.resize')} className="disp-cols">
            <div className="disp-col">
              <ActiveRoutePanel
                detail={detail}
                loading={trips.isPending || (!!selectedId && detailQuery.isPending)}
                failed={trips.isError || detailQuery.isError}
                hasTrips={list.length > 0}
                perms={perms}
                canFleet={fleetOn}
                drivers={choices.drivers}
                vehicles={choices.vehicles}
                fleetFailed={choices.isError}
                message={message}
                onMessage={setMessage}
                note={note}
                onDeleted={() => update({ trip: null })}
              />
              <DriversPanel
                drivers={drivers.data ?? []}
                loading={drivers.isLoading}
                failed={drivers.isError}
                allowed={fleetOn}
                availability={choices.availability?.drivers ?? []}
              />
            </div>
            <TodayRoutesPanel trips={list} loading={trips.isPending} failed={trips.isError} selectedId={selectedId} progress={progress} onSelect={(id) => update({ trip: id })} />
          </SplitPane>
        </SplitPane>
      )}

      {modal === 'create' && (
        <TripCreateModal
          date={date}
          zones={zoneList}
          canFleet={fleetOn}
          onCreated={(publicId, planDate) => update({ date: planDate, trip: publicId })}
          onClose={() => setModal(null)}
        />
      )}
      {modal === 'planDay' && <PlanDayModal date={date} zones={zoneList} canFleet={fleetOn} onPlanned={(planDate) => planDate !== date && update({ date: planDate, trip: null })} onClose={() => setModal(null)} />}
      {modal === 'reassign' && <ReassignZoneModal date={date} zones={zoneList} canFleet={fleetOn} onDone={(planDate) => planDate !== date && update({ date: planDate, trip: null })} onClose={() => setModal(null)} />}
      {modal === 'dispatch' && <DispatchModal date={date} preselect={detail?.publicId ?? null} onClose={() => setModal(null)} />}
    </div>
  )
}
