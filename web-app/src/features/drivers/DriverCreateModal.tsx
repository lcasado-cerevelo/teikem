// «Nuevo chofer» (maqueta `renderDriverModalHtml`): Código, Nombre, Zona, Tope de paradas y Fecha de alta (más «Usuario vinculado»
// solo con `admin.users`). El «Área» de la maqueta es el nombre de la zona elegida y no se captura. POST /drivers; los errores del
// servidor salen bajo cada campo (código repetido 409 «Ya existe un chofer con ese código.», zona inactiva 400…).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { useTenantSettings } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, NumberInput, Select, TextInput, toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import { useCreateDriver, useDispatchZones, useLinkableUsers } from './api'
import { buildDriverCreate, driverSchema, emptyDriverValues, linkableUsers, type DriverValues } from './driverRules'
import { zoneOptions } from './zoneOptions'

export function DriverCreateModal({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: (publicId: string) => void }) {
  const t = useT()
  const create = useCreateDriver()
  const canLinkUser = useCan('admin.users')
  const zones = useDispatchZones()
  const users = useLinkableUsers(canLinkUser && open)
  const { data: settings } = useTenantSettings()
  const schema = useMemo(() => driverSchema(t, { creating: true }), [t])
  const initial = useMemo(() => emptyDriverValues(), [])
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })

  const close = () => {
    form.reset(initial, { keepDirtyValues: false })
    onClose()
  }

  return (
    <FormModal
      id="driver-create"
      title={t('drivers.new')}
      open={open}
      size="md"
      onClose={close}
      form={form}
      onSubmit={async (v: DriverValues) => {
        const created = await create.mutateAsync(buildDriverCreate(v, { canLinkUser }))
        toast.success(t('drivers.created'))
        form.reset(initial, { keepDirtyValues: false })
        onClose()
        if (created.publicId) onSaved(created.publicId)
      }}
    >
      <div className="r2">
        <Field name="code" label={t('drivers.code')} required help={t('drivers.codeHelp')}>
          <TextInput className="mono" placeholder="000" maxLength={60} autoComplete="off" />
        </Field>
        <Field name="fullName" label={t('drivers.name')} required>
          <TextInput autoComplete="off" />
        </Field>
      </div>
      <div className="r2">
        <Field name="dispatchZoneId" label={t('drivers.zone')}>
          <Select options={zoneOptions(zones.data)} placeholder={t('drivers.noZone')} />
        </Field>
        <Field name="maxStopsPerRoute" label={t('drivers.maxStops')} help={t('drivers.maxStopsHelp')}>
          <NumberInput className="mono" min={1} step={1} placeholder={settings?.maxStopsPerRouteDefault != null ? String(settings.maxStopsPerRouteDefault) : undefined} />
        </Field>
      </div>
      <div className="r2">
        <Field name="hireDate" label={t('drivers.hireDate')}>
          <DateInput />
        </Field>
        {canLinkUser && (
          <Field name="userId" label={t('drivers.user')} help={t('drivers.userHelp')}>
            <Select options={linkableUsers(users.data, null)} placeholder={t('drivers.noUser')} />
          </Field>
        )}
      </div>
    </FormModal>
  )
}
