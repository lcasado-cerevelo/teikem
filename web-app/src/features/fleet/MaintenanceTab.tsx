// Pestaña «Mantenimiento» (maqueta `flota()`, panel «Mantenimiento preventivo»; manual 04 §5). Arriba, el estado de cada programa por vehículo
// (Vehículo con placa y código, Base, Próximo y Estado: Al día / Por vencer / Vencido / Sin historial) que calcula el servidor en
// `GET /maintenance-schedules/due`, con filtros por estado y vehículo y buscador. Abajo, la gestión de los PROGRAMAS (alta, edición,
// desactivar y reactivar; nunca se borran). Sin `fleet.maintenance` todo se consulta y no se pinta ninguna acción.
import { useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  FilterScope,
  Filters,
  IconEdit,
  IconGear,
  IconPlus,
  IconPower,
  IconRotateCcw,
  matchesQ,
  Panel,
  QBox,
  SelectFilter,
  Spinner,
  toast,
  type DataColumn,
} from '../../kernel/ui'
import { IconClip } from '../../kernel/ui/screenIcons'
import { useDue, useSchedules, useScheduleMutations, type MaintenanceDue, type MaintenanceSchedule } from './maintenanceApi'
import { buildDueQuery, dueChipInfo, DUE_STATE_ORDER, dueNextText, dueRemainingText } from './maintenanceRules'
import { VehicleCell } from './maintenanceShared'
import { useVehicleChoices, vehicleText } from './vehicleChoices'
import { ScheduleModal } from './ScheduleModal'
import { WorkOrderCreateModal } from './WorkOrderCreateModal'

const NO_DUE: MaintenanceDue[] = []
const NO_SCHEDULES: MaintenanceSchedule[] = []

/** Panel «Mantenimiento preventivo»: el estado de cada programa por vehículo. */
function DuePanel() {
  const t = useT()
  const f = useFormat()
  const canMaintain = useCan('fleet.maintenance')
  const vehicles = useVehicleChoices()
  const [state, setState] = useState('')
  const [vehicle, setVehicle] = useState('')
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState<MaintenanceDue | null>(null)
  const due = useDue(useMemo(() => buildDueQuery({ state, vehicle }), [state, vehicle]))

  const fmt = useMemo(() => ({ number: (n: number) => f.number(n), date: (iso: string) => f.date(iso) }), [f])
  const rows = useMemo(
    () =>
      (due.data ?? NO_DUE).filter((r) =>
        matchesQ(q, r.vehicleCode, vehicles.plateOf(r.vehiclePublicId), r.scheduleName, t(`fleet.maintenance.basisValue.${(r.triggerCode ?? '').toUpperCase()}`), t(dueChipInfo(r.state).key), r.lastWorkOrderNumber),
      ),
    [due.data, q, vehicles, t],
  )

  const stateOptions = useMemo(() => DUE_STATE_ORDER.map((s) => ({ value: s, label: t(dueChipInfo(s).key) })), [t])

  const columns = useMemo<DataColumn<MaintenanceDue>[]>(
    () => [
      {
        id: 'vehicle',
        header: t('fleet.maintenance.vehicle'),
        cell: (r) => <VehicleCell plate={vehicles.plateOf(r.vehiclePublicId)} code={r.vehicleCode} />,
        sortValue: (r) => vehicles.plateOf(r.vehiclePublicId) || r.vehicleCode || '',
        exportValue: (r) => vehicleText(vehicles.plateOf(r.vehiclePublicId), r.vehicleCode),
        card: 'title',
      },
      { id: 'program', header: t('fleet.maintenance.program'), cell: (r) => r.scheduleName, sortValue: (r) => r.scheduleName ?? '' },
      {
        id: 'basis',
        header: t('fleet.maintenance.basis'),
        cell: (r) => t(`fleet.maintenance.basisValue.${(r.triggerCode ?? '').toUpperCase()}`),
        sortValue: (r) => r.triggerCode ?? '',
      },
      {
        id: 'next',
        header: t('fleet.maintenance.next'),
        cell: (r) => {
          const remaining = dueRemainingText(r, t, fmt)
          return (
            <>
              <span className="mono">{dueNextText(r, fmt)}</span>
              {remaining ? <span className="meta flt-sub">{remaining}</span> : null}
            </>
          )
        },
        exportValue: (r) => [dueNextText(r, fmt), dueRemainingText(r, t, fmt)].filter(Boolean).join(' — '),
        sortValue: (r) => r.nextDueDate ?? (r.nextDueKm != null ? String(r.nextDueKm).padStart(14, '0') : ''),
      },
      {
        id: 'state',
        header: t('fleet.maintenance.stateColumn'),
        cell: (r) => {
          const info = dueChipInfo(r.state)
          return (
            <Chip tone={info.tone} title={info.severity === 1 ? t('fleet.maintenance.noBaselineTitle') : undefined}>
              {t(info.key)}
            </Chip>
          )
        },
        exportValue: (r) => t(dueChipInfo(r.state).key),
        sortValue: (r) => dueChipInfo(r.state).severity,
      },
    ],
    [t, vehicles, fmt],
  )

  const clear = () => {
    setState('')
    setVehicle('')
    setQ('')
  }

  return (
    <FilterScope>
      <Panel flush icon={<IconGear />} title={t('fleet.maintenance.title')} badge={due.isPending ? undefined : rows.length}>
        <div className="pb" style={{ paddingBottom: 0 }}>
          <p className="help" style={{ marginTop: 0 }}>
            {t('fleet.maintenance.help')}
          </p>
          <div className="flt-bar">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
          </div>
          <Filters onClear={clear}>
            <SelectFilter label={t('fleet.maintenance.stateFilter')} value={state} onChange={setState} options={stateOptions} allLabel={t('fleet.maintenance.allStates')} />
            <SelectFilter label={t('fleet.maintenance.vehicleFilter')} value={vehicle} onChange={setVehicle} options={vehicles.options} allLabel={t('fleet.maintenance.allVehicles')} />
          </Filters>
        </div>
        <div className="pb" style={{ paddingTop: 10 }}>
          {due.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(due.error).title}
            </p>
          ) : due.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('fleet.maintenance.title')}
              columns={columns}
              rows={rows}
              rowKey={(r) => `${r.scheduleId}-${r.vehiclePublicId}`}
              defaultSort={{ id: 'state', desc: true }}
              empty={q || state || vehicle ? t('fleet.maintenance.noResults') : t('fleet.maintenance.noneDue')}
              rowActions={[
                {
                  key: 'order',
                  label: t('fleet.maintenance.createOrder'),
                  icon: <IconClip />,
                  onClick: (r) => setCreating(r),
                  visible: () => canMaintain,
                },
              ]}
            />
          )}
        </div>
      </Panel>
      {creating && (
        <WorkOrderCreateModal
          init={{ vehiclePublicId: creating.vehiclePublicId ?? '', scheduleId: String(creating.scheduleId ?? '') }}
          onClose={() => setCreating(null)}
          onCreated={() => setCreating(null)}
        />
      )}
    </FilterScope>
  )
}

/** Panel «Programas de mantenimiento»: alta, edición, desactivar y reactivar. */
function SchedulesPanel() {
  const t = useT()
  const f = useFormat()
  const canMaintain = useCan('fleet.maintenance')
  const vehicles = useVehicleChoices()
  const { data: triggers = [] } = useLookups('MaintenanceTrigger')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [q, setQ] = useState('')
  const list = useSchedules(includeInactive)
  const { setActive } = useScheduleMutations()
  const [editing, setEditing] = useState<{ schedule: MaintenanceSchedule | null } | null>(null)
  const [deactivating, setDeactivating] = useState<MaintenanceSchedule | null>(null)

  // textos de las celdas (también sirven para ordenar y exportar)
  const text = useMemo(
    () => ({
      basis: (s: MaintenanceSchedule) => {
        const code = (s.triggerCode ?? '').toUpperCase()
        return triggers.find((o) => o.code.toUpperCase() === code)?.label ?? t(`fleet.maintenance.basisValue.${code}`)
      },
      target: (s: MaintenanceSchedule) =>
        s.vehicleCode ? [vehicles.plateOf(s.vehiclePublicId), s.vehicleCode].filter(Boolean).join(' · ') : t('fleet.maintenance.programs.typePrefix', { type: s.vehicleType || s.vehicleTypeCode || '—' }),
      interval: (s: MaintenanceSchedule) =>
        [s.intervalKm != null ? `${f.number(s.intervalKm)} km` : '', s.intervalDays != null ? t('fleet.maintenance.programs.days', { days: s.intervalDays }) : ''].filter(Boolean).join(' · ') || '—',
      last: (s: MaintenanceSchedule) => [s.lastServiceKm != null ? `${f.number(s.lastServiceKm)} km` : '', s.lastServiceDate ? f.date(s.lastServiceDate) : ''].filter(Boolean).join(' · ') || '—',
    }),
    [t, f, vehicles, triggers],
  )

  const rows = useMemo(
    () => (list.data ?? NO_SCHEDULES).filter((s) => matchesQ(q, s.name, s.vehicleCode, vehicles.plateOf(s.vehiclePublicId), s.vehicleType, s.vehicleTypeCode, s.trigger, s.triggerCode)),
    [list.data, q, vehicles],
  )

  const reactivate = async (s: MaintenanceSchedule) => {
    try {
      await setActive.mutateAsync({ id: s.id ?? 0, active: true })
      toast.success(t('fleet.maintenance.programs.reactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const columns = useMemo<DataColumn<MaintenanceSchedule>[]>(
    () => [
      { id: 'name', header: t('fleet.maintenance.programs.name'), cell: (s) => <b>{s.name}</b>, sortValue: (s) => s.name ?? '', card: 'title' },
      { id: 'target', header: t('fleet.maintenance.programs.appliesTo'), cell: (s) => text.target(s), sortValue: (s) => text.target(s) },
      { id: 'basis', header: t('fleet.maintenance.basis'), cell: (s) => text.basis(s), sortValue: (s) => text.basis(s) },
      { id: 'interval', header: t('fleet.maintenance.programs.interval'), cell: (s) => <span className="mono">{text.interval(s)}</span>, exportValue: (s) => text.interval(s), sortValue: (s) => s.intervalKm ?? s.intervalDays ?? null },
      { id: 'last', header: t('fleet.maintenance.programs.lastService'), cell: (s) => <span className="mono">{text.last(s)}</span>, exportValue: (s) => text.last(s), sortValue: (s) => s.lastServiceDate ?? '' },
      {
        id: 'active',
        header: t('fleet.maintenance.programs.active'),
        cell: (s) => (s.isActive ? <Chip tone="deliv">{t('fleet.vehicle.active')}</Chip> : <Chip tone="cap">{t('fleet.vehicle.inactive')}</Chip>),
        exportValue: (s) => (s.isActive ? t('fleet.vehicle.active') : t('fleet.vehicle.inactive')),
        sortValue: (s) => (s.isActive ? 1 : 0),
      },
    ],
    [t, text],
  )

  return (
    <FilterScope>
      <Panel
        flush
        icon={<IconGear />}
        title={t('fleet.maintenance.programs.title')}
        badge={list.isPending ? undefined : rows.length}
        actions={
          <Can perm="fleet.maintenance">
            <button type="button" className="btn sm flow" onClick={() => setEditing({ schedule: null })}>
              <IconPlus /> {t('fleet.maintenance.programs.new')}
            </button>
          </Can>
        }
      >
        <div className="pb" style={{ paddingBottom: 0 }}>
          <p className="help" style={{ marginTop: 0 }}>
            {t('fleet.maintenance.programs.help')}
          </p>
          <div className="flt-bar">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
            <label className="sw">
              <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('fleet.maintenance.programs.showInactive')}</span>
            </label>
          </div>
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
              label={t('fleet.maintenance.programs.title')}
              columns={columns}
              rows={rows}
              rowKey={(s) => String(s.id)}
              defaultSort={{ id: 'name', desc: false }}
              rowClassName={(s) => (s.isActive ? undefined : 'dim')}
              empty={t('fleet.maintenance.programs.empty')}
              rowActions={[
                { key: 'edit', label: t('fleet.edit'), icon: <IconEdit />, onClick: (s) => setEditing({ schedule: s }), visible: () => canMaintain },
                { key: 'deactivate', label: t('fleet.maintenance.programs.deactivate'), icon: <IconPower />, tone: 'danger', onClick: (s) => setDeactivating(s), visible: (s) => canMaintain && s.isActive === true },
                { key: 'reactivate', label: t('fleet.maintenance.programs.reactivate'), icon: <IconRotateCcw />, onClick: (s) => void reactivate(s), visible: (s) => canMaintain && s.isActive !== true },
              ]}
            />
          )}
        </div>
        {editing && <ScheduleModal key={editing.schedule?.id ?? 'new'} schedule={editing.schedule} vehicles={vehicles} onClose={() => setEditing(null)} />}
        <ConfirmDialog
          open={deactivating !== null}
          tone="danger"
          title={t('fleet.maintenance.programs.deactivateTitle')}
          message={t('fleet.maintenance.programs.deactivateBody', { name: deactivating?.name ?? '' })}
          confirmLabel={t('fleet.maintenance.programs.deactivate')}
          onConfirm={async () => {
            if (!deactivating) return
            await setActive.mutateAsync({ id: deactivating.id ?? 0, active: false })
            toast.success(t('fleet.maintenance.programs.deactivated'))
          }}
          onClose={() => setDeactivating(null)}
        />
      </Panel>
    </FilterScope>
  )
}

export function MaintenanceTab() {
  return (
    <div className="flt-stack">
      <DuePanel />
      <SchedulesPanel />
    </div>
  )
}
