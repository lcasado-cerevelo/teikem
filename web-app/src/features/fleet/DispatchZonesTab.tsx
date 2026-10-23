// Pestaña «Zonas de despacho» (manual 04 §2.7): CRUD mínimo de zonas (código, nombre, choferes, activo). El código se fija al crear la zona;
// solo el nombre se edita. Inactivar una zona con choferes asignados es 409 (se muestra el mensaje del servidor dentro del diálogo);
// los miembros de la zona (municipios y códigos postales) quedan para Despacho. Lee `fleet.view`; escribe `fleet.manage`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Field, FilterScope, IconEdit, IconPlus, IconPower, IconRotateCcw, matchesQ, Panel, QBox, Select, Spinner, TextInput, toast, type DataColumn } from '../../kernel/ui'
import { IconRoute } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { useDispatchZones, type DispatchZone } from '../drivers/api'
import { useZoneGroupMutations, useZoneGroups, useZoneMutations, type ZoneGroup } from './api'
import { useDrivers } from '../drivers/api'
import { buildZoneCreate, emptyZoneValues, zoneSchema, type ZoneValues } from './fleetRules'
import { UnresolvedZonesPanel } from './UnresolvedZonesPanel'
import { ResponsiblesModal } from '../messages/Responsibles'
import { useMessages } from '../messages/MessagesProvider'

const NO_ROWS: DispatchZone[] = []

function ZoneModal({ zone, onClose }: { zone: DispatchZone | null; onClose: () => void }) {
  const t = useT()
  const { create, rename, setLevel } = useZoneMutations()
  const groups = useZoneGroups()
  const groupOptions = useMemo(() => (groups.data ?? []).map((g) => ({ value: String(g.id), label: g.name || g.code || '' })), [groups.data])
  const schema = useMemo(() => zoneSchema(t, { creating: !zone }), [t, zone])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: zone ? { code: zone.code ?? '', name: zone.name ?? '', zoneGroupId: zone.zoneGroupId ? String(zone.zoneGroupId) : '' } : emptyZoneValues() })

  return (
    <FormModal
      id="dispatch-zone"
      title={zone ? t('fleet.zones.edit') : t('fleet.zones.new')}
      open
      onClose={onClose}
      form={form}
      onSubmit={async (v: ZoneValues) => {
        if (zone) {
          if (v.name.trim() !== (zone.name ?? '')) await rename.mutateAsync({ id: zone.id, name: v.name.trim() })
          const level = v.zoneGroupId ? Number(v.zoneGroupId) : null
          if (level !== (zone.zoneGroupId ?? null)) await setLevel.mutateAsync({ id: zone.id, zoneGroupId: level })
        } else await create.mutateAsync(buildZoneCreate(v))
        toast.success(t(zone ? 'fleet.zones.saved' : 'fleet.zones.created'))
        onClose()
      }}
    >
      {zone ? (
        <div className="f">
          <label htmlFor="flt-zone-code">{t('fleet.zones.code')}</label>
          <input id="flt-zone-code" className="mono" value={zone.code ?? ''} disabled readOnly />
        </div>
      ) : (
        <Field name="code" label={t('fleet.zones.code')} required help={t('fleet.zones.codeHelp')}>
          <TextInput className="mono" placeholder="Z-01" maxLength={60} autoComplete="off" />
        </Field>
      )}
      <Field name="name" label={t('fleet.zones.name')} help={t('fleet.zones.nameHelp')}>
        <TextInput autoComplete="off" />
      </Field>
      <Field name="zoneGroupId" label={t('fleet.zones.level')} help={t('fleet.zones.levelHelp')}>
        <Select options={groupOptions} placeholder={t('fleet.zones.noLevel')} />
      </Field>
    </FormModal>
  )
}

export function DispatchZonesTab() {
  const t = useT()
  const canManage = useCan('fleet.manage')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [q, setQ] = useState('')
  const list = useDispatchZones(includeInactive)
  const { setActive } = useZoneMutations()
  const [editing, setEditing] = useState<{ zone: DispatchZone | null } | null>(null)
  const [deactivating, setDeactivating] = useState<DispatchZone | null>(null)
  const [responsibleFor, setResponsibleFor] = useState<DispatchZone | null>(null)
  const messaging = useMessages().enabled
  // el buscador filtra lo cargado (código y nombre), sin acentos ni mayúsculas
  const rows = useMemo(() => (list.data ?? NO_ROWS).filter((z) => matchesQ(q, z.code, z.name)), [list.data, q])

  const reactivate = async (z: DispatchZone) => {
    try {
      await setActive.mutateAsync({ id: z.id, active: true })
      toast.success(t('fleet.zones.reactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const columns = useMemo<DataColumn<DispatchZone>[]>(
    () => [
      { id: 'code', header: t('fleet.zones.code'), cell: (z) => <span className="ref">{z.code}</span>, sortValue: (z) => z.code ?? '', card: 'title' },
      { id: 'name', header: t('fleet.zones.name'), cell: (z) => z.name || '—', sortValue: (z) => z.name ?? '' },
      { id: 'level', header: t('fleet.zones.level'), cell: (z) => (z.zoneGroupName ? <Chip tone="neutral">{z.zoneGroupName}</Chip> : '—'), exportValue: (z) => z.zoneGroupName ?? '', sortValue: (z) => z.zoneGroupName ?? '' },
      { id: 'drivers', header: t('fleet.zones.drivers'), align: 'end', cell: (z) => <span className="mono">{z.driverCount}</span>, sortValue: (z) => z.driverCount },
      {
        id: 'active',
        header: t('fleet.zones.active'),
        cell: (z) => (z.isActive ? <Chip tone="deliv">{t('fleet.vehicle.active')}</Chip> : <Chip tone="cap">{t('fleet.vehicle.inactive')}</Chip>),
        exportValue: (z) => (z.isActive ? t('fleet.vehicle.active') : t('fleet.vehicle.inactive')),
        sortValue: (z) => (z.isActive ? 1 : 0),
      },
    ],
    [t],
  )

  return (
    <FilterScope>
      <UnresolvedZonesPanel />
      <ZoneLevelsPanel />
      <Panel
        flush
        icon={<IconRoute />}
        title={t('fleet.zones.title')}
        badge={list.isPending ? undefined : rows.length}
        actions={
          <Can perm="fleet.manage">
            <button type="button" className="btn sm flow" onClick={() => setEditing({ zone: null })}>
              <IconPlus /> {t('fleet.zones.new')}
            </button>
          </Can>
        }
      >
        <div className="pb" style={{ paddingBottom: 0 }}>
          <p className="help" style={{ marginTop: 0 }}>
            {t('fleet.zones.help')}
          </p>
          <div className="flt-bar">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
            <label className="sw">
              <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('fleet.zones.showInactive')}</span>
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
              label={t('fleet.zones.title')}
              columns={columns}
              rows={rows}
              rowKey={(z) => String(z.id)}
              defaultSort={{ id: 'code', desc: false }}
              rowClassName={(z) => (z.isActive ? undefined : 'dim')}
              empty={t('fleet.zones.empty')}
              rowActions={[
                { key: 'edit', label: t('fleet.edit'), icon: <IconEdit />, onClick: (z) => setEditing({ zone: z }), visible: (z) => canManage && z.isActive },
                { key: 'responsibles', label: t('messages.responsibles.edit'), icon: <IconEdit />, onClick: (z) => setResponsibleFor(z), visible: (z) => canManage && messaging && z.isActive },
                { key: 'deactivate', label: t('fleet.zones.deactivate'), icon: <IconPower />, tone: 'danger', onClick: (z) => setDeactivating(z), visible: (z) => canManage && z.isActive },
                { key: 'reactivate', label: t('fleet.zones.reactivate'), icon: <IconRotateCcw />, onClick: (z) => void reactivate(z), visible: (z) => canManage && !z.isActive },
              ]}
            />
          )}
        </div>
        {editing && <ZoneModal key={editing.zone?.id ?? 'new'} zone={editing.zone} onClose={() => setEditing(null)} />}
        {responsibleFor && <ResponsiblesModal entity="DISPATCH_ZONE" keyId={String(responsibleFor.id)} title={responsibleFor.code ?? ''} onClose={() => setResponsibleFor(null)} />}
        <ConfirmDialog
          open={deactivating !== null}
          tone="danger"
          title={t('fleet.zones.deactivateTitle')}
          message={t('fleet.zones.deactivateBody', { code: deactivating?.code ?? '', name: deactivating?.name || '—' })}
          confirmLabel={t('fleet.zones.deactivate')}
          onConfirm={async () => {
            if (!deactivating) return
            await setActive.mutateAsync({ id: deactivating.id, active: false })
            toast.success(t('fleet.zones.deactivated'))
          }}
          onClose={() => setDeactivating(null)}
        />
      </Panel>
    </FilterScope>
  )
}

// ---------------------------------------------------------------- Lote 129: niveles de zona (Isla/Metro…)

function LevelModal({ level, onClose }: { level: ZoneGroup | null; onClose: () => void }) {
  const t = useT()
  const { create, update } = useZoneGroupMutations()
  const drivers = useDrivers(true)
  const driverOptions = useMemo(() => (drivers.data ?? []).map((d) => ({ value: d.publicId ?? '', label: `${d.code} · ${d.fullName}` })), [drivers.data])
  const form = useForm({
    defaultValues: { code: level?.code ?? '', name: level?.name ?? '', sortOrder: level?.sortOrder ?? 0, defaultDriverPublicId: level?.defaultDriverPublicId ?? '' },
  })
  return (
    <FormModal
      id="zone-level"
      title={level ? t('fleet.levels.edit') : t('fleet.levels.new')}
      open
      onClose={onClose}
      form={form}
      onSubmit={async (v: { code: string; name: string; sortOrder: number; defaultDriverPublicId: string }) => {
        if (!level && !v.code.trim()) {
          form.setError('code', { message: t('fleet.levels.codeRequired') })
          return
        }
        const driver = v.defaultDriverPublicId || null
        if (level) await update.mutateAsync({ id: level.id, body: { name: v.name.trim(), sortOrder: Number(v.sortOrder) || 0, defaultDriverPublicId: driver, clearDefaultDriver: !driver } })
        else await create.mutateAsync({ code: v.code.trim().toUpperCase(), name: v.name.trim(), sortOrder: Number(v.sortOrder) || 0, defaultDriverPublicId: driver })
        toast.success(t(level ? 'fleet.levels.saved' : 'fleet.levels.created'))
        onClose()
      }}
    >
      {level ? (
        <div className="f">
          <label htmlFor="flt-level-code">{t('fleet.levels.code')}</label>
          <input id="flt-level-code" className="mono" value={level.code ?? ''} disabled readOnly />
        </div>
      ) : (
        <Field name="code" label={t('fleet.levels.code')} required help={t('fleet.levels.codeHelp')}>
          <TextInput className="mono" placeholder="METRO" maxLength={20} autoComplete="off" />
        </Field>
      )}
      <Field name="name" label={t('fleet.levels.name')}>
        <TextInput autoComplete="off" />
      </Field>
      <Field name="sortOrder" label={t('fleet.levels.sortOrder')}>
        <TextInput inputMode="numeric" />
      </Field>
      <Field name="defaultDriverPublicId" label={t('fleet.levels.defaultDriver')} help={t('fleet.levels.defaultDriverHelp')}>
        <Select options={driverOptions} placeholder={t('fleet.levels.noDriver')} />
      </Field>
    </FormModal>
  )
}

/** Los niveles de zona de la compañía (Isla/Metro…): código, nombre, orden, chofer por omisión y cuántas zonas tienen. */
export function ZoneLevelsPanel() {
  const t = useT()
  const canManage = useCan('fleet.manage')
  const [includeInactive, setIncludeInactive] = useState(false)
  const list = useZoneGroups(includeInactive)
  const { setActive, assignOrders } = useZoneGroupMutations()
  const [editing, setEditing] = useState<{ level: ZoneGroup | null } | null>(null)
  const [assignResult, setAssignResult] = useState<string | null>(null)
  const toggle = async (g: ZoneGroup) => {
    try {
      await setActive.mutateAsync({ id: g.id, active: !g.isActive })
      toast.success(t(g.isActive ? 'fleet.levels.deactivated' : 'fleet.levels.reactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const assign = async () => {
    try {
      const r = await assignOrders.mutateAsync(false)
      setAssignResult(t('fleet.levels.assignResult', { examined: r.examined ?? 0, assigned: r.assigned ?? 0, unresolved: r.unresolved ?? 0 }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const columns = useMemo<DataColumn<ZoneGroup>[]>(
    () => [
      { id: 'code', header: t('fleet.levels.code'), cell: (g) => <span className="ref">{g.code}</span>, sortValue: (g) => g.code ?? '', card: 'title' },
      { id: 'name', header: t('fleet.levels.name'), cell: (g) => g.name || '—', sortValue: (g) => g.name ?? '' },
      { id: 'driver', header: t('fleet.levels.defaultDriver'), cell: (g) => (g.defaultDriverName ? `${g.defaultDriverCode} · ${g.defaultDriverName}` : '—'), sortValue: (g) => g.defaultDriverName ?? '' },
      { id: 'zones', header: t('fleet.levels.zones'), align: 'end', cell: (g) => <span className="mono">{g.zoneCount}</span>, sortValue: (g) => g.zoneCount },
      { id: 'active', header: t('fleet.zones.active'), cell: (g) => (g.isActive ? <Chip tone="deliv">{t('fleet.vehicle.active')}</Chip> : <Chip tone="cap">{t('fleet.vehicle.inactive')}</Chip>), sortValue: (g) => (g.isActive ? 1 : 0) },
    ],
    [t],
  )
  return (
    <Panel
      flush
      icon={<IconRoute />}
      title={t('fleet.levels.title')}
      badge={list.isPending ? undefined : (list.data ?? []).length}
      actions={
        <Can perm="fleet.manage">
          <button type="button" className="btn sm" disabled={assignOrders.isPending} onClick={() => void assign()}>
            {t('fleet.levels.assignOrders')}
          </button>
          <button type="button" className="btn sm flow" onClick={() => setEditing({ level: null })}>
            <IconPlus /> {t('fleet.levels.new')}
          </button>
        </Can>
      }
    >
      <div className="pb" style={{ paddingBottom: 0 }}>
        <p className="help" style={{ marginTop: 0 }}>
          {t('fleet.levels.help')}
        </p>
        {assignResult && (
          <p className="help" role="status">
            {assignResult}
          </p>
        )}
        <label className="sw">
          <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('fleet.levels.showInactive')}</span>
        </label>
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
            label={t('fleet.levels.title')}
            columns={columns}
            rows={list.data ?? []}
            rowKey={(g) => String(g.id)}
            rowClassName={(g) => (g.isActive ? undefined : 'dim')}
            empty={t('fleet.levels.empty')}
            rowActions={[
              { key: 'edit', label: t('fleet.edit'), icon: <IconEdit />, onClick: (g) => setEditing({ level: g }), visible: (g) => canManage && g.isActive },
              { key: 'deactivate', label: t('fleet.zones.deactivate'), icon: <IconPower />, tone: 'danger', onClick: (g) => void toggle(g), visible: (g) => canManage && g.isActive },
              { key: 'reactivate', label: t('fleet.zones.reactivate'), icon: <IconRotateCcw />, onClick: (g) => void toggle(g), visible: (g) => canManage && !g.isActive },
            ]}
          />
        )}
      </div>
      {editing && <LevelModal key={editing.level?.id ?? 'new'} level={editing.level} onClose={() => setEditing(null)} />}
    </Panel>
  )
}
