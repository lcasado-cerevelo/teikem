// Lectores de una integración de correo (2026-10-22): qué hace la integración con cada documento que llega (el cuerpo, un PDF, un archivo de datos). Cada lector es una TARJETA con cuatro pasos:
// 1) qué documento es (nombre, de dónde viene y las condiciones que lo identifican), 2) qué se hace con él (destino, almacén, cliente), 3) cómo se lee (los datos que se sacan) y 4) las revisiones
// (confianza del OCR y chequeos contra los totales del documento). Lo avanzado va plegado: la persona que configura no escribe expresiones, las arma el asistente de lectura.
import { useState } from 'react'
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form'
import { useWarehouses, warehouseLabel } from '../warehouse/api'
import { useT } from '../../kernel/i18n'
import { Chip, Field, IconPlus, IconTrash, Select, TextInput, Toggle } from '../../kernel/ui'
import { IconChevronDown } from '../../kernel/ui/icons'
import { ReaderFieldsEditor } from './ReaderFieldsEditor'
import { ReadingAssistant } from './ReadingAssistant'
import presets from './readerPresets.json'
import { CHECK_KINDS, CHECK_SCOPES, COMPLETION_TARGETS, CONDITION_SCOPES, MATCH_OPERATORS, READER_MODES, READER_SOURCES, READER_TARGETS, emptyCheck, emptyCondition, emptyReader, legacyToReaders, scopeApplies, type ReaderValues } from './emailRules'
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
  // un modelo = un lector ya armado para un tipo de documento conocido; si ya hay uno con ese nombre se numera para no repetirlo
  const addPreset = (key: string) => {
    const preset = presets.find((p) => p.key === key)
    if (!preset) return
    const taken = new Set(readers.fields.map((_, i) => (list?.[i]?.name ?? '').toLowerCase()))
    let name = preset.reader.name
    for (let n = 2; taken.has(name.toLowerCase()); n++) name = `${preset.reader.name} ${n}`
    readers.append({ ...(structuredClone(preset.reader) as ReaderValues), name })
  }
  const anyNotice = (list ?? []).some((r) => r.target === 'ARRIVAL_NOTICE' || r.target === 'EXIT_NOTICE')
  const warehouses = useWarehouses({ includeInactive: false }, { enabled: anyNotice, handleAccessDenied: false })
  const warehouseOptions = (warehouses.data ?? []).map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) }))
  const canConvert = readers.fields.length === 0 && (email?.readBody || email?.readPdf || (email?.attachmentTypes ?? '').trim() !== '')
  return (
    <section className="int-readers" aria-labelledby="int-readers-title">
      <h3 id="int-readers-title" className="int-readers-title">
        {t('integrations.email.readers.title')}
      </h3>
      <p className="int-readers-intro">{t('integrations.email.readers.help')}</p>

      {readers.fields.map((f, i) => (
        <ReaderCard
          key={f.id}
          form={form}
          index={i}
          reader={list?.[i]}
          clientOptions={clientOptions}
          templateOptions={templateOptions}
          warehouseOptions={warehouseOptions}
          defaultOpen={readers.fields.length <= 2 || i === readers.fields.length - 1}
          onRemove={() => readers.remove(i)}
        />
      ))}
      {listError && (
        <p className="ferr" role="alert">
          {listError}
        </p>
      )}

      <div className="int-readers-add">
        <button type="button" className="btn flow" onClick={() => readers.append(emptyReader())}>
          <IconPlus /> {t('integrations.email.readers.add')}
        </button>
        {canConvert && (
          <button type="button" className="btn" onClick={convert} title={t('integrations.email.readers.convertHelp')}>
            {t('integrations.email.readers.convert')}
          </button>
        )}
      </div>
      <div className="int-presets" role="group" aria-label={t('integrations.email.readers.presets')}>
        <p className="help">{t('integrations.email.readers.presetsHelp')}</p>
        <div className="int-presets-list">
          {presets.map((p) => (
            <button key={p.key} type="button" className="btn sm" onClick={() => addPreset(p.key)} data-testid={`reader-preset-${p.key}`}>
              <IconPlus /> {t(`integrations.email.readers.preset.${p.key}`)}
            </button>
          ))}
        </div>
      </div>
    </section>
  )
}

function Step({ n, title, help, children }: { n: number; title: string; help?: string; children: React.ReactNode }) {
  return (
    <div className="int-step">
      <h4 className="int-step-h">
        <span className="int-step-n" aria-hidden="true">
          {n}
        </span>
        {title}
      </h4>
      {help && <p className="help int-step-help">{help}</p>}
      {children}
    </div>
  )
}

function ReaderCard({ form, index, reader, clientOptions, templateOptions, warehouseOptions, defaultOpen, onRemove }: { form: UseFormReturn<IntegrationValues>; index: number; reader: ReaderValues | undefined; clientOptions: Option[]; templateOptions: Option[]; warehouseOptions: Option[]; defaultOpen: boolean; onRemove: () => void }) {
  const t = useT()
  const [open, setOpen] = useState(defaultOpen)
  const [totalFor, setTotalFor] = useState<number | null>(null)
  const base = `email.readers.${index}`
  const conditions = useFieldArray({ control: form.control, name: `${base}.conditions` as 'email.readers.0.conditions' })
  const checks = useFieldArray({ control: form.control, name: `${base}.checks` as 'email.readers.0.checks' })
  const source = reader?.source ?? 'BODY'
  const opOptions = MATCH_OPERATORS.map((o) => ({ value: o, label: t(`integrations.email.operator.${o}`) }))
  const scopeOptions = CONDITION_SCOPES.filter((s) => scopeApplies(s, source)).map((s) => ({ value: s, label: t(`integrations.email.readers.scope.${s}`) }))
  const fieldsError = (form.formState.errors.email as { readers?: { message?: string }[] } | undefined)?.readers?.[index]
  const isNotice = reader?.target === 'ARRIVAL_NOTICE' || reader?.target === 'EXIT_NOTICE'
  const bodyId = `int-rcard-${index}`
  return (
    <article className="int-rcard" aria-label={t('integrations.email.readers.cardLabel', { n: index + 1, name: reader?.name || t('integrations.email.readers.unnamed') })}>
      <header className="int-rcard-h">
        <button type="button" className="int-rcard-toggle" aria-expanded={open} aria-controls={bodyId} onClick={() => setOpen(!open)}>
          <span className={`int-rcard-chev${open ? ' open' : ''}`} aria-hidden="true">
            <IconChevronDown />
          </span>
          <span className="int-rcard-n">{t('integrations.email.readers.cardTitle', { n: index + 1 })}</span>
          <span className="int-rcard-name">{reader?.name || t('integrations.email.readers.unnamed')}</span>
        </button>
        <span className="int-rcard-chips">
          <Chip tone="neutral">{t(`integrations.email.readers.sourceOption.${source}`)}</Chip>
          <Chip tone="route">{t(`integrations.email.readers.targetOption.${reader?.target ?? 'ORDER'}`)}</Chip>
        </span>
        <button type="button" className="btn sm int-rcard-remove" onClick={onRemove}>
          <IconTrash /> {t('integrations.email.readers.remove', { n: index + 1 })}
        </button>
      </header>

      {open && (
        <div className="int-rcard-b" id={bodyId}>
          <Step n={1} title={t('integrations.email.readers.step1')} help={t('integrations.email.readers.step1Help')}>
            <div className="r2">
              <Field name={`${base}.name`} label={t('integrations.email.readers.nameShort')} required>
                <TextInput placeholder={t('integrations.email.readers.namePlaceholder')} />
              </Field>
              <Field name={`${base}.source`} label={t('integrations.email.readers.source')}>
                <Select options={READER_SOURCES.map((s) => ({ value: s, label: t(`integrations.email.readers.sourceOption.${s}`) }))} />
              </Field>
            </div>
            <p className="help">{t('integrations.email.readers.whenHelp')}</p>
            <div className="int-conds">
              {conditions.fields.map((c, j) => (
                <div className="int-cond" key={c.id}>
                  <Field name={`${base}.conditions.${j}.scope`} label={t('integrations.email.readers.condScope', { n: j + 1 })} hideLabel>
                    <Select options={scopeOptions} />
                  </Field>
                  <Field name={`${base}.conditions.${j}.operator`} label={t('integrations.email.readers.condOperator', { n: j + 1 })} hideLabel>
                    <Select options={opOptions} />
                  </Field>
                  <Field name={`${base}.conditions.${j}.value`} label={t('integrations.email.readers.condValue', { n: j + 1 })} hideLabel required>
                    <TextInput placeholder={t('integrations.email.readers.condPlaceholder')} />
                  </Field>
                  <button type="button" className="btn sm int-iconbtn" title={t('integrations.email.readers.removeCondition', { n: j + 1 })} aria-label={t('integrations.email.readers.removeCondition', { n: j + 1 })} onClick={() => conditions.remove(j)}>
                    <IconTrash />
                  </button>
                </div>
              ))}
            </div>
            <button type="button" className="btn sm" onClick={() => conditions.append(emptyCondition(source))}>
              <IconPlus /> {t('integrations.email.readers.addCondition')}
            </button>
          </Step>

          <Step n={2} title={t('integrations.email.readers.step2')} help={t('integrations.email.readers.step2Help')}>
            <div className="r2">
              <Field name={`${base}.target`} label={t('integrations.email.readers.target')} help={t('integrations.email.readers.targetHelp')}>
                <Select options={READER_TARGETS.map((s) => ({ value: s, label: t(`integrations.email.readers.targetOption.${s}`) }))} />
              </Field>
              {isNotice && (
                <Field name={`${base}.warehousePublicId`} label={t('integrations.email.readers.warehouse')} help={t('integrations.email.readers.warehouseHelp')}>
                  <Select options={warehouseOptions} placeholder={t('integrations.email.readers.onlyActiveWarehouse')} />
                </Field>
              )}
              {reader?.target === 'ORDER_COMPLETION' && (
                <Field name={`${base}.toStatus`} label={t('integrations.email.readers.toStatus')} help={t('integrations.email.readers.toStatusHelp')}>
                  <Select options={COMPLETION_TARGETS.map((m) => ({ value: m, label: t(`integrations.email.readers.toStatusOption.${m}`) }))} />
                </Field>
              )}
            </div>
            {reader?.target === 'ORDER_COMPLETION' && (
              <Field name={`${base}.mode`} label={t('integrations.email.readers.mode')} help={t('integrations.email.readers.modeHelp')}>
                <Select options={READER_MODES.map((m) => ({ value: m, label: t(`integrations.email.readers.modeOption.${m}`) }))} />
              </Field>
            )}
            <div className="r2">
              <Field name={`${base}.clientPublicId`} label={t('integrations.email.readers.client')} help={t('integrations.email.readers.clientHelp')}>
                <Select options={clientOptions} placeholder={t('integrations.email.readers.sameAsIntegration')} />
              </Field>
              <Field name={`${base}.templatePublicId`} label={t('integrations.email.readers.template')} help={t('integrations.email.readers.templateHelp')}>
                <Select options={templateOptions} placeholder={t('integrations.email.readers.sameAsIntegration')} />
              </Field>
            </div>
          </Step>

          <Step n={3} title={t('integrations.email.readers.step3')} help={source === 'FILE' ? t('integrations.email.readers.fileHelp') : t('integrations.email.readers.step3Help')}>
            {source !== 'FILE' && <ReaderFieldsEditor form={form} base={`${base}.fields`} target={reader?.target ?? 'ORDER'} error={fieldsError?.message} />}
          </Step>

          {source !== 'FILE' && (
            <Step n={4} title={t('integrations.email.readers.step4')} help={t('integrations.email.readers.step4Help')}>
              {source === 'PDF' && (
                <Field name={`${base}.minConfidence`} label={t('integrations.email.readers.minConfidence')} help={t('integrations.email.readers.minConfidenceHelp')}>
                  <TextInput inputMode="numeric" maxLength={3} placeholder="80" />
                </Field>
              )}
              <p className="help">{t('integrations.email.readers.checksHelp')}</p>
              {checks.fields.map((c, j) => (
                <div className="int-check" key={c.id}>
                  <div className="int-check-h">
                    <strong>{t('integrations.email.readers.checkKind', { n: j + 1 })}</strong>
                    <button type="button" className="btn sm" aria-label={t('integrations.email.readers.removeCheck', { n: j + 1 })} onClick={() => checks.remove(j)}>
                      <IconTrash /> {t('integrations.email.readers.removeCheckShort')}
                    </button>
                  </div>
                  <div className="r3">
                    <Field name={`${base}.checks.${j}.kind`} label={t('integrations.email.readers.checkWhat')}>
                      <Select options={CHECK_KINDS.map((k) => ({ value: k, label: t(`integrations.email.readers.checkKindOption.${k}`) }))} />
                    </Field>
                    <Field name={`${base}.checks.${j}.name`} label={t('integrations.email.readers.checkName', { n: j + 1 })}>
                      <TextInput placeholder="Importes" />
                    </Field>
                    <Field name={`${base}.checks.${j}.position`} label={t('integrations.email.readers.checkPosition', { n: j + 1 })} required help={t('integrations.email.readers.checkPositionHelp')}>
                      <TextInput inputMode="numeric" maxLength={3} />
                    </Field>
                  </div>
                  <div className="int-datum-assist">
                    <button type="button" className="btn sm flow" onClick={() => setTotalFor(j)}>
                      {t('integrations.email.readers.checkAssist')}
                    </button>
                    <span className="help">{t('integrations.email.readers.checkAssistHelp')}</span>
                  </div>
                  <details className="int-adv">
                    <summary>{t('integrations.fields.seeExpression')}</summary>
                    <Field name={`${base}.checks.${j}.pattern`} label={t('integrations.email.readers.checkPattern', { n: j + 1 })} required help={t('integrations.email.readers.checkPatternHelp')}>
                      <TextInput placeholder="Total Amount USD\s+([\d,.]+)" />
                    </Field>
                  </details>
                  <Field name={`${base}.checks.${j}.scope`} label={t('integrations.email.readers.checkScope', { n: j + 1 })} help={t('integrations.email.readers.checkScopeHelp')}>
                    <Select options={CHECK_SCOPES.map((k) => ({ value: k, label: t(`integrations.email.readers.checkScopeOption.${k}`) }))} />
                  </Field>
                  <div className="r2">
                    {reader?.checks?.[j]?.kind !== 'SUM' && (
                      <Field name={`${base}.checks.${j}.distinct`} label={t('integrations.email.readers.checkDistinct', { n: j + 1 })} help={t('integrations.email.readers.checkDistinctHelp')}>
                        <Toggle text={t('integrations.email.readers.checkDistinct', { n: j + 1 })} />
                      </Field>
                    )}
                    <Field name={`${base}.checks.${j}.tolerance`} label={t('integrations.email.readers.checkTolerance', { n: j + 1 })} help={t('integrations.email.readers.checkToleranceHelp')}>
                      <TextInput inputMode="decimal" maxLength={8} />
                    </Field>
                  </div>
                </div>
              ))}
              <button type="button" className="btn sm" onClick={() => checks.append(emptyCheck())}>
                <IconPlus /> {t('integrations.email.readers.addCheck')}
              </button>
            </Step>
          )}

          {totalFor !== null && (
            <ReadingAssistant
              open
              mode="total"
              datums={null}
              onClose={() => setTotalFor(null)}
              onApply={(r) => {
                if (r.kind === 'TOTAL') form.setValue(`${base}.checks.${totalFor}.pattern` as 'email.readers.0.checks.0.pattern', r.pattern, { shouldDirty: true, shouldValidate: true })
                setTotalFor(null)
              }}
            />
          )}

          <details className="int-adv">
            <summary>{t('integrations.email.readers.advanced')}</summary>
            <div className="r2">
              <Field name={`${base}.recordStart`} label={t('integrations.email.readers.recordStart')} help={t('integrations.email.readers.recordStartHelp')}>
                <TextInput placeholder="Order #:" />
              </Field>
              <Field name={`${base}.firstOnly`} label={t('integrations.email.readers.firstOnly')} help={t('integrations.email.readers.firstOnlyHelp')}>
                <Toggle text={t('integrations.email.readers.firstOnly')} />
              </Field>
            </div>
          </details>
        </div>
      )}
    </article>
  )
}
