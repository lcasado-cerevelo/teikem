// Sistema → Aparatos móviles (`/system/devices`, Lote F8a P5). `devices.manage` + módulo WMS_LOTSERIAL (los aplica la
// ruta). Sin ficha ni edición de modelo/tema en esta entrega (loteF8-plan.md §3 P5): solo alta, baja/reactivación y
// regenerar el código de registro. Ver api.ts sobre la decisión del código técnico del aparato (Code).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  Field,
  Filters,
  Form,
  IconPower,
  IconRefreshCw,
  matchesQ,
  Modal,
  Panel,
  QBox,
  SelectFilter,
  TextInput,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { WarehousePickerInput } from '../warehouse/pickers'
import { useCreateDevice, useDevices, useDeviceStatusAction, useRegenerateEnrollCode, type DeviceDto } from './devicesApi'
import { EnrollCodeModal } from './EnrollCodeModal'
import { formatRelative } from './devicesFormat'
import { IconGear } from '../../kernel/ui/screenIcons'

// ---- Modal de alta ----
function CreateDeviceModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (enrollCode: string) => void }) {
  const t = useT()
  const create = useCreateDevice()
  const schema = useMemo(
    () =>
      z.object({
        name: z.string().trim().min(1, t('system.devices.errors.nameRequired')),
        defaultWarehousePublicId: z.string().nullable(),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { name: '', defaultWarehousePublicId: null as string | null } })
  const formId = 'device-create'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('system.devices.new')}
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
          const created = await create.mutateAsync({ name: v.name, defaultWarehousePublicId: v.defaultWarehousePublicId })
          close()
          if (created.enrollCode) onCreated(created.enrollCode)
        }}
      >
        <Field name="name" label={t('system.devices.fields.name')} required>
          <TextInput maxLength={100} />
        </Field>
        <Field name="defaultWarehousePublicId" label={t('system.devices.fields.defaultWarehouse')}>
          <WarehousePickerInput />
        </Field>
      </Form>
    </Modal>
  )
}

const NO_ROWS: DeviceDto[] = []

// ---- Pantalla ----
export default function DevicesPage() {
  const t = useT()
  const lang = useLang()
  const [show, setShow] = useState<'active' | 'all'>('active')
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const [enrollCode, setEnrollCode] = useState<string | null>(null)
  const [toToggle, setToToggle] = useState<{ device: DeviceDto; active: boolean } | null>(null)
  const [toRegenerate, setToRegenerate] = useState<DeviceDto | null>(null)

  const { data = NO_ROWS, isLoading, error } = useDevices(show === 'all')
  const status = useDeviceStatusAction()
  const regenerate = useRegenerateEnrollCode()

  const rows = useMemo(() => data.filter((d) => matchesQ(q, d.code, d.name)), [data, q])

  const columns = useMemo<DataColumn<DeviceDto>[]>(
    () => [
      { id: 'code', header: t('system.devices.columns.code'), cell: (d) => <span className="ref">{d.code}</span>, sortValue: (d) => d.code, card: 'title' },
      { id: 'name', header: t('system.devices.columns.name'), cell: (d) => d.name || '—', sortValue: (d) => d.name },
      {
        id: 'warehouse',
        header: t('system.devices.columns.warehouse'),
        cell: (d) => d.defaultWarehouseCode || '—',
        sortValue: (d) => d.defaultWarehouseCode,
      },
      {
        id: 'status',
        header: t('system.devices.columns.status'),
        cell: (d) => (
          <Chip tone={d.isActive ? 'deliv' : 'warn'}>{d.isActive ? t('system.devices.active') : t('system.devices.inactive')}</Chip>
        ),
        sortValue: (d) => d.isActive,
      },
      {
        id: 'lastSeen',
        header: t('system.devices.columns.lastSeen'),
        cell: (d) => formatRelative(d.lastSeenUtc, lang) || '—',
        sortValue: (d) => d.lastSeenUtc ?? '',
      },
      { id: 'appVersion', header: t('system.devices.columns.appVersion'), cell: (d) => <span className="mono">{d.appVersion || '—'}</span>, sortValue: (d) => d.appVersion },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<DeviceDto>[]>(
    () => [
      {
        key: 'deactivate',
        label: t('system.devices.deactivate'),
        perm: 'devices.manage',
        visible: (d) => d.isActive === true,
        tone: 'danger',
        icon: <IconPower />,
        onClick: (d) => setToToggle({ device: d, active: false }),
      },
      {
        key: 'reactivate',
        label: t('system.devices.reactivate'),
        perm: 'devices.manage',
        visible: (d) => d.isActive === false,
        icon: <IconPower />,
        onClick: (d) => setToToggle({ device: d, active: true }),
      },
      {
        key: 'enroll-code',
        label: t('system.devices.newEnrollCode'),
        perm: 'devices.manage',
        visible: (d) => d.isActive === true,
        icon: <IconRefreshCw />,
        onClick: (d) => setToRegenerate(d),
      },
    ],
    [t],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('system.devices.title')}</h1>
          <p>{t('system.devices.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="devices.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('system.devices.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setShow('active')
          setQ('')
        }}
      >
        <SelectFilter
          label={t('system.devices.show')}
          value={show}
          allLabel={null}
          onChange={(v) => setShow(v === 'all' ? 'all' : 'active')}
          options={[
            { value: 'active', label: t('system.devices.onlyActive') },
            { value: 'all', label: t('system.devices.includeInactive') },
          ]}
        />
      </Filters>

      <Panel flush icon={<IconGear />} title={t('system.devices.title')} badge={rows.length}>
        <div className="qrow">
          <QBox value={q} onChange={setQ} placeholder={t('system.devices.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('system.devices.title')}
            columns={columns}
            rows={rows}
            rowKey={(d) => d.publicId ?? d.code ?? ''}
            defaultSort={{ id: 'code', desc: false }}
            pageSize={25}
            loading={isLoading}
            rowActions={actions}
          />
        )}
      </Panel>

      <CreateDeviceModal open={creating} onClose={() => setCreating(false)} onCreated={setEnrollCode} />

      <EnrollCodeModal enrollCode={enrollCode} onClose={() => setEnrollCode(null)} />

      <ConfirmDialog
        open={toToggle !== null}
        tone={toToggle?.active ? 'flow' : 'danger'}
        title={toToggle?.active ? t('system.devices.reactivateTitle') : t('system.devices.deactivateTitle')}
        message={t(toToggle?.active ? 'system.devices.reactivateBody' : 'system.devices.deactivateBody', {
          name: toToggle?.device.name || toToggle?.device.code || '',
        })}
        confirmLabel={toToggle?.active ? t('system.devices.reactivate') : t('system.devices.deactivate')}
        onConfirm={async () => {
          if (!toToggle?.device.publicId) return
          await status.mutateAsync({ action: toToggle.active ? 'reactivate' : 'deactivate', publicId: toToggle.device.publicId })
          toast.success(toToggle.active ? t('system.devices.reactivated') : t('system.devices.deactivated'))
        }}
        onClose={() => setToToggle(null)}
      />

      <ConfirmDialog
        open={toRegenerate !== null}
        title={t('system.devices.newEnrollCode')}
        message={t('system.devices.newEnrollCodeBody', { name: toRegenerate?.name || toRegenerate?.code || '' })}
        confirmLabel={t('system.devices.newEnrollCode')}
        onConfirm={async () => {
          if (!toRegenerate?.publicId) return
          const result = await regenerate.mutateAsync(toRegenerate.publicId)
          if (result.enrollCode) setEnrollCode(result.enrollCode)
        }}
        onClose={() => setToRegenerate(null)}
      />
    </div>
  )
}
