// Sección «Qué se manda» del formulario de una integración de SALIDA (Lote 137b, P11): los estatus que disparan la salida, cuándo sale (al instante o por horario), el formato (CSV, JSON o XML), el nombre del archivo
// (SFTP/FTP), las columnas (nombre y campo de la orden) y, en el conector HTTP, la API saliente (URL, método, autenticación y encabezados). «Vista previa» (al editar) arma un ejemplo con órdenes recientes del cliente.
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Select, TextInput } from '../../kernel/ui'
import type { IntegrationValues } from './integrationRules'
import type { IntegrationOptionsDto } from './integrationsApi'
import { HTTP_AUTH_KINDS, HTTP_METHODS, MAX_COLUMNS, MAX_HEADERS, OUTPUT_FORMATS, OUTPUT_TRIGGERS } from './outputRules'

export function OutputSection({ form, connector, options, hasSavedSecret, onPreview, entity = false }: { form: UseFormReturn<IntegrationValues>; connector: string; options: IntegrationOptionsDto | undefined; hasSavedSecret: boolean; onPreview?: () => void ; entity?: boolean}) {
  const t = useT()
  const output = useWatch({ control: form.control, name: 'output' })
  const columns = output?.columns ?? []
  const statuses = output?.statuses ?? []
  const headers = output?.http?.headers ?? []
  const fields = options?.outputFields ?? []
  const errors = form.formState.errors.output as { statuses?: { message?: string }; columns?: { message?: string } } | undefined
  const set = <K extends 'columns' | 'statuses'>(name: K, value: IntegrationValues['output'][K]) => form.setValue(`output.${name}`, value as never, { shouldDirty: true, shouldValidate: form.formState.isSubmitted })
  const setHeaders = (next: { name: string; value: string }[]) => form.setValue('output.http.headers', next, { shouldDirty: true })
  const auth = output?.http?.authKind ?? 'NONE'

  return (
    <>
      <fieldset className="int-scopes">
        <legend>{t('integrations.out.section')}</legend>
        <p className="help">{t('integrations.out.sectionHelp')}</p>
        {!entity && (
          <>
            <p className="help">{t('integrations.out.statuses')}</p>
            <div className="int-users">
              {(options?.orderStatuses ?? []).map((s) => (
                <label key={s} className="sw">
                  <input type="checkbox" role="switch" aria-label={t(`integrations.out.status.${s}`)} checked={statuses.includes(s)} onChange={(e) => set('statuses', e.target.checked ? [...statuses.filter((x) => x !== s), s] : statuses.filter((x) => x !== s))} />
                  <span className="tk" aria-hidden="true" />
                  <span>{t(`integrations.out.status.${s}`)}</span>
                </label>
              ))}
            </div>
            {typeof errors?.statuses?.message === 'string' && (
              <p className="ferr" role="alert">
                {errors.statuses.message}
              </p>
            )}
          </>
        )}
        <div className="r2">
          <Field name="output.trigger" label={t('integrations.out.trigger')} help={t(`integrations.out.triggerHelp.${output?.trigger ?? 'EVENT'}`)}>
            <Select options={OUTPUT_TRIGGERS.map((k) => ({ value: k, label: t(`integrations.out.triggers.${k}`) }))} />
          </Field>
          {output?.trigger === 'SCHEDULE' && (
            <Field name="intervalMinutes" label={t('integrations.form.every')} required help={t('integrations.out.everyHelp')}>
              <TextInput inputMode="numeric" maxLength={5} />
            </Field>
          )}
        </div>
        <div className="r2">
          <Field name="output.format" label={t('integrations.out.format')}>
            <Select options={OUTPUT_FORMATS.map((k) => ({ value: k, label: k }))} />
          </Field>
          {output?.format === 'CSV' && (
            <Field name="output.delimiter" label={t('integrations.out.delimiter')}>
              <Select options={[{ value: ',', label: t('integrations.out.delimiters.comma') }, { value: ';', label: t('integrations.out.delimiters.semicolon') }, { value: '\t', label: t('integrations.out.delimiters.tab') }]} />
            </Field>
          )}
        </div>
        {connector !== 'HTTP' && (
          <Field name="output.fileName" label={t('integrations.out.fileName')} help={t('integrations.out.fileNameHelp')}>
            <TextInput maxLength={120} placeholder={entity ? 'productos-{date}-{time}.{ext}' : 'ordenes-{date}-{time}.{ext}'} autoComplete="off" />
          </Field>
        )}
        {entity ? (
          <p className="help">{t('integrations.out.entityColumnsHelp')}</p>
        ) : (
          <div className="int-cols">
            <div className="int-cols-head">
              <b>{t('integrations.out.columns')}</b>
              <span className="help">{t(output?.format === 'CSV' ? 'integrations.out.columnsHelpCsv' : 'integrations.out.columnsHelp')}</span>
            </div>
            {columns.map((_, i) => (
              <div className="int-col" key={i}>
                <span className="help int-col-n">{i + 1}</span>
                <Field name={`output.columns.${i}.header`} label={t('integrations.out.header')}>
                  <TextInput maxLength={60} autoComplete="off" />
                </Field>
                <Field name={`output.columns.${i}.field`} label={t('integrations.out.field')}>
                  <Select options={fields.map((f) => ({ value: f.code ?? '', label: f.label ?? f.code ?? '' }))} placeholder={t('integrations.form.pick')} />
                </Field>
                <button type="button" className="btn sm" aria-label={t('integrations.format.removeColumn', { n: i + 1 })} onClick={() => set('columns', columns.filter((__, j) => j !== i))}>
                  {t('integrations.format.remove')}
                </button>
              </div>
            ))}
            <button type="button" className="btn sm" disabled={columns.length >= MAX_COLUMNS} onClick={() => set('columns', [...columns, { header: '', field: '' }])}>
              {t('integrations.format.addColumn')}
            </button>
            {typeof errors?.columns?.message === 'string' && (
              <p className="ferr" role="alert">
                {errors.columns.message}
              </p>
            )}
          </div>
        )}
        <div className="int-previewbar">
          <button type="button" className="btn sm" disabled={!onPreview} onClick={onPreview}>
            {t('integrations.out.preview')}
          </button>
          <span className="help">{t(onPreview ? 'integrations.out.previewHelp' : 'integrations.out.previewAfterSave')}</span>
        </div>
      </fieldset>

      {connector === 'HTTP' && (
        <fieldset className="int-scopes">
          <legend>{t('integrations.out.http.section')}</legend>
          <p className="help">{t('integrations.out.http.sectionHelp')}</p>
          <Field name="output.http.url" label={t('integrations.out.http.url')} required help={t('integrations.out.http.urlHelp')}>
            <TextInput maxLength={500} placeholder="https://sistema.cliente.com/api/entregas" autoComplete="off" />
          </Field>
          <div className="r2">
            <Field name="output.http.method" label={t('integrations.out.http.method')}>
              <Select options={HTTP_METHODS.map((m) => ({ value: m, label: m }))} />
            </Field>
            <Field name="output.http.authKind" label={t('integrations.out.http.auth')}>
              <Select options={HTTP_AUTH_KINDS.map((k) => ({ value: k, label: t(`integrations.out.http.authKinds.${k}`) }))} />
            </Field>
          </div>
          {(auth === 'BASIC' || auth === 'HEADER') && (
            <Field name="output.http.authName" label={t(auth === 'BASIC' ? 'integrations.out.http.user' : 'integrations.out.http.headerName')} required>
              <TextInput maxLength={100} placeholder={auth === 'HEADER' ? 'X-Api-Key' : ''} autoComplete="off" />
            </Field>
          )}
          {auth !== 'NONE' && (
            <Field name="output.http.secret" label={t(auth === 'BEARER' ? 'integrations.out.http.token' : auth === 'BASIC' ? 'integrations.out.http.password' : 'integrations.out.http.headerValue')} required={!hasSavedSecret} help={hasSavedSecret ? t('integrations.out.http.secretKept') : t('integrations.out.http.secretOnce')}>
              <TextInput type="password" autoComplete="new-password" />
            </Field>
          )}
          <div className="int-cols">
            <div className="int-cols-head">
              <b>{t('integrations.out.http.headers')}</b>
              <span className="help">{t('integrations.out.http.headersHelp')}</span>
            </div>
            {headers.map((_, i) => (
              <div className="int-col int-col-h" key={i}>
                <Field name={`output.http.headers.${i}.name`} label={t('integrations.out.http.headerNameLabel')}>
                  <TextInput maxLength={100} autoComplete="off" />
                </Field>
                <Field name={`output.http.headers.${i}.value`} label={t('integrations.out.http.headerValueLabel')}>
                  <TextInput maxLength={500} autoComplete="off" />
                </Field>
                <button type="button" className="btn sm" aria-label={t('integrations.out.http.removeHeader', { n: i + 1 })} onClick={() => setHeaders(headers.filter((__, j) => j !== i))}>
                  {t('integrations.format.remove')}
                </button>
              </div>
            ))}
            <button type="button" className="btn sm" disabled={headers.length >= MAX_HEADERS} onClick={() => setHeaders([...headers, { name: '', value: '' }])}>
              {t('integrations.out.http.addHeader')}
            </button>
          </div>
        </fieldset>
      )}
    </>
  )
}
