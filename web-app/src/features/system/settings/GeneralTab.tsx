// Ajustes → General (maqueta `tenantProfilePanelHtml`): datos de la compañía (nombre de solo lectura, razón social, ID fiscal,
// idioma por defecto) y el enlace a Seguridad y auditoría (MFA, reautenticación y sesiones viven allá, no aquí).
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { useT } from '../../../kernel/i18n'
import { Field, Form, IconGear, Panel, Select, TextInput, toast } from '../../../kernel/ui'
import { IconShield } from '../../../kernel/ui/actionIcons'
import { useSaveTenantSettings, type TenantSettingsDto } from '../tenantSettingsApi'

interface GeneralValues {
  legalName: string
  taxId: string
  defaultLangCode: string
}

export function GeneralTab({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const navigate = useNavigate()
  const save = useSaveTenantSettings()
  const form = useForm<GeneralValues>({
    values: {
      legalName: settings.legalName ?? '',
      taxId: settings.taxId ?? '',
      defaultLangCode: (settings.defaultLangCode ?? 'es').toLowerCase(),
    },
  })
  const dirty = form.formState.isDirty

  return (
    <div className="set-cols general">
      <Panel icon={<IconGear />} title={t('system.settings.general.profileTitle')}>
        <Form
          form={form}
          onSubmit={async (v) => {
            // textos tal cual ('' borra el valor); el nombre no se toca desde aquí
            await save.mutateAsync({ legalName: v.legalName.trim(), taxId: v.taxId.trim(), defaultLangCode: v.defaultLangCode })
            toast.success(t('system.settings.saved'))
          }}
        >
          <fieldset className="set-fs" disabled={!canEdit}>
            <div className="f">
              <label htmlFor="set-name">{t('system.settings.general.nameLabel')}</label>
              <input id="set-name" value={settings.name ?? ''} disabled readOnly aria-describedby="set-name-help" />
              <p id="set-name-help" className="help">
                {t('system.settings.general.nameHint')}
              </p>
            </div>
            <div className="r2">
              <Field name="legalName" label={t('system.settings.general.legalLabel')}>
                <TextInput maxLength={200} autoComplete="organization" />
              </Field>
              <Field name="taxId" label={t('system.settings.general.taxLabel')}>
                <TextInput maxLength={50} className="mono" />
              </Field>
            </div>
            <div style={{ maxWidth: 260 }}>
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
      <Panel icon={<IconShield />} title={t('system.settings.general.secLinkTitle')}>
        <p className="set-d" style={{ marginBottom: 12 }}>
          {t('system.settings.general.secLinkHint')}
        </p>
        <button type="button" className="btn" onClick={() => navigate('/system/audit')}>
          {t('system.settings.general.secLinkBtn')}
        </button>
      </Panel>
    </div>
  )
}
