// Lectores de una integración de correo (2026-10-22): qué hace la integración con cada documento que llega (el cuerpo, un PDF, un archivo de datos). Cada lector tiene sus condiciones (nombre del adjunto,
// asunto o texto del documento), sus campos a leer, su destino y, si hace falta, su plantilla y su cliente (un mismo buzón puede traer documentos de varios clientes y de varios tipos).
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, IconPlus, IconTrash, Select, TextInput, Toggle } from '../../kernel/ui'
import { BodyFieldsEditor } from './BodyFieldsEditor'
import { CONDITION_SCOPES, MATCH_OPERATORS, READER_SOURCES, READER_TARGETS, emptyCondition, emptyReader, legacyToReaders, scopeApplies, type ReaderValues } from './emailRules'
import type { IntegrationValues } from './integrationRules'

type Option = { value: string; label: string }

export function ReadersSection({ form, clientOptions, templateOptions }: { form: UseFormReturn<IntegrationValues>; clientOptions: Option[]; templateOptions: Option[] }) {
  const t = useT()
  const readers = useFieldArray({ control: form.control, name: 'email.readers' })
  const list = useWatch({ control: form.control, name: 'email.readers' }) as ReaderValues[] | undefined
  const email = useWatch({ control: form.control, name: 'email' })
  const errors = form.formState.errors.email as { readers?: { message?: string; root?: { message?: string } } } | undefined
  const listError = errors?.readers?.message ?? errors?.readers?.root?.message
  const convert = () => {
    const converted = legacyToReaders(form.getValues('email'))
    for (const r of converted) readers.append(r)
  }
  const canConvert = (readers.fields.length === 0) && (email?.readBody || email?.readPdf || (email?.attachmentTypes ?? '').trim() !== '')
  return (
    <fieldset className="int-scopes int-readers">
      <legend>{t('integrations.email.readers.title')}</legend>
      <p className="help">{t('integrations.email.readers.help')}</p>
      {readers.fields.map((f, i) => (
        <ReaderCard key={f.id} form={form} index={i} reader={list?.[i]} clientOptions={clientOptions} templateOptions={templateOptions} onRemove={() => readers.remove(i)} />
      ))}
      {listError && (
        <p className="ferr" role="alert">
          {listError}
        </p>
      )}
      <div className="int-readers-actions">
        <button type="button" className="btn sm" onClick={() => readers.append(emptyReader())}>
          <IconPlus /> {t('integrations.email.readers.add')}
        </button>
        {canConvert && (
          <button type="button" className="btn sm" onClick={convert} title={t('integrations.email.readers.convertHelp')}>
            {t('integrations.email.readers.convert')}
          </button>
        )}
      </div>
    </fieldset>
  )
}

function ReaderCard({ form, index, reader, clientOptions, templateOptions, onRemove }: { form: UseFormReturn<IntegrationValues>; index: number; reader: ReaderValues | undefined; clientOptions: Option[]; templateOptions: Option[]; onRemove: () => void }) {
  const t = useT()
  const base = `email.readers.${index}`
  const conditions = useFieldArray({ control: form.control, name: `${base}.conditions` as 'email.readers.0.conditions' })
  const source = reader?.source ?? 'BODY'
  const opOptions = MATCH_OPERATORS.map((o) => ({ value: o, label: t(`integrations.email.operator.${o}`) }))
  const scopeOptions = CONDITION_SCOPES.filter((s) => scopeApplies(s, source)).map((s) => ({ value: s, label: t(`integrations.email.readers.scope.${s}`) }))
  const fieldsError = (form.formState.errors.email as { readers?: { message?: string }[] } | undefined)?.readers?.[index]
  return (
    <div className="int-reader">
      <div className="r2">
        <Field name={`${base}.name`} label={t('integrations.email.readers.name', { n: index + 1 })} required>
          <TextInput placeholder={t('integrations.email.readers.namePlaceholder')} />
        </Field>
        <Field name={`${base}.source`} label={t('integrations.email.readers.source')}>
          <Select options={READER_SOURCES.map((s) => ({ value: s, label: t(`integrations.email.readers.sourceOption.${s}`) }))} />
        </Field>
      </div>
      <div className="r2">
        <Field name={`${base}.target`} label={t('integrations.email.readers.target')} help={t('integrations.email.readers.targetHelp')}>
          <Select options={READER_TARGETS.map((s) => ({ value: s, label: t(`integrations.email.readers.targetOption.${s}`) }))} />
        </Field>
        <Field name={`${base}.firstOnly`} label={t('integrations.email.readers.firstOnly')} help={t('integrations.email.readers.firstOnlyHelp')}>
          <Toggle text={t('integrations.email.readers.firstOnly')} />
        </Field>
      </div>
      <div className="r2">
        <Field name={`${base}.clientPublicId`} label={t('integrations.email.readers.client')} help={t('integrations.email.readers.clientHelp')}>
          <Select options={clientOptions} placeholder={t('integrations.email.readers.sameAsIntegration')} />
        </Field>
        <Field name={`${base}.templatePublicId`} label={t('integrations.email.readers.template')} help={t('integrations.email.readers.templateHelp')}>
          <Select options={templateOptions} placeholder={t('integrations.email.readers.sameAsIntegration')} />
        </Field>
      </div>

      <p className="help">{t('integrations.email.readers.whenHelp')}</p>
      {conditions.fields.map((c, j) => (
        <div className="r3" key={c.id}>
          <Field name={`${base}.conditions.${j}.scope`} label={t('integrations.email.readers.condScope', { n: j + 1 })}>
            <Select options={scopeOptions} />
          </Field>
          <Field name={`${base}.conditions.${j}.operator`} label={t('integrations.email.readers.condOperator', { n: j + 1 })}>
            <Select options={opOptions} />
          </Field>
          <Field name={`${base}.conditions.${j}.value`} label={t('integrations.email.readers.condValue', { n: j + 1 })} required>
            <TextInput placeholder="packing list" />
          </Field>
          <button type="button" className="btn sm" aria-label={t('integrations.email.readers.removeCondition', { n: j + 1 })} onClick={() => conditions.remove(j)}>
            <IconTrash />
          </button>
        </div>
      ))}
      <button type="button" className="btn sm" onClick={() => conditions.append(emptyCondition(source))}>
        <IconPlus /> {t('integrations.email.readers.addCondition')}
      </button>

      {source !== 'FILE' ? (
        <div className="int-reader-fields">
          <p className="help">{t('integrations.email.bodyHelp')}</p>
          <BodyFieldsEditor form={form} base={`${base}.fields`} error={fieldsError?.message} />
        </div>
      ) : (
        <p className="help">{t('integrations.email.readers.fileHelp')}</p>
      )}
      <button type="button" className="btn sm" onClick={onRemove}>
        <IconTrash /> {t('integrations.email.readers.remove', { n: index + 1 })}
      </button>
    </div>
  )
}
