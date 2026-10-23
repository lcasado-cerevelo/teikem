// Editor de los «campos a leer» de un documento (cuerpo del correo o PDF): por etiqueta, expresión regular o bloque de renglones, y a qué columna de la plantilla va cada uno. Lo usan la sección
// de correo (lista de siempre) y cada lector (2026-10-22): el mismo editor, con la ruta de la lista como parámetro.
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, IconPlus, IconTrash, Select, TextInput, Toggle } from '../../kernel/ui'
import { BODY_KINDS, TRANSFORMS, emptyBodyField, type BodyFieldValues } from './emailRules'
import type { IntegrationValues } from './integrationRules'

export function BodyFieldsEditor({ form, base, error }: { form: UseFormReturn<IntegrationValues>; base: string; error?: string }) {
  const t = useT()
  const path = base as 'email.bodyFields'
  const fields = useFieldArray({ control: form.control, name: path })
  const list = useWatch({ control: form.control, name: path }) as BodyFieldValues[] | undefined
  return (
    <>
      {fields.fields.map((f, i) => {
        const kind = list?.[i]?.kind ?? 'LABEL'
        const hasLineRegex = !!list?.[i]?.lineRegex?.trim()
        return (
          <div className="int-bodyfield" key={f.id}>
            <div className="r2">
              <Field name={`${base}.${i}.position`} label={t('integrations.email.column', { n: i + 1 })} required help={t('integrations.email.columnHelp')}>
                <TextInput inputMode="numeric" maxLength={3} />
              </Field>
              <Field name={`${base}.${i}.kind`} label={t('integrations.email.kind', { n: i + 1 })}>
                <Select options={BODY_KINDS.map((k) => ({ value: k, label: t(`integrations.email.kindOption.${k}`) }))} />
              </Field>
            </div>
            {kind === 'LABEL' && (
              <Field name={`${base}.${i}.label`} label={t('integrations.email.label', { n: i + 1 })} required help={t('integrations.email.labelHelp')}>
                <TextInput placeholder="Reference #" />
              </Field>
            )}
            {kind === 'REGEX' && (
              <Field name={`${base}.${i}.pattern`} label={t('integrations.email.pattern', { n: i + 1 })} required help={t('integrations.email.patternHelp')}>
                <TextInput placeholder="Pueblo:\s*(\w+)" />
              </Field>
            )}
            {kind === 'BLOCK' && (
              <>
                <div className="r2">
                  <Field name={`${base}.${i}.start`} label={t('integrations.email.start', { n: i + 1 })} required={!hasLineRegex}>
                    <TextInput placeholder="Order Summary" />
                  </Field>
                  <Field name={`${base}.${i}.end`} label={t('integrations.email.end', { n: i + 1 })} required={!hasLineRegex}>
                    <TextInput placeholder="Total" />
                  </Field>
                </div>
                <Field name={`${base}.${i}.lineRegex`} label={t('integrations.email.lineRegex', { n: i + 1 })} help={t('integrations.email.lineRegexHelp')}>
                  <TextInput placeholder="^(T-\w+)\t.*\t(\d+)\t(\d+)$" />
                </Field>
                <Field name={`${base}.${i}.repeat`} label={t('integrations.email.repeat', { n: i + 1 })} help={t('integrations.email.repeatHelp')}>
                  <Toggle text={t('integrations.email.repeat', { n: i + 1 })} />
                </Field>
                <div className="r2">
                  <Field name={`${base}.${i}.separator`} label={t('integrations.email.separator', { n: i + 1 })} help={t('integrations.email.separatorHelp')}>
                    <TextInput />
                  </Field>
                  <Field name={`${base}.${i}.columns`} label={t('integrations.email.blockColumns', { n: i + 1 })} required help={t('integrations.email.blockColumnsHelp')}>
                    <TextInput placeholder="6=1, 7=2, 8=3" />
                  </Field>
                </div>
              </>
            )}
            <div className="r2">
              <Field name={`${base}.${i}.transform`} label={t('integrations.email.transform', { n: i + 1 })}>
                <Select options={TRANSFORMS.map((x) => ({ value: x, label: t(`integrations.email.transformOption.${x}`) }))} />
              </Field>
              <Field name={`${base}.${i}.required`} label={t('integrations.email.required', { n: i + 1 })}>
                <Toggle text={t('integrations.email.required', { n: i + 1 })} />
              </Field>
            </div>
            <button type="button" className="btn sm" aria-label={t('integrations.email.removeField', { n: i + 1 })} onClick={() => fields.remove(i)}>
              <IconTrash /> {t('integrations.email.removeField', { n: i + 1 })}
            </button>
          </div>
        )
      })}
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
      <button type="button" className="btn sm" onClick={() => fields.append(emptyBodyField())}>
        <IconPlus /> {t('integrations.email.addField')}
      </button>
    </>
  )
}
