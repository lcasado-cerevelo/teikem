// Datos que lee un lector (2026-10-24), en lenguaje llano: cada dato es una tarjeta que dice QUÉ se lee y A DÓNDE va; las expresiones las arma el asistente de lectura (se ven plegadas por si hay que ajustarlas).
// Para los avisos (llegada y salida) y el cierre de órdenes, el destino se escoge por su nombre («Producto», «Cantidad») y no por un número de columna.
import { useState } from 'react'
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, IconPlus, IconTrash, Select, TextInput, Toggle } from '../../kernel/ui'
import { ReadingAssistant, type AssistantMode, type AssistantResult } from './ReadingAssistant'
import { BODY_KINDS, TRANSFORMS, emptyBodyField, type BodyFieldValues } from './emailRules'
import { datumsOf, parseColumnsText, type Datum } from './assistantRules'
import type { IntegrationValues } from './integrationRules'

export function ReaderFieldsEditor({ form, base, target, error }: { form: UseFormReturn<IntegrationValues>; base: string; target: string; error?: string }) {
  const t = useT()
  const path = base as 'email.bodyFields'
  const fields = useFieldArray({ control: form.control, name: path })
  const list = useWatch({ control: form.control, name: path }) as BodyFieldValues[] | undefined
  const datums = datumsOf(target, t)
  const [assist, setAssist] = useState<{ index: number; mode: AssistantMode } | null>(null)

  const set = (i: number, key: string, value: unknown) => form.setValue(`${base}.${i}.${key}` as 'email.bodyFields.0.label', value as never, { shouldDirty: true, shouldValidate: true })
  const apply = (r: AssistantResult) => {
    if (!assist) return
    const i = assist.index
    if (r.kind === 'BLOCK') {
      set(i, 'kind', 'BLOCK')
      set(i, 'lineRegex', r.lineRegex)
      set(i, 'columns', r.columns)
      set(i, 'position', String(r.position))
      set(i, 'start', '')
      set(i, 'end', '')
    } else if (r.kind === 'REGEX') {
      set(i, 'kind', 'REGEX')
      set(i, 'pattern', r.pattern)
    }
    setAssist(null)
  }
  const summary = (f: BodyFieldValues | undefined): string => {
    if (!f) return ''
    if (f.kind === 'BLOCK') {
      const cols = parseColumnsText(f.columns ?? '')
      return cols.length > 0 ? t('integrations.fields.summaryTable', { n: cols.length }) : t('integrations.fields.summaryTableEmpty')
    }
    if (f.kind === 'REGEX') return f.pattern?.trim() ? t('integrations.fields.summaryRegex') : t('integrations.fields.summaryNone')
    return f.label?.trim() ? t('integrations.fields.summaryLabel', { label: f.label.trim() }) : t('integrations.fields.summaryNone')
  }
  const nameOf = (position: number) => datums?.find((d) => d.position === position)?.label ?? t('integrations.assistant.columnN', { n: position })

  return (
    <>
      {fields.fields.map((f, i) => {
        const item = list?.[i]
        const kind = item?.kind ?? 'LABEL'
        const hasLineRegex = !!item?.lineRegex?.trim()
        const cols = kind === 'BLOCK' ? parseColumnsText(item?.columns ?? '') : []
        return (
          <article className="int-datum" key={f.id} aria-label={t('integrations.fields.cardLabel', { n: i + 1 })}>
            <header className="int-datum-h">
              <strong>{t('integrations.fields.cardTitle', { n: i + 1 })}</strong>
              <span className="int-datum-sum">{summary(item)}</span>
              <button type="button" className="btn sm int-rcard-remove" onClick={() => fields.remove(i)}>
                <IconTrash /> {t('integrations.fields.remove', { n: i + 1 })}
              </button>
            </header>

            <Field name={`${base}.${i}.kind`} label={t('integrations.fields.how')} help={t('integrations.fields.howHelp')}>
              <Select options={BODY_KINDS.map((k) => ({ value: k, label: t(`integrations.fields.kindOption.${k}`) }))} />
            </Field>

            {kind === 'LABEL' && (
              <div className="r2">
                <Field name={`${base}.${i}.label`} label={t('integrations.fields.labelText')} required help={t('integrations.fields.labelHelp')}>
                  <TextInput placeholder="Reference #" />
                </Field>
                <ToColumn base={base} index={i} datums={datums} />
              </div>
            )}

            {kind === 'REGEX' && (
              <>
                <div className="int-datum-assist">
                  <button type="button" className="btn sm flow" onClick={() => setAssist({ index: i, mode: 'single' })}>
                    {t('integrations.fields.assistSingle')}
                  </button>
                  <span className="help">{t('integrations.fields.assistSingleHelp')}</span>
                </div>
                <ToColumn base={base} index={i} datums={datums} />
                <details className="int-adv">
                  <summary>{t('integrations.fields.seeExpression')}</summary>
                  <Field name={`${base}.${i}.pattern`} label={t('integrations.fields.patternText')} required help={t('integrations.email.patternHelp')}>
                    <TextInput placeholder="Pueblo:\s*(\w+)" />
                  </Field>
                </details>
              </>
            )}

            {kind === 'BLOCK' && (
              <>
                <div className="int-datum-assist">
                  <button type="button" className="btn sm flow" onClick={() => setAssist({ index: i, mode: 'table' })}>
                    {t(cols.length > 0 ? 'integrations.fields.assistTableAgain' : 'integrations.fields.assistTable')}
                  </button>
                  <span className="help">{t('integrations.fields.assistTableHelp')}</span>
                </div>
                {cols.length > 0 ? (
                  <div className="int-datum-cols">
                    <p className="help">{t('integrations.fields.colsExplain')}</p>
                    <ul>
                      {cols.map((c) => (
                        <li key={c.position}>{c.divideBy > 0 ? t('integrations.assistant.explainDivide', { name: nameOf(c.position), a: c.cell, b: c.divideBy }) : t('integrations.assistant.explainCell', { name: nameOf(c.position), a: c.cell })}</li>
                      ))}
                    </ul>
                  </div>
                ) : (
                  <p className="help">{t('integrations.fields.colsEmpty')}</p>
                )}
                <details className="int-adv">
                  <summary>{t('integrations.fields.seeExpression')}</summary>
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
                  <Field name={`${base}.${i}.columns`} label={t('integrations.fields.columnsText')} required help={t('integrations.fields.columnsHelp')}>
                    <TextInput placeholder="2=2, 3=4/3, 4=4" />
                  </Field>
                  <div className="r2">
                    <Field name={`${base}.${i}.repeat`} label={t('integrations.email.repeat', { n: i + 1 })} help={t('integrations.email.repeatHelp')}>
                      <Toggle text={t('integrations.email.repeat', { n: i + 1 })} />
                    </Field>
                    <Field name={`${base}.${i}.separator`} label={t('integrations.email.separator', { n: i + 1 })} help={t('integrations.email.separatorHelp')}>
                      <TextInput />
                    </Field>
                  </div>
                </details>
              </>
            )}

            <details className="int-adv">
              <summary>{t('integrations.fields.more')}</summary>
              <div className="r2">
                <Field name={`${base}.${i}.required`} label={t('integrations.email.required', { n: i + 1 })} help={t('integrations.fields.requiredHelp')}>
                  <Toggle text={t('integrations.email.required', { n: i + 1 })} />
                </Field>
                <Field name={`${base}.${i}.transform`} label={t('integrations.email.transform', { n: i + 1 })}>
                  <Select options={TRANSFORMS.map((x) => ({ value: x, label: t(`integrations.email.transformOption.${x}`) }))} />
                </Field>
              </div>
              <div className="r2">
                <Field name={`${base}.${i}.fromHeader`} label={t('integrations.email.fromHeader', { n: i + 1 })} help={t('integrations.email.fromHeaderHelp')}>
                  <Toggle text={t('integrations.email.fromHeader', { n: i + 1 })} />
                </Field>
                <Field name={`${base}.${i}.appendWith`} label={t('integrations.email.appendWith', { n: i + 1 })} help={t('integrations.email.appendWithHelp')}>
                  <TextInput placeholder=" | " />
                </Field>
              </div>
            </details>
          </article>
        )
      })}
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
      <button type="button" className="btn sm" onClick={() => fields.append(emptyBodyField())}>
        <IconPlus /> {t('integrations.fields.add')}
      </button>
      {assist && <ReadingAssistant open mode={assist.mode} datums={datums} onClose={() => setAssist(null)} onApply={apply} />}
    </>
  )
}

/** A dónde va un dato suelto: por nombre cuando el destino tiene datos conocidos; si no, el número de columna de la plantilla. */
function ToColumn({ base, index, datums }: { base: string; index: number; datums: Datum[] | null }) {
  const t = useT()
  return datums ? (
    <Field name={`${base}.${index}.position`} label={t('integrations.fields.goesTo')} required>
      <Select options={datums.map((d) => ({ value: String(d.position), label: d.label }))} />
    </Field>
  ) : (
    <Field name={`${base}.${index}.position`} label={t('integrations.fields.goesToColumn')} required help={t('integrations.email.columnHelp')}>
      <TextInput inputMode="numeric" maxLength={3} />
    </Field>
  )
}
