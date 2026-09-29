// Lote F6 — Pantalla A: ficha de almacén (zonas, posiciones y muelles). Lecturas con inventory.view; alta/edición/baja
// de almacén, zonas, posiciones y muelles (y estatus manual del muelle) con warehouse.manage.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, StatusPipeline, useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  type DataColumn,
  type RowAction,
  EmptyState,
  Field,
  Filters,
  Form,
  Modal,
  Panel,
  QBox,
  Select,
  SelectFilter,
  Spinner,
  Tabs,
  TextInput,
  toast,
} from '../../kernel/ui'
import {
  useDeactivateWarehouse,
  useSaveWarehouseBin,
  useSaveWarehouseDock,
  useSaveWarehouseZone,
  useUpdateWarehouse,
  useWarehouse,
  useWarehouseBins,
  useWarehouseDocks,
  useWarehouseZones,
  type WarehouseBinDto,
  type WarehouseDetailDto,
  type WarehouseDockDto,
  type WarehouseZoneDto,
} from './api'
import { IconWarehouse } from '../../kernel/ui/screenIcons'
import { BinModal, ReadOnlyField } from './BinModal'

type TabKey = 'profile' | 'zones' | 'bins' | 'docks'

/** Dominios de estatus y códigos EntityType (CatalogDomains / EntityTypes). */
const WAREHOUSE_STATUS_DOMAIN = 'WarehouseStatus'
const WAREHOUSE_ENTITY_TYPE = 'WAREHOUSE'
const DOCK_STATUS_DOMAIN = 'DockStatus'
const DOCK_ENTITY_TYPE = 'WAREHOUSE_DOCK'

/** Interruptor simple (fuera de un `Form`), para filtros booleanos como `includeInactive`/`onlyWithStock`. */
function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="f">
      <label className="sw">
        <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
    </div>
  )
}

// =====================================================================================================================
// Pestaña Datos
// =====================================================================================================================
function ProfileTab({ detail }: { detail: WarehouseDetailDto }) {
  const t = useT()
  const w = detail.warehouse ?? {}
  const publicId = w.publicId ?? ''
  const canEdit = useCan('warehouse.manage')
  const update = useUpdateWarehouse()
  const { data: countries = [] } = useLookups('Country')

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().trim().min(1, t('warehouse.list.errors.nameRequired')),
        line1: z.string().trim(),
        city: z.string().trim(),
        state: z.string().trim(),
        postalCode: z.string().trim(),
        country: z.string().trim(),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      name: w.name ?? '',
      line1: w.line1 ?? '',
      city: w.city ?? '',
      state: w.state ?? '',
      postalCode: w.postalCode ?? '',
      country: w.countryCode ?? '',
    },
  })

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        await update.mutateAsync({
          publicId,
          body: {
            name: v.name,
            line1: v.line1,
            city: v.city,
            state: v.state,
            postalCode: v.postalCode,
            country: v.country,
            rowVersion: w.rowVersion ?? null,
          },
        })
        toast.success(t('warehouse.detail.saved'))
      }}
    >
      <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
        <div className="r2">
          <ReadOnlyField label={t('warehouse.detail.code')} value={w.code ?? ''} help={t('warehouse.detail.codeHelp')} />
          <Field name="name" label={t('warehouse.detail.name')} required>
            <TextInput />
          </Field>
        </div>
        <Field name="line1" label={t('warehouse.detail.line1')}>
          <TextInput />
        </Field>
        <div className="r3">
          <Field name="city" label={t('warehouse.detail.city')}>
            <TextInput />
          </Field>
          <Field name="state" label={t('warehouse.detail.state')}>
            <TextInput />
          </Field>
          <Field name="postalCode" label={t('warehouse.detail.postalCode')}>
            <TextInput />
          </Field>
        </div>
        <Field name="country" label={t('warehouse.detail.country')}>
          <Select options={countries.map((c) => ({ value: c.code, label: c.label }))} placeholder="" />
        </Field>
      </fieldset>
      <Can perm="warehouse.manage">
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      </Can>
    </Form>
  )
}

// =====================================================================================================================
// Pestaña Zonas
// =====================================================================================================================
function ZoneModal({
  publicId,
  zone,
  open,
  onClose,
}: {
  publicId: string
  zone: WarehouseZoneDto | null
  open: boolean
  onClose: () => void
}) {
  const t = useT()
  const save = useSaveWarehouseZone()
  const { data: zoneTypes = [] } = useLookups('ZoneType')
  const isEdit = zone !== null

  const schema = useMemo(
    () =>
      z.object({
        code: isEdit ? z.string() : z.string().trim().min(1, t('warehouse.zones.errors.codeRequired')),
        name: z.string().trim().min(1, t('warehouse.zones.errors.nameRequired')),
        zoneType: z.string(),
      }),
    [t, isEdit],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: { code: zone?.code ?? '', name: zone?.name ?? '', zoneType: zone?.zoneTypeCode ?? '' },
  })
  const formId = 'warehouse-zone-save'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEdit ? t('warehouse.zones.edit') : t('warehouse.zones.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          if (isEdit && zone) {
            await save.mutateAsync({ publicId, action: 'update', zoneId: zone.id ?? 0, body: { name: v.name, zoneType: v.zoneType || null } })
            toast.success(t('warehouse.zones.saved'))
          } else {
            await save.mutateAsync({ publicId, action: 'create', body: { code: v.code, name: v.name, zoneType: v.zoneType || null } })
            toast.success(t('warehouse.zones.created'))
          }
          close()
        }}
      >
        {isEdit ? (
          <ReadOnlyField label={t('warehouse.zones.code')} value={zone?.code ?? ''} help={t('warehouse.zones.codeHelp')} />
        ) : (
          <Field name="code" label={t('warehouse.zones.code')} required>
            <TextInput />
          </Field>
        )}
        <Field name="name" label={t('warehouse.zones.name')} required>
          <TextInput />
        </Field>
        <Field name="zoneType" label={t('warehouse.zones.type')}>
          <Select options={zoneTypes.map((z2) => ({ value: z2.code, label: z2.label }))} placeholder="" />
        </Field>
      </Form>
    </Modal>
  )
}

function ZonesTab({ publicId }: { publicId: string }) {
  const t = useT()
  const canManage = useCan('warehouse.manage')
  const [includeInactive, setIncludeInactive] = useState(false)
  const { data, isLoading } = useWarehouseZones(publicId, { includeInactive })
  const save = useSaveWarehouseZone()
  const [editing, setEditing] = useState<WarehouseZoneDto | null | 'new'>(null)
  const [confirmAction, setConfirmAction] = useState<{ zone: WarehouseZoneDto; action: 'deactivate' | 'reactivate' } | null>(null)

  const columns = useMemo<DataColumn<WarehouseZoneDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.zones.code'), cell: (z) => <span className="ref">{z.code}</span>, sortValue: (z) => z.code, card: 'title' },
      { id: 'name', header: t('warehouse.zones.name'), cell: (z) => z.name, sortValue: (z) => z.name },
      { id: 'type', header: t('warehouse.zones.type'), cell: (z) => z.zoneType, sortValue: (z) => z.zoneType },
      { id: 'bins', header: t('warehouse.zones.bins'), cell: (z) => z.binCount, sortValue: (z) => z.binCount, align: 'end' },
      {
        id: 'active',
        header: t('warehouse.zones.active'),
        cell: (z) => <Chip tone={z.isActive ? 'deliv' : 'warn'}>{z.isActive ? t('warehouse.zones.active') : t('warehouse.zones.inactive')}</Chip>,
        sortValue: (z) => z.isActive,
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<WarehouseZoneDto>[]>(
    () => [
      { key: 'edit', label: t('warehouse.zones.edit'), perm: 'warehouse.manage', onClick: (z) => setEditing(z) },
      {
        key: 'deactivate',
        label: t('warehouse.zones.deactivate'),
        perm: 'warehouse.manage',
        visible: (z) => z.isActive === true,
        onClick: (z) => setConfirmAction({ zone: z, action: 'deactivate' }),
        tone: 'danger',
      },
      {
        key: 'reactivate',
        label: t('warehouse.zones.reactivate'),
        perm: 'warehouse.manage',
        visible: (z) => z.isActive !== true,
        onClick: (z) => setConfirmAction({ zone: z, action: 'reactivate' }),
      },
    ],
    [t],
  )

  return (
    <>
      <div className="head">
        <div>
          <p>{t('warehouse.zones.count', { count: data?.length ?? 0 })}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('warehouse.zones.new')}
            </button>
          </Can>
        </div>
      </div>
      <Filters onClear={() => setIncludeInactive(false)}>
        <ToggleFilter label={t('warehouse.zones.includeInactive')} checked={includeInactive} onChange={setIncludeInactive} />
      </Filters>
      <Panel flush>
        <DataTable
          label={t('warehouse.zones.title')}
          columns={columns}
          rows={data ?? []}
          rowKey={(z) => z.id ?? 0}
          defaultSort={{ id: 'code', desc: false }}
          loading={isLoading}
          rowActions={canManage ? actions : []}
        />
      </Panel>

      <ZoneModal publicId={publicId} zone={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />

      <ConfirmDialog
        open={confirmAction !== null}
        tone={confirmAction?.action === 'deactivate' ? 'danger' : 'flow'}
        title={confirmAction?.action === 'deactivate' ? t('warehouse.zones.deactivateTitle') : t('warehouse.zones.reactivateTitle')}
        message={t(confirmAction?.action === 'deactivate' ? 'warehouse.zones.deactivateBody' : 'warehouse.zones.reactivateBody', {
          code: confirmAction?.zone.code ?? '',
        })}
        confirmLabel={confirmAction?.action === 'deactivate' ? t('warehouse.zones.deactivate') : t('warehouse.zones.reactivate')}
        onConfirm={async () => {
          if (!confirmAction) return
          await save.mutateAsync({ publicId, action: confirmAction.action, zoneId: confirmAction.zone.id ?? 0 })
          toast.success(confirmAction.action === 'deactivate' ? t('warehouse.zones.deactivated') : t('warehouse.zones.reactivated'))
        }}
        onClose={() => setConfirmAction(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pestaña Posiciones
// =====================================================================================================================
function BinsTab({ publicId, zones }: { publicId: string; zones: readonly WarehouseZoneDto[] }) {
  const t = useT()
  const canManage = useCan('warehouse.manage')
  const [zoneId, setZoneId] = useState('')
  const [search, setSearch] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [onlyWithStock, setOnlyWithStock] = useState(false)
  const query = { zoneId: zoneId ? Number(zoneId) : undefined, search: search || undefined, includeInactive, onlyWithStock }
  const { data, isLoading } = useWarehouseBins(publicId, query)
  const save = useSaveWarehouseBin()
  const [editing, setEditing] = useState<WarehouseBinDto | null | 'new'>(null)
  const [confirmAction, setConfirmAction] = useState<{ bin: WarehouseBinDto; action: 'deactivate' | 'reactivate' } | null>(null)

  const columns = useMemo<DataColumn<WarehouseBinDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.bins.code'), cell: (b) => <span className="ref">{b.code}</span>, sortValue: (b) => b.code, card: 'title' },
      { id: 'zone', header: t('warehouse.bins.zone'), cell: (b) => b.zoneCode, sortValue: (b) => b.zoneCode },
      {
        id: 'location',
        header: t('warehouse.bins.location'),
        cell: (b) => [b.aisle, b.rack, b.level, b.position].filter(Boolean).join(' / '),
        sortValue: (b) => [b.aisle, b.rack, b.level, b.position].filter(Boolean).join(' / '),
      },
      { id: 'maxWeight', header: t('warehouse.bins.maxWeight'), cell: (b) => b.maxWeightKg ?? '—', sortValue: (b) => b.maxWeightKg, align: 'end' },
      { id: 'onHand', header: t('warehouse.bins.onHand'), cell: (b) => b.qtyOnHand, sortValue: (b) => b.qtyOnHand, align: 'end' },
      { id: 'products', header: t('warehouse.bins.products'), cell: (b) => b.productCount, sortValue: (b) => b.productCount, align: 'end' },
      {
        id: 'active',
        header: t('warehouse.bins.active'),
        cell: (b) => <Chip tone={b.isActive ? 'deliv' : 'warn'}>{b.isActive ? t('warehouse.bins.active') : t('warehouse.bins.inactive')}</Chip>,
        sortValue: (b) => b.isActive,
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<WarehouseBinDto>[]>(
    () => [
      { key: 'edit', label: t('warehouse.bins.edit'), perm: 'warehouse.manage', onClick: (b) => setEditing(b) },
      {
        key: 'deactivate',
        label: t('warehouse.bins.deactivate'),
        perm: 'warehouse.manage',
        visible: (b) => b.isActive === true,
        onClick: (b) => setConfirmAction({ bin: b, action: 'deactivate' }),
        tone: 'danger',
      },
      {
        key: 'reactivate',
        label: t('warehouse.bins.reactivate'),
        perm: 'warehouse.manage',
        visible: (b) => b.isActive !== true,
        onClick: (b) => setConfirmAction({ bin: b, action: 'reactivate' }),
      },
    ],
    [t],
  )

  return (
    <>
      <div className="head">
        <div>
          <p>{t('warehouse.bins.count', { count: data?.length ?? 0 })}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('warehouse.bins.new')}
            </button>
          </Can>
        </div>
      </div>
      <Filters
        onClear={() => {
          setZoneId('')
          setSearch('')
          setIncludeInactive(false)
          setOnlyWithStock(false)
        }}
      >
        <SelectFilter
          label={t('warehouse.bins.zone')}
          value={zoneId}
          onChange={setZoneId}
          allLabel={t('warehouse.bins.allZones')}
          options={zones.map((z) => ({ value: String(z.id), label: z.code ?? '' }))}
        />
        <ToggleFilter label={t('warehouse.bins.includeInactive')} checked={includeInactive} onChange={setIncludeInactive} />
        <ToggleFilter label={t('warehouse.bins.onlyWithStock')} checked={onlyWithStock} onChange={setOnlyWithStock} />
      </Filters>
      <Panel flush>
        <div className="qrow">
          <QBox value={search} onChange={setSearch} />
        </div>
        <DataTable
          label={t('warehouse.bins.title')}
          columns={columns}
          rows={data ?? []}
          rowKey={(b) => b.id ?? 0}
          defaultSort={{ id: 'code', desc: false }}
          loading={isLoading}
          rowActions={canManage ? actions : []}
        />
      </Panel>

      <BinModal publicId={publicId} zones={zones} bin={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />

      <ConfirmDialog
        open={confirmAction !== null}
        tone={confirmAction?.action === 'deactivate' ? 'danger' : 'flow'}
        title={confirmAction?.action === 'deactivate' ? t('warehouse.bins.deactivateTitle') : t('warehouse.bins.reactivateTitle')}
        message={t(confirmAction?.action === 'deactivate' ? 'warehouse.bins.deactivateBody' : 'warehouse.bins.reactivateBody', {
          code: confirmAction?.bin.code ?? '',
        })}
        confirmLabel={confirmAction?.action === 'deactivate' ? t('warehouse.bins.deactivate') : t('warehouse.bins.reactivate')}
        onConfirm={async () => {
          if (!confirmAction) return
          await save.mutateAsync({ publicId, action: confirmAction.action, binId: confirmAction.bin.id ?? 0 })
          toast.success(confirmAction.action === 'deactivate' ? t('warehouse.bins.deactivated') : t('warehouse.bins.reactivated'))
        }}
        onClose={() => setConfirmAction(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pestaña Muelles
// =====================================================================================================================
function DockModal({
  publicId,
  dock,
  open,
  onClose,
}: {
  publicId: string
  dock: WarehouseDockDto | null
  open: boolean
  onClose: () => void
}) {
  const t = useT()
  const save = useSaveWarehouseDock()
  const { data: dockTypes = [] } = useLookups('DockType')
  const isEdit = dock !== null

  const schema = useMemo(
    () =>
      z.object({
        code: isEdit ? z.string() : z.string().trim().min(1, t('warehouse.docks.errors.codeRequired')),
        dockType: z.string(),
      }),
    [t, isEdit],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: { code: dock?.code ?? '', dockType: dock?.dockTypeCode ?? '' },
  })
  const formId = 'warehouse-dock-save'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEdit ? t('warehouse.docks.edit') : t('warehouse.docks.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          if (isEdit && dock) {
            await save.mutateAsync({ publicId, action: 'update', dockId: dock.id ?? 0, body: { dockType: v.dockType || null } })
            toast.success(t('warehouse.docks.saved'))
          } else {
            await save.mutateAsync({ publicId, action: 'create', body: { code: v.code, dockType: v.dockType || null } })
            toast.success(t('warehouse.docks.created'))
          }
          close()
        }}
      >
        {isEdit ? (
          <ReadOnlyField label={t('warehouse.docks.code')} value={dock?.code ?? ''} help={t('warehouse.docks.codeHelp')} />
        ) : (
          <Field name="code" label={t('warehouse.docks.code')} required>
            <TextInput />
          </Field>
        )}
        <Field name="dockType" label={t('warehouse.docks.type')}>
          <Select options={dockTypes.map((d) => ({ value: d.code, label: d.label }))} placeholder="" />
        </Field>
      </Form>
    </Modal>
  )
}

function DockStatusModal({ publicId, dock, open, onClose }: { publicId: string; dock: WarehouseDockDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const save = useSaveWarehouseDock()
  const canManage = useCan('warehouse.manage')
  if (!dock) return null
  return (
    <Modal open={open} title={t('warehouse.docks.changeStatus')} onClose={onClose} size="sm">
      <StatusPipeline
        domain={DOCK_STATUS_DOMAIN}
        entityType={DOCK_ENTITY_TYPE}
        entityId={dock.id}
        currentCode={dock.statusCode}
        disabled={!canManage}
        onTransition={(toCode, comment) =>
          save.mutateAsync({ publicId, action: 'status', dockId: dock.id ?? 0, body: { status: toCode, comment: comment ?? null } })
        }
      />
    </Modal>
  )
}

function DocksTab({ publicId }: { publicId: string }) {
  const t = useT()
  const canManage = useCan('warehouse.manage')
  const [includeInactive, setIncludeInactive] = useState(false)
  const { data, isLoading } = useWarehouseDocks(publicId, { includeInactive })
  const save = useSaveWarehouseDock()
  const [editing, setEditing] = useState<WarehouseDockDto | null | 'new'>(null)
  const [statusDock, setStatusDock] = useState<WarehouseDockDto | null>(null)
  const [confirmAction, setConfirmAction] = useState<{ dock: WarehouseDockDto; action: 'deactivate' | 'reactivate' } | null>(null)

  const columns = useMemo<DataColumn<WarehouseDockDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.docks.code'), cell: (d) => <span className="ref">{d.code}</span>, sortValue: (d) => d.code, card: 'title' },
      { id: 'type', header: t('warehouse.docks.type'), cell: (d) => d.dockType, sortValue: (d) => d.dockType },
      {
        id: 'status',
        header: t('warehouse.docks.status'),
        cell: (d) => <StatusChip domain={DOCK_STATUS_DOMAIN} code={d.statusCode} label={d.status} />,
        sortValue: (d) => d.status,
      },
      {
        id: 'active',
        header: t('warehouse.docks.active'),
        cell: (d) => <Chip tone={d.isActive ? 'deliv' : 'warn'}>{d.isActive ? t('warehouse.docks.active') : t('warehouse.docks.inactive')}</Chip>,
        sortValue: (d) => d.isActive,
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<WarehouseDockDto>[]>(
    () => [
      { key: 'edit', label: t('warehouse.docks.edit'), perm: 'warehouse.manage', onClick: (d) => setEditing(d) },
      { key: 'status', label: t('warehouse.docks.changeStatus'), perm: 'warehouse.manage', onClick: (d) => setStatusDock(d) },
      {
        key: 'deactivate',
        label: t('warehouse.docks.deactivate'),
        perm: 'warehouse.manage',
        visible: (d) => d.isActive === true,
        onClick: (d) => setConfirmAction({ dock: d, action: 'deactivate' }),
        tone: 'danger',
      },
      {
        key: 'reactivate',
        label: t('warehouse.docks.reactivate'),
        perm: 'warehouse.manage',
        visible: (d) => d.isActive !== true,
        onClick: (d) => setConfirmAction({ dock: d, action: 'reactivate' }),
      },
    ],
    [t],
  )

  return (
    <>
      <div className="head">
        <div>
          <p>{t('warehouse.docks.count', { count: data?.length ?? 0 })}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('warehouse.docks.new')}
            </button>
          </Can>
        </div>
      </div>
      <Filters onClear={() => setIncludeInactive(false)}>
        <ToggleFilter label={t('warehouse.docks.includeInactive')} checked={includeInactive} onChange={setIncludeInactive} />
      </Filters>
      <Panel flush>
        <DataTable
          label={t('warehouse.docks.title')}
          columns={columns}
          rows={data ?? []}
          rowKey={(d) => d.id ?? 0}
          defaultSort={{ id: 'code', desc: false }}
          loading={isLoading}
          rowActions={canManage ? actions : []}
        />
      </Panel>

      <DockModal publicId={publicId} dock={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />
      <DockStatusModal publicId={publicId} dock={statusDock} open={statusDock !== null} onClose={() => setStatusDock(null)} />

      <ConfirmDialog
        open={confirmAction !== null}
        tone={confirmAction?.action === 'deactivate' ? 'danger' : 'flow'}
        title={confirmAction?.action === 'deactivate' ? t('warehouse.docks.deactivateTitle') : t('warehouse.docks.reactivateTitle')}
        message={t(confirmAction?.action === 'deactivate' ? 'warehouse.docks.deactivateBody' : 'warehouse.docks.reactivateBody', {
          code: confirmAction?.dock.code ?? '',
        })}
        confirmLabel={confirmAction?.action === 'deactivate' ? t('warehouse.docks.deactivate') : t('warehouse.docks.reactivate')}
        onConfirm={async () => {
          if (!confirmAction) return
          await save.mutateAsync({ publicId, action: confirmAction.action, dockId: confirmAction.dock.id ?? 0 })
          toast.success(confirmAction.action === 'deactivate' ? t('warehouse.docks.deactivated') : t('warehouse.docks.reactivated'))
        }}
        onClose={() => setConfirmAction(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function WarehouseDetailScreen() {
  const t = useT()
  const { publicId = '' } = useParams()
  const { data, isLoading, error } = useWarehouse(publicId)
  const canManage = useCan('warehouse.manage')
  const deactivateWarehouse = useDeactivateWarehouse()
  const [tab, setTab] = useState<TabKey>('profile')

  if (isLoading) return <Spinner block />
  if (error || !data?.warehouse) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.detail.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/warehouses">
            {t('warehouse.detail.back')}
          </Link>
        }
      />
    )
  }

  const w = data.warehouse

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{w.code}</span> · {w.name}
          </h1>
          <p>
            {!w.isActive && <Chip tone="fail">{t('warehouse.detail.inactive')}</Chip>} {w.city}
          </p>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <StatusPipeline
          domain={WAREHOUSE_STATUS_DOMAIN}
          entityType={WAREHOUSE_ENTITY_TYPE}
          entityId={w.id}
          currentCode={w.statusCode}
          disabled={!canManage || !w.isActive}
          onTransition={(toCode, comment) => {
            if (toCode !== 'INACTIVE') return Promise.resolve()
            return deactivateWarehouse.mutateAsync({ publicId, body: { comment: comment ?? null, rowVersion: w.rowVersion ?? null } })
          }}
        />
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.list.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'profile', label: t('warehouse.detail.tabProfile') },
            { key: 'zones', label: t('warehouse.detail.tabZones') },
            { key: 'bins', label: t('warehouse.detail.tabBins') },
            { key: 'docks', label: t('warehouse.detail.tabDocks') },
          ]}
        />
      </div>

      {tab === 'profile' && (
        <Panel icon={<IconWarehouse />} title={t('warehouse.detail.tabProfile')}>
          <ProfileTab detail={data} />
        </Panel>
      )}
      {tab === 'zones' && <ZonesTab publicId={publicId} />}
      {tab === 'bins' && <BinsTab publicId={publicId} zones={data.zones ?? []} />}
      {tab === 'docks' && <DocksTab publicId={publicId} />}
    </div>
  )
}
