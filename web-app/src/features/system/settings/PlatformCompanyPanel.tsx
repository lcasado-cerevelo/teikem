// Panel «Solo la plataforma» de Ajustes → General (2026-10-26): la dirección web de la compañía (dominio propio y subdominio: de ella sale el logo de la pantalla de entrada) y activarla o
// desactivarla. Solo lo ve el administrador de la plataforma. Desactivar no borra nada: nadie entra (ni cambia a esa compañía) y se cierran las sesiones de las demás personas; la plataforma
// puede entrar para volver a activarla.
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useSession } from '../../../app/session'
import { useT } from '../../../kernel/i18n'
import { ConfirmDialog, Field, Form, IconGear, Panel, TextInput, toast } from '../../../kernel/ui'
import { useSetTenantActive, useSetTenantDomains, type TenantSettingsDto } from '../tenantSettingsApi'

interface DomainValues {
  customDomain: string
  subdomain: string
}

export function PlatformCompanyPanel({ settings }: { settings: TenantSettingsDto }) {
  const t = useT()
  const { reloadMe } = useSession()
  const publicId = settings.publicId ?? ''
  const setDomains = useSetTenantDomains(publicId)
  const setActive = useSetTenantActive(publicId)
  const [confirming, setConfirming] = useState(false)
  const form = useForm<DomainValues>({ values: { customDomain: settings.customDomain ?? '', subdomain: settings.subdomain ?? '' } })
  const dirty = form.formState.isDirty
  const active = settings.isActive !== false

  return (
    <Panel icon={<IconGear />} title={t('system.settings.company.platformTitle')}>
      <p className="set-d" style={{ marginBottom: 12 }}>
        {t('system.settings.company.platformHint')}
      </p>
      <Form
        form={form}
        onSubmit={async (v) => {
          await setDomains.mutateAsync({ customDomain: v.customDomain.trim(), subdomain: v.subdomain.trim() })
          toast.success(t('system.settings.saved'))
        }}
      >
        <div className="r2">
          <Field name="customDomain" label={t('system.settings.company.domain')} help={t('system.settings.company.domainHelp')}>
            <TextInput maxLength={253} className="mono" placeholder="logistica.miempresa.com" autoCapitalize="none" />
          </Field>
          <Field name="subdomain" label={t('system.settings.company.subdomain')} help={t('system.settings.company.subdomainHelp')}>
            <TextInput maxLength={63} className="mono" placeholder="miempresa" autoCapitalize="none" />
          </Field>
        </div>
        <div className="set-actions">
          {dirty && <p className="set-d">{t('system.settings.unsaved')}</p>}
          <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
            {t('system.settings.discard')}
          </button>
          <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.company.saveDomains')}
          </button>
        </div>
      </Form>

      <h3 className="set-sub">{t('system.settings.company.activeTitle')}</h3>
      <p className={active ? 'set-d' : 'note'} role="status" data-testid="company-status">
        {active ? t('system.settings.company.activeOn') : t('system.settings.company.activeOff')}
      </p>
      <div className="set-actions">
        <button type="button" className={active ? 'btn danger' : 'btn flow'} data-testid="company-active-toggle" onClick={() => setConfirming(true)}>
          {active ? t('system.settings.company.deactivate') : t('system.settings.company.activate')}
        </button>
      </div>
      <ConfirmDialog
        open={confirming}
        tone={active ? 'danger' : 'flow'}
        title={active ? t('system.settings.company.deactivateTitle', { name: settings.name ?? '' }) : t('system.settings.company.activateTitle', { name: settings.name ?? '' })}
        message={active ? t('system.settings.company.deactivateMessage') : t('system.settings.company.activateMessage')}
        confirmLabel={active ? t('system.settings.company.deactivate') : t('system.settings.company.activate')}
        onConfirm={async () => {
          await setActive.mutateAsync(!active)
          await reloadMe?.()
          toast.success(active ? t('system.settings.company.deactivated') : t('system.settings.company.activated'))
        }}
        onClose={() => setConfirming(false)}
      />
    </Panel>
  )
}
