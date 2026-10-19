// Alta y edición de una integración (Lote 132, P11). Alta: nombre, cliente, plantilla de importación, conector (carpeta de entrada o subida manual), horario, alcances (solo los que quien crea puede dar),
// a quién avisar (con «incluirme») y retención de la bandeja; el servidor crea la cuenta de servicio con su rol. Edición: lo mismo salvo el cliente y el conector.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import { useClientOptions } from '../consignees/api'
import { useOrderImportTemplates } from '../orders/importApi'
import { EmailSection } from './EmailSection'
import { buildCreateRequest, buildPatchRequest, integrationSchema, integrationValuesOf, type IntegrationValues } from './integrationRules'
import { useIntegrationMutations, useIntegrationOptions, type IntegrationDto } from './integrationsApi'

export function IntegrationFormModal({ integration, onClose, onSaved }: { integration: IntegrationDto | null; onClose: () => void; onSaved?: (i: IntegrationDto) => void }) {
  const t = useT()
  const editing = integration !== null
  const { create, update } = useIntegrationMutations()
  const options = useIntegrationOptions(true)
  const clients = useClientOptions(true)
  const schema = useMemo(
    () =>
      integrationSchema(
        {
          nameRequired: t('integrations.form.errors.nameRequired'),
          nameMax: t('integrations.form.errors.nameMax'),
          clientRequired: t('integrations.form.errors.clientRequired'),
          templateRequired: t('integrations.form.errors.templateRequired'),
          folderRequired: t('integrations.form.errors.folderRequired'),
          folderInvalid: t('integrations.form.errors.folderInvalid'),
          interval: t('integrations.form.errors.interval'),
          retention: t('integrations.form.errors.retention'),
          scopes: t('integrations.form.errors.scopes'),
        },
        editing,
        {
          hasSavedPassword: integration?.hasMailPassword === true,
          messages: {
            host: t('integrations.email.errors.host'),
            port: t('integrations.email.errors.port'),
            username: t('integrations.email.errors.username'),
            password: t('integrations.email.errors.password'),
            mailbox: t('integrations.email.errors.mailbox'),
            noFields: t('integrations.email.errors.noFields'),
            position: t('integrations.email.errors.position'),
            label: t('integrations.email.errors.label'),
            pattern: t('integrations.email.errors.pattern'),
            block: t('integrations.email.errors.block'),
            blockColumns: t('integrations.email.errors.blockColumns'),
            nameFilter: t('integrations.email.errors.nameFilter'),
            senders: t('integrations.email.errors.senders'),
          },
        },
      ),
    [t, editing, integration?.hasMailPassword],
  )
  const form = useForm<IntegrationValues>({ resolver: zodResolver(schema), defaultValues: integrationValuesOf(integration) })
  const values = useWatch({ control: form.control })
  const clientId = values.clientPublicId ?? ''
  const templates = useOrderImportTemplates(clientId, false, clientId !== '')
  const connector = values.connectorKind ?? 'FOLDER'

  const clientOptions = (clients.data ?? []).map((c) => ({ value: c.publicId ?? '', label: [c.code, c.name].filter(Boolean).join(' · ') }))
  const templateOptions = (templates.data ?? []).map((x) => ({ value: x.publicId ?? '', label: x.name ?? '' }))
  // al editar, la plantilla actual siempre es una opción (si la lista aún no llegó, el control no se queda en blanco)
  if (integration?.templatePublicId && !templateOptions.some((o) => o.value === integration.templatePublicId)) templateOptions.unshift({ value: integration.templatePublicId, label: integration.templateName ?? '' })
  const scopeList = options.data?.scopes ?? []
  const users = options.data?.users ?? []
  const toggle = (name: 'scopes' | 'notifyUserIds', value: string | number, on: boolean) => {
    const current = (form.getValues(name) as (string | number)[]) ?? []
    const next = on ? [...current.filter((x) => x !== value), value] : current.filter((x) => x !== value)
    form.setValue(name, next as never, { shouldDirty: true, shouldValidate: form.formState.isSubmitted })
  }
  const scopesError = form.formState.errors.scopes?.message

  return (
    <FormModal
      id="integration-form"
      size="md"
      title={editing ? t('integrations.form.editTitle', { name: integration.name ?? '' }) : t('integrations.form.newTitle')}
      open
      onClose={onClose}
      form={form}
      saveLabel={editing ? t('integrations.form.save') : t('integrations.form.create')}
      onSubmit={async (v: IntegrationValues) => {
        const saved = editing
          ? await update.mutateAsync({ publicId: integration.publicId!, body: buildPatchRequest(v, integration) })
          : await create.mutateAsync(buildCreateRequest(v))
        toast.success(t(editing ? 'integrations.form.saved' : 'integrations.form.created', { name: saved.name ?? '' }))
        onSaved?.(saved)
        onClose()
      }}
    >
      <Field name="name" label={t('integrations.form.name')} required>
        <TextInput maxLength={120} placeholder={t('integrations.form.namePlaceholder')} />
      </Field>
      <Field name="description" label={t('integrations.form.description')}>
        <TextInput maxLength={400} />
      </Field>
      {!editing && (
        <Field name="clientPublicId" label={t('integrations.form.client')} required help={t('integrations.form.clientHelp')}>
          <Select options={clientOptions} placeholder={t('integrations.form.pick')} />
        </Field>
      )}
      <Field name="templatePublicId" label={t('integrations.form.template')} required help={t('integrations.form.templateHelp')}>
        <Select options={templateOptions} placeholder={clientId || editing ? t('integrations.form.pick') : t('integrations.form.pickClientFirst')} />
      </Field>
      <div className="r2">
        <Field name="connectorKind" label={t('integrations.form.connector')} required>
          <Select
            disabled={editing}
            options={[
              { value: 'FOLDER', label: t('integrations.connector.FOLDER') },
              { value: 'EMAIL', label: t('integrations.connector.EMAIL') },
              { value: 'MANUAL', label: t('integrations.connector.MANUAL') },
            ]}
          />
        </Field>
        {connector === 'FOLDER' && (
          <Field name="folder" label={t('integrations.form.folder')} required help={t('integrations.form.folderHelp')}>
            <TextInput maxLength={60} placeholder="axiscare" autoComplete="off" />
          </Field>
        )}
      </div>
      {connector !== 'MANUAL' && (
        <div className="r2">
          <Field name="scheduleKind" label={t('integrations.form.schedule')}>
            <Select
              options={[
                { value: 'MANUAL', label: t('integrations.schedule.manual') },
                { value: 'INTERVAL', label: t('integrations.form.scheduleInterval') },
              ]}
            />
          </Field>
          {values.scheduleKind === 'INTERVAL' && (
            <Field name="intervalMinutes" label={t('integrations.form.every')} required help={t('integrations.form.everyHelp')}>
              <TextInput inputMode="numeric" maxLength={5} />
            </Field>
          )}
        </div>
      )}

      {connector === 'EMAIL' && <EmailSection form={form} hasSavedPassword={integration?.hasMailPassword === true} />}

      <fieldset className="int-scopes">
        <legend>{t('integrations.form.scopes')}</legend>
        <p className="help">{t('integrations.form.scopesHelp')}</p>
        {options.isPending && <p className="help">{t('integrations.loading')}</p>}
        {scopeList.map((s) => {
          const on = (values.scopes ?? []).includes(s.code ?? '')
          return (
            <div key={s.code} className="int-opt">
              <label className="sw">
                <input type="checkbox" role="switch" aria-label={s.label ?? s.code} checked={on} disabled={!s.available && !on} onChange={(e) => toggle('scopes', s.code ?? '', e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{s.label}</span>
              </label>
              {!s.available && <span className="help"> {s.unavailableReason}</span>}
            </div>
          )
        })}
        {scopesError && (
          <p className="ferr" role="alert">
            {scopesError}
          </p>
        )}
      </fieldset>

      <fieldset className="int-notify">
        <legend>{t('integrations.form.notify')}</legend>
        <p className="help">{t('integrations.form.notifyHelp')}</p>
        <div className="int-users">
          {users.map((u) => (
            <label key={u.id} className="sw">
              <input type="checkbox" role="switch" aria-label={u.name ?? ''} checked={(values.notifyUserIds ?? []).includes(u.id ?? 0)} onChange={(e) => toggle('notifyUserIds', u.id ?? 0, e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{u.name}</span>
            </label>
          ))}
        </div>
        <Field name="includeMe" label={t('integrations.form.includeMe')}>
          <Toggle text={t('integrations.form.includeMe')} />
        </Field>
      </fieldset>

      <Field name="retentionDays" label={t('integrations.form.retention')} required help={t('integrations.form.retentionHelp')}>
        <TextInput inputMode="numeric" maxLength={3} />
      </Field>
    </FormModal>
  )
}
