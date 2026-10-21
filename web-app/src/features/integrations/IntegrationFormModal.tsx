// Alta y edición de una integración (Lote 132, P11). Alta: nombre, cliente, plantilla de importación, conector (carpeta de entrada o subida manual), horario, alcances (solo los que quien crea puede dar),
// a quién avisar (con «incluirme») y retención de la bandeja; el servidor crea la cuenta de servicio con su rol. Edición: lo mismo salvo el cliente y el conector.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Field, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import { useClientOptions } from '../consignees/api'
import { ImportTemplateEditorModal } from '../consignees/ImportTemplateEditorModal'
import { useEntityImportFields, useEntityImportTemplates } from '../imports/entityImportApi'
import { useOrderImportFields, useOrderImportTemplates } from '../orders/importApi'
import { EmailSection } from './EmailSection'
import { FormatSection } from './FormatSection'
import { OutputPreviewModal } from './OutputPreviewModal'
import { OutputSection } from './OutputSection'
import { RemoteSection } from './RemoteSection'
import { CATALOG_EXPORT_SCOPE, EXPORT_SCOPE } from './outputRules'
import { buildCreateRequest, buildPatchRequest, integrationSchema, integrationValuesOf, type IntegrationValues } from './integrationRules'
import { useIntegrationMutations, useIntegrationOptions, type IntegrationDto } from './integrationsApi'

export function IntegrationFormModal({ integration, onClose, onSaved }: { integration: IntegrationDto | null; onClose: () => void; onSaved?: (i: IntegrationDto) => void }) {
  const t = useT()
  const editing = integration !== null
  const { create, update } = useIntegrationMutations()
  const options = useIntegrationOptions(true)
  const clients = useClientOptions(true)
  const needsClientRef = useRef<string[]>([])
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
        {
          hasSavedSecret: integration?.hasRemoteSecret === true,
          messages: {
            host: t('integrations.remote.errors.host'),
            port: t('integrations.remote.errors.port'),
            username: t('integrations.remote.errors.username'),
            secretPassword: t('integrations.remote.errors.secretPassword'),
            secretKey: t('integrations.remote.errors.secretKey'),
            dir: t('integrations.remote.errors.dir'),
            dirInvalid: t('integrations.remote.errors.dirInvalid'),
            fingerprint: t('integrations.remote.errors.fingerprint'),
            keyOnlySftp: t('integrations.remote.errors.keyOnlySftp'),
            fileName: t('integrations.remote.errors.fileName'),
          },
        },
        { needsClient: (codes) => needsClientRef.current.some((c) => codes.includes(c)), message: t('integrations.api.errors.clientForScopes') },
        {
          sheet: t('integrations.format.errors.sheet'),
          recordsRequired: t('integrations.format.errors.recordsRequired'),
          recordsRoot: t('integrations.format.errors.recordsRoot'),
          linesRoot: t('integrations.format.errors.linesRoot'),
          columnsRequired: t('integrations.format.errors.columnsRequired'),
          columnsMax: t('integrations.format.errors.columnsMax'),
          headerRequired: t('integrations.format.errors.headerRequired'),
          headerMax: t('integrations.format.errors.headerMax'),
          headerRepeated: (h) => t('integrations.format.errors.headerRepeated', { h }),
          pathInvalid: (p) => t('integrations.format.errors.pathInvalid', { p }),
          orderRoot: (p) => t('integrations.format.errors.orderRoot', { p }),
        },
        {
          messages: {
            statuses: t('integrations.out.errors.statuses'),
            columnsRequired: t('integrations.out.errors.columnsRequired'),
            columnsMax: t('integrations.out.errors.columnsMax'),
            headerRequired: t('integrations.out.errors.headerRequired'),
            headerMax: t('integrations.out.errors.headerMax'),
            headerRepeated: (h) => t('integrations.out.errors.headerRepeated', { h }),
            headerName: (h) => t('integrations.out.errors.headerName', { h }),
            fieldRequired: t('integrations.out.errors.fieldRequired'),
            url: t('integrations.out.errors.url'),
            authName: t('integrations.out.errors.authName'),
            secret: t('integrations.out.errors.secret'),
            headersMax: t('integrations.out.errors.headersMax'),
            headerPair: t('integrations.out.errors.headerPair'),
          },
          hasSavedSecret: integration?.hasHttpSecret === true,
        },
      ),
    [t, editing, integration?.hasMailPassword, integration?.hasRemoteSecret, integration?.hasHttpSecret],
  )
  const form = useForm<IntegrationValues>({ resolver: zodResolver(schema), defaultValues: integrationValuesOf(integration) })
  const values = useWatch({ control: form.control })
  const clientId = values.clientPublicId ?? ''
  const templates = useOrderImportTemplates(clientId, false, clientId !== '')
  const connector = values.connectorKind ?? 'FOLDER'
  const outbound = (values.direction ?? 'INBOUND') === 'OUTBOUND'
  // 2026-10-21: salida de un catálogo (productos): sin cliente ni estatus; las columnas salen de la plantilla de la entidad
  const entityKind = outbound ? (values.entityKind ?? '') : ''
  const isEntity = entityKind !== ''
  const entityTemplates = useEntityImportTemplates('PRODUCT', false, isEntity)
  const canEntityTemplates = useCan('inventory.manage')
  const entityFields = useEntityImportFields('PRODUCT', isEntity && canEntityTemplates)
  const [creatingEntityTemplate, setCreatingEntityTemplate] = useState(false)
  const [previewing, setPreviewing] = useState(false)
  // «Nueva plantilla» sin salir de la integración (dictado de Luis, 2026-10-19): el mismo editor del asistente de Importar; al guardar queda elegida
  const canTemplates = useCan('orders.edit')
  const orderFields = useOrderImportFields(canTemplates)
  const [creatingTemplate, setCreatingTemplate] = useState(false)
  const [justCreated, setJustCreated] = useState<{ value: string; label: string } | null>(null)
  const fieldLabel = (field: string) => {
    const key = `orders.import.fields.${field}`
    const text = t(key)
    return text === key ? field : text
  }
  // al cambiar la dirección (solo al crear) se ofrece lo que corresponde: conector y alcance
  const lastDirection = useRef(values.direction)
  useEffect(() => {
    if (editing || lastDirection.current === values.direction) return
    lastDirection.current = values.direction
    form.setValue('connectorKind', values.direction === 'OUTBOUND' ? 'HTTP' : 'FOLDER')
    form.setValue('scopes', values.direction === 'OUTBOUND' ? [EXPORT_SCOPE] : [], { shouldDirty: true })
    form.setValue('entityKind', '')
    form.setValue('templatePublicId', '')
  }, [editing, form, values.direction])
  // al escoger qué se manda (solo al crear) el alcance es el de esa salida
  const lastEntity = useRef(values.entityKind ?? '')
  useEffect(() => {
    if (editing || lastEntity.current === (values.entityKind ?? '')) return
    lastEntity.current = values.entityKind ?? ''
    form.setValue('scopes', [values.entityKind ? CATALOG_EXPORT_SCOPE : EXPORT_SCOPE], { shouldDirty: true })
    form.setValue('templatePublicId', '')
  }, [editing, form, values.entityKind])

  const clientOptions = (clients.data ?? []).map((c) => ({ value: c.publicId ?? '', label: [c.code, c.name].filter(Boolean).join(' · ') }))
  const templateOptions = (isEntity ? (entityTemplates.data ?? []) : (templates.data ?? [])).map((x) => ({ value: x.publicId ?? '', label: x.name ?? '' }))
  if (justCreated && !templateOptions.some((o) => o.value === justCreated.value)) templateOptions.push(justCreated)
  // al editar, la plantilla actual siempre es una opción (si la lista aún no llegó, el control no se queda en blanco)
  if (integration?.templatePublicId && !templateOptions.some((o) => o.value === integration.templatePublicId)) templateOptions.unshift({ value: integration.templatePublicId, label: integration.templateName ?? '' })
  // la API solo ofrece sus alcances; las demás integraciones, solo los de archivos
  const scopeList = (options.data?.scopes ?? []).filter((s) => (outbound ? s.code === (isEntity ? CATALOG_EXPORT_SCOPE : EXPORT_SCOPE) : connector === 'API' ? !!s.isApi : !s.isApi && s.code !== EXPORT_SCOPE))
  needsClientRef.current = (options.data?.scopes ?? []).filter((s) => s.needsClient).map((s) => s.code ?? '')
  const clientNeeded = connector !== 'API' || (values.scopes ?? []).some((c) => needsClientRef.current.includes(c))
  const users = options.data?.users ?? []
  const toggle = (name: 'scopes' | 'notifyUserIds', value: string | number, on: boolean) => {
    const current = (form.getValues(name) as (string | number)[]) ?? []
    const next = on ? [...current.filter((x) => x !== value), value] : current.filter((x) => x !== value)
    form.setValue(name, next as never, { shouldDirty: true, shouldValidate: form.formState.isSubmitted })
  }
  const scopesError = form.formState.errors.scopes?.message

  return (
    <>
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
      {!editing && !isEntity && (
        <Field name="clientPublicId" label={t('integrations.form.client')} required={clientNeeded} help={t(connector === 'API' ? 'integrations.api.clientHelp' : 'integrations.form.clientHelp')}>
          <Select options={clientOptions} placeholder={t('integrations.form.pick')} />
        </Field>
      )}
      {!editing && (
        <Field name="direction" label={t('integrations.form.direction')} help={t(`integrations.form.directionHelp.${values.direction ?? 'INBOUND'}`)}>
          <Select options={[{ value: 'INBOUND', label: t('integrations.direction.INBOUND') }, { value: 'OUTBOUND', label: t('integrations.direction.OUTBOUND') }]} />
        </Field>
      )}
      {!editing && outbound && (
        <Field name="entityKind" label={t('integrations.form.entityKind')} help={t(isEntity ? 'integrations.form.entityKindHelp.PRODUCT' : 'integrations.form.entityKindHelp.ORDERS')}>
          <Select options={[{ value: '', label: t('integrations.form.entityKinds.ORDERS') }, { value: 'PRODUCT', label: t('integrations.form.entityKinds.PRODUCT') }]} />
        </Field>
      )}
      {isEntity && (
        <>
          <Field name="templatePublicId" label={t('integrations.form.entityTemplate')} required help={t('integrations.form.entityTemplateHelp')}>
            <Select options={templateOptions} placeholder={t('integrations.form.pick')} />
          </Field>
          {canTemplates && (
            <p>
              <button type="button" className="btn sm" onClick={() => setCreatingEntityTemplate(true)} disabled={(entityFields.data ?? []).length === 0}>
                {t('integrations.form.newTemplate')}
              </button>
            </p>
          )}
        </>
      )}
      {connector !== 'API' && !outbound && (
        <Field name="templatePublicId" label={t('integrations.form.template')} required help={t('integrations.form.templateHelp')}>
          <Select options={templateOptions} placeholder={clientId || editing ? t('integrations.form.pick') : t('integrations.form.pickClientFirst')} />
        </Field>
      )}
      {connector !== 'API' && !outbound && canTemplates && (
        <p>
          <button type="button" className="btn sm" onClick={() => setCreatingTemplate(true)} disabled={(orderFields.data ?? []).length === 0}>
            {t('integrations.form.newTemplate')}
          </button>
        </p>
      )}
      <div className="r2">
        <Field name="connectorKind" label={t('integrations.form.connector')} required>
          <Select
            disabled={editing}
            options={(outbound
              ? ['HTTP', 'SFTP', 'FTP']
              : ['FOLDER', 'EMAIL', 'SFTP', 'FTP', 'MANUAL', 'API']
            ).map((k) => ({ value: k, label: t(`integrations.connector.${k}`) }))}
          />
        </Field>
        {connector === 'FOLDER' && (
          <Field name="folder" label={t('integrations.form.folder')} required help={t('integrations.form.folderHelp')}>
            <TextInput maxLength={60} placeholder="axiscare" autoComplete="off" />
          </Field>
        )}
      </div>
      {connector !== 'MANUAL' && connector !== 'API' && !outbound && (
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
      {connector !== 'API' && !outbound && <FormatSection form={form} />}
      {(connector === 'SFTP' || connector === 'FTP') && <RemoteSection form={form} kind={connector} hasSavedSecret={integration?.hasRemoteSecret === true} outbound={outbound} />}
      {outbound && <OutputSection form={form} connector={connector} options={options.data} hasSavedSecret={integration?.hasHttpSecret === true} onPreview={editing ? () => setPreviewing(true) : undefined} entity={isEntity} />}

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
    {creatingTemplate && (
      <ImportTemplateEditorModal
        kind="ORDER"
        template={null}
        fields={orderFields.data ?? []}
        fieldLabel={fieldLabel}
        onClose={() => setCreatingTemplate(false)}
        onSaved={(saved) => {
          setCreatingTemplate(false)
          setJustCreated({ value: saved.publicId ?? '', label: saved.name ?? '' })
          form.setValue('templatePublicId', saved.publicId ?? '', { shouldDirty: true, shouldValidate: true })
        }}
      />
    )}
    {creatingEntityTemplate && (
      <ImportTemplateEditorModal
        kind="PRODUCT"
        template={null}
        fields={entityFields.data ?? []}
        fieldLabel={(field) => {
          const key = `entityImport.fields.PRODUCT.${field}`
          const text = t(key)
          return text === key ? field : text
        }}
        onClose={() => setCreatingEntityTemplate(false)}
        onSaved={(saved) => {
          setCreatingEntityTemplate(false)
          form.setValue('templatePublicId', saved.publicId ?? '', { shouldDirty: true, shouldValidate: true })
        }}
      />
    )}
    {previewing && integration && <OutputPreviewModal integration={integration} onClose={() => setPreviewing(false)} />}
    </>
  )
}

