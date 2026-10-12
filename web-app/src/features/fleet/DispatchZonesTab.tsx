// Pestaña «Zonas de despacho» (manual 04 §2.7): CRUD mínimo de zonas (código, nombre, choferes, activo). El código se fija al crear la zona;
// solo el nombre se edita. Inactivar una zona con choferes asignados es 409 (se muestra el mensaje del servidor dentro del diálogo);
// los miembros de la zona (municipios y códigos postales) quedan para Despacho. Lee `fleet.view`; escribe `fleet.manage`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Field, FilterScope, IconEdit, IconPlus, IconPower, IconRotateCcw, matchesQ, Panel, QBox, Spinner, TextInput, toast, type DataColumn } from '../../kernel/ui'
import { IconRoute } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { useDispatchZones, type DispatchZone } from '../drivers/api'
import { useZoneMutations } from './api'
import { buildZoneCreate, emptyZoneValues, zoneSchema, type ZoneValues } from './fleetRules'

const NO_ROWS: DispatchZone[] = []

function ZoneModal({ zone, onClose }: { zone: DispatchZone | null; onClose: () => void }) {
  const t = useT()
  const { create, rename } = useZoneMutations()
  const schema = useMemo(() => zoneSchema(t, { creating: !zone }), [t, zone])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: zone ? { code: zone.code ?? '', name: zone.name ?? '' } : emptyZoneValues() })

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
                { key: 'deactivate', label: t('fleet.zones.deactivate'), icon: <IconPower />, tone: 'danger', onClick: (z) => setDeactivating(z), visible: (z) => canManage && z.isActive },
                { key: 'reactivate', label: t('fleet.zones.reactivate'), icon: <IconRotateCcw />, onClick: (z) => void reactivate(z), visible: (z) => canManage && !z.isActive },
              ]}
            />
          )}
        </div>
        {editing && <ZoneModal key={editing.zone?.id ?? 'new'} zone={editing.zone} onClose={() => setEditing(null)} />}
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
