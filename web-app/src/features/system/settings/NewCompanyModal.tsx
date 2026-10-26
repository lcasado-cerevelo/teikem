// «Nueva compañía» (solo administrador de la plataforma): una ventana con lo base, sin salir de Ajustes de la compañía. Pide nombre, teléfono (para cartas y documentos), domicilio,
// dirección postal (si no es la misma) y, si ya los tiene, dominio y subdominio. Al aceptar la compañía nace con los módulos base y el sistema entra directo a ella: el resto de la
// configuración (módulos, región, marca…) se termina ahí mismo con las pestañas de siempre.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { useSession } from '../../../app/session'
import { useT } from '../../../kernel/i18n'
import { Field, Form, Modal, TextInput, Toggle, toast } from '../../../kernel/ui'
import { useProvisionTenant } from '../tenantSettingsApi'
import { AddressFields } from './AddressFields'
import { buildProvisionRequest, emptyNewCompany, NAME_MAX, newCompanySchema, PHONE_MAX, type NewCompanyValues } from './companyForm'

export function NewCompanyModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const { switchTenant } = useSession()
  const provision = useProvisionTenant()
  const schema = useMemo(
    () =>
      newCompanySchema({
        nameRequired: t('system.settings.company.new.nameRequired'),
        phoneRequired: t('system.settings.company.new.phoneRequired'),
        line1Required: t('system.settings.company.new.line1Required'),
        cityRequired: t('system.settings.company.new.cityRequired'),
        max: (max) => t('system.settings.company.new.max', { max }),
      }),
    [t],
  )
  const form = useForm<NewCompanyValues>({ resolver: zodResolver(schema), defaultValues: emptyNewCompany() })
  const postalSame = useWatch({ control: form.control, name: 'postalSame' })
  const formId = 'new-company'

  const close = () => {
    form.reset(emptyNewCompany())
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('system.settings.company.new.title')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close} disabled={form.formState.isSubmitting}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting} data-testid="new-company-save">
            {form.formState.isSubmitting ? t('common.loading') : t('system.settings.company.new.create')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const created = await provision.mutateAsync(buildProvisionRequest(v))
          const id = created.tenant?.id
          close()
          toast.success(t('system.settings.company.new.created', { name: created.tenant?.name ?? v.name }))
          // entra directo a la compañía nueva y se queda en Ajustes para terminar de configurarla
          if (id != null) {
            await switchTenant(id)
            navigate('/system/settings')
          }
        }}
      >
        <p className="set-d">{t('system.settings.company.new.intro')}</p>
        <Field name="name" label={t('system.settings.general.nameLabel')} required>
          <TextInput maxLength={NAME_MAX} autoComplete="off" data-testid="new-company-name" />
        </Field>
        <Field name="phoneNumber" label={t('system.settings.company.phone')} help={t('system.settings.company.phoneHelp')} required>
          <TextInput maxLength={PHONE_MAX} type="tel" autoComplete="off" />
        </Field>
        <h3 className="set-sub">{t('system.settings.company.addressTitle')}</h3>
        <AddressFields prefix="address" required />
        <Field name="postalSame" label={t('system.settings.company.postalTitle')}>
          <Toggle text={postalSame ? t('system.settings.company.postalSame') : t('system.settings.company.postalOther')} />
        </Field>
        {!postalSame && <AddressFields prefix="postalAddress" required />}
        <h3 className="set-sub">{t('system.settings.company.platformTitle')}</h3>
        <div className="r2">
          <Field name="customDomain" label={t('system.settings.company.domain')} help={t('system.settings.company.domainHelp')}>
            <TextInput maxLength={253} className="mono" placeholder="logistica.miempresa.com" autoCapitalize="none" />
          </Field>
          <Field name="subdomain" label={t('system.settings.company.subdomain')} help={t('system.settings.company.subdomainHelp')}>
            <TextInput maxLength={63} className="mono" placeholder="miempresa" autoCapitalize="none" />
          </Field>
        </div>
      </Form>
    </Modal>
  )
}
