// Ajustes → General (maqueta `tenantProfilePanelHtml`): datos de la compañía (nombre, razón social, ID fiscal, idioma por defecto y teléfono), su domicilio y su dirección postal, y el enlace a
// Seguridad y auditoría (MFA, reautenticación y sesiones viven allá, no aquí). 2026-10-26: el NOMBRE lo cambia solo el administrador de la plataforma (para los demás sale bloqueado), igual que el
// dominio, el subdominio y activar o desactivar la compañía (panel aparte `PlatformCompanyPanel`, solo plataforma). El domicilio y la dirección postal son localizaciones de la compañía.
import { useForm, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { useT } from '../../../kernel/i18n'
import { Field, Form, IconGear, IconPin, Panel, Select, TextInput, Toggle, toast } from '../../../kernel/ui'
import { IconShield } from '../../../kernel/ui/actionIcons'
import { useSaveTenantSettings, type TenantSettingsDto, type TenantSettingsUpdateRequest } from '../tenantSettingsApi'
import { AddressFields } from './AddressFields'
import { addressHasContent, addressInputOf, addressValuesOf, NAME_MAX, PHONE_MAX, type AddressValues } from './companyForm'
import { PlatformCompanyPanel } from './PlatformCompanyPanel'
import { ReturnSuggestPanel } from './ReturnSuggestPanel'

interface GeneralValues {
  name: string
  legalName: string
  taxId: string
  defaultLangCode: string
  phoneNumber: string
  address: AddressValues
  postalSame: boolean
  postalAddress: AddressValues
}

export function GeneralTab({ settings, canEdit, isPlatformAdmin = false }: { settings: TenantSettingsDto; canEdit: boolean; isPlatformAdmin?: boolean }) {
  const t = useT()
  const navigate = useNavigate()
  const save = useSaveTenantSettings()
  const form = useForm<GeneralValues>({
    values: {
      name: settings.name ?? '',
      legalName: settings.legalName ?? '',
      taxId: settings.taxId ?? '',
      defaultLangCode: (settings.defaultLangCode ?? 'es').toLowerCase(),
      phoneNumber: settings.phoneNumber ?? '',
      address: addressValuesOf(settings.address),
      postalSame: !settings.postalAddress,
      postalAddress: addressValuesOf(settings.postalAddress),
    },
  })
  const dirty = form.formState.isDirty
  const postalSame = useWatch({ control: form.control, name: 'postalSame' })

  return (
    <div className="set-cols general">
      <Panel icon={<IconGear />} title={t('system.settings.general.profileTitle')}>
        <Form
          form={form}
          onSubmit={async (v) => {
            // textos tal cual ('' borra el valor); el nombre solo viaja si la plataforma lo cambió
            const body: TenantSettingsUpdateRequest = { legalName: v.legalName.trim(), taxId: v.taxId.trim(), defaultLangCode: v.defaultLangCode, phoneNumber: v.phoneNumber.trim() }
            if (isPlatformAdmin && v.name.trim() !== '' && v.name.trim() !== settings.name) body.name = v.name.trim()
            if (addressHasContent(v.address)) body.address = addressInputOf(v.address)
            if (v.postalSame) body.postalSameAsAddress = true
            else if (addressHasContent(v.postalAddress)) body.postalAddress = addressInputOf(v.postalAddress)
            await save.mutateAsync(body)
            toast.success(t('system.settings.saved'))
          }}
        >
          <fieldset className="set-fs" disabled={!canEdit}>
            {isPlatformAdmin ? (
              <Field name="name" label={t('system.settings.general.nameLabel')} help={t('system.settings.company.nameHelpPlatform')} required>
                <TextInput maxLength={NAME_MAX} autoComplete="organization" />
              </Field>
            ) : (
              <div className="f">
                <label htmlFor="set-name">{t('system.settings.general.nameLabel')}</label>
                <input id="set-name" value={settings.name ?? ''} disabled readOnly aria-describedby="set-name-help" />
                <p id="set-name-help" className="help">
                  {t('system.settings.general.nameHint')}
                </p>
              </div>
            )}
            <div className="r2">
              <Field name="legalName" label={t('system.settings.general.legalLabel')}>
                <TextInput maxLength={200} autoComplete="organization" />
              </Field>
              <Field name="taxId" label={t('system.settings.general.taxLabel')}>
                <TextInput maxLength={50} className="mono" />
              </Field>
            </div>
            <div className="r2">
              <Field name="phoneNumber" label={t('system.settings.company.phone')} help={t('system.settings.company.phoneHelp')}>
                <TextInput maxLength={PHONE_MAX} type="tel" autoComplete="tel" />
              </Field>
              <Field name="defaultLangCode" label={t('system.settings.general.langLabel')}>
                <Select
                  options={[
                    { value: 'es', label: t('system.settings.general.langs.es') },
                    { value: 'en', label: t('system.settings.general.langs.en') },
                  ]}
                />
              </Field>
            </div>
            <p className="set-d">{t('system.settings.general.langHint')}</p>
          </fieldset>

          <h3 className="set-sub">
            <IconPin /> {t('system.settings.company.addressTitle')}
          </h3>
          <p className="set-d">{t('system.settings.company.addressHint')}</p>
          <fieldset className="set-fs" disabled={!canEdit}>
            <AddressFields prefix="address" />
            <Field name="postalSame" label={t('system.settings.company.postalTitle')} help={t('system.settings.company.postalHelp')}>
              <Toggle text={postalSame ? t('system.settings.company.postalSame') : t('system.settings.company.postalOther')} />
            </Field>
            {!postalSame && <AddressFields prefix="postalAddress" />}
          </fieldset>

          {canEdit && (
            <div className="set-actions">
              {dirty && <p className="set-d">{t('system.settings.unsaved')}</p>}
              <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
                {t('system.settings.discard')}
              </button>
              <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
                {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
              </button>
            </div>
          )}
        </Form>
      </Panel>
      {isPlatformAdmin && <PlatformCompanyPanel settings={settings} />}
      <ReturnSuggestPanel canEdit={canEdit} />
      <Panel icon={<IconShield />} title={t('system.settings.general.secLinkTitle')}>
        <p className="set-d" style={{ marginBottom: 12 }}>
          {t('system.settings.general.secLinkHint')}
        </p>
        <button type="button" className="btn" onClick={() => navigate('/system/audit?tab=sessions')}>
          {t('system.settings.general.secLinkBtn')}
        </button>
      </Panel>
    </div>
  )
}
