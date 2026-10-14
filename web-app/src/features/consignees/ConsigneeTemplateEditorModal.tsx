// Lote F-B2 — Editor de plantilla de importación de CONSIGNATARIOS (reemplaza en esta pantalla al editor compartido con órdenes,
// que sigue sirviendo a `features/orders`): nombre, delimitador, ¿encabezado?, cliente por defecto, tabla «Columna N → Campo destino»
// con «(No usar)» (se guarda en la plantilla) y, si hay un archivo revisado (`inspect-file`), el encabezado y valores de muestra de
// cada columna. Una columna puede ir a más de un campo («Usar también en otro campo»: p. ej. el código y parte del nombre) y un campo
// de texto puede unir varias columnas con un separador por hueco y orden propio (sección «Campos con varias columnas», con vista
// previa del resultado). Valores por defecto como antes. POST o PATCH con `locations.create`.
// Lote F-B3 — VARIANTES de mapeo: un archivo que mezcla filas con columnas distintas se importa con una plantilla de varias variantes;
// cada una tiene nombre, condiciones sobre columnas («es igual a», «contiene», «no está vacía», «está vacía») y su propio mapeo, y a
// cada fila se le aplica la primera cuya condición se cumple. Pestañas por variante (agregar copia la actual; quitar; mover antes).
// Con una sola variante el editor es el de siempre y la plantilla viaja con `columns`. Los valores por defecto son de toda la plantilla.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useFieldArray, useForm, useFormContext, useWatch, type Control, type FieldErrors } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Form, IconPlus, IconTrash, Modal, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { useSaveImportTemplate } from './api'
import {
  buildConsigneeTemplateCreate,
  buildConsigneeTemplatePatch,
  CONDITION_OPERATORS,
  CONDITION_VALUE_MAX,
  conditionNeedsValue,
  consigneeTemplateSchema,
  consigneeTemplateValuesOf,
  DELIMITERS,
  joinPreview,
  MAX_CONDITIONS,
  MAX_VARIANTS,
  moveJoinPart,
  SEPARATOR_MAX,
  syncJoins,
  TEMPLATE_NAME_MAX,
  VARIANT_NAME_MAX,
  variantPath,
  variantsOf,
  variantsWithIssues,
  type ConsigneeTemplateValues,
  type ImportField,
  type ImportFileInspection,
  type ImportTemplate,
  type JoinDef,
  type MappingColumn,
  type VariantValues,
} from './consigneeRules'

export interface ConsigneeTemplateEditorModalProps {
  /** null = plantilla nueva. */
  template: ImportTemplate | null
  fields: readonly ImportField[]
  /** Etiqueta del campo destino (ya traducida). */
  fieldLabel: (field: string) => string
  /** Clientes que se ofrecen como cliente por defecto ({ value: publicId, label }). */
  clients: readonly { value: string; label: string }[]
  /** Archivo revisado en el modal de importación (encabezados y muestras), si lo hay. */
  inspection?: ImportFileInspection | null
  fileName?: string | null
  onClose: () => void
  onSaved: (template: ImportTemplate) => void
}

const DELIMITER_KEYS: Record<string, string> = { ',': 'comma', ';': 'semicolon', '\t': 'tab', '|': 'pipe' }

/** Rutas con error (aplanadas) del formulario, para marcar la pestaña de la variante que los tiene. */
function errorPaths(errors: FieldErrors, prefix = ''): string[] {
  const out: string[] = []
  for (const [key, value] of Object.entries(errors ?? {})) {
    if (!value || key === 'ref') continue
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof value === 'object' && 'message' in value && typeof value.message === 'string') out.push(path)
    if (typeof value === 'object') out.push(...errorPaths(value as FieldErrors, path))
  }
  return out
}

export function ConsigneeTemplateEditorModal({ template, fields, fieldLabel, clients, inspection, fileName, onClose, onSaved }: ConsigneeTemplateEditorModalProps) {
  const t = useT()
  const save = useSaveImportTemplate()
  const isNew = template === null
  const hadVariants = (template?.variants?.length ?? 0) > 0
  const fileColumns = inspection?.columns?.length ?? 0
  // valores iniciales una sola vez: el archivo revisado agrega filas «(No usar)» hasta su ancho
  const [initial] = useState(() => consigneeTemplateValuesOf(template, fileColumns))
  // uniones por variante (0 = la 1)
  const [joins, setJoins] = useState<JoinDef[][]>(() => [initial.joins, ...initial.moreJoins])
  const [active, setActive] = useState(0)
  // cambia con cada cambio de estructura (quitar o mover variantes): el panel se vuelve a montar con los valores nuevos
  const [version, setVersion] = useState(0)

  const schema = useMemo(
    () =>
      consigneeTemplateSchema(
        {
          nameRequired: t('consignees.import.errors.nameRequired'),
          nameMax: t('consignees.errors.max', { max: TEMPLATE_NAME_MAX }),
          columnsRequired: t('consignees.import.errors.columnsRequired'),
          nameLine1: t('consignees.import.errors.nameLine1'),
          city: t('consignees.import.errors.city'),
          clientBoth: t('consignees.import.errors.clientBoth'),
          repeated: (field) => t('consignees.import.errors.repeated', { field }),
          tooManyParts: (field) => t('consignees.import.errors.tooManyParts', { field }),
          noDefault: (field) => t('consignees.import.errors.noDefault', { field }),
          defaultValueRequired: (field) => t('consignees.import.errors.defaultValueRequired', { field: fieldLabel(field) }),
          conditionColumn: t('consignees.import.errors.conditionColumn'),
          conditionValue: t('consignees.import.errors.conditionValue'),
          unreachable: (n) => t('consignees.import.errors.unreachable', { n }),
        },
        fields,
      ),
    [t, fields, fieldLabel],
  )
  const form = useForm<ConsigneeTemplateValues>({ resolver: zodResolver(schema), defaultValues: initial.values })
  const defaults = useFieldArray({ control: form.control, name: 'defaults' })
  const moreVariants = useFieldArray({ control: form.control, name: 'moreVariants' })
  const variantCount = 1 + moreVariants.fields.length
  const watchedColumns = useWatch({ control: form.control, name: 'columns' })
  const watchedMore = useWatch({ control: form.control, name: 'moreVariants' })
  const watchedFirstName = useWatch({ control: form.control, name: 'variantName' })
  const synced = useMemo(
    () => [watchedColumns ?? [], ...(watchedMore ?? []).map((v) => v?.columns ?? [])].map((cols, k) => syncJoins(cols, joins[k] ?? [])),
    [watchedColumns, watchedMore, joins],
  )

  // tras un intento de guardar, los errores de las listas se vuelven a evaluar con cada cambio (si no, quedan aunque ya estén corregidos)
  const submitted = form.formState.isSubmitted
  useEffect(() => {
    if (!submitted) return
    const sub = form.watch(() => void form.trigger())
    return () => sub.unsubscribe()
  }, [form, submitted])

  const flatErrors = errorPaths(form.formState.errors)
  const withIssues = variantsWithIssues(flatErrors, variantCount)

  const targetOptions = useMemo(
    () => fields.map((f) => ({ value: f.field ?? '', label: `${fieldLabel(f.field ?? '')}${f.required ? ' *' : ''}` })),
    [fields, fieldLabel],
  )
  const defaultOptions = useMemo(() => fields.filter((f) => f.defaultAllowed).map((f) => ({ value: f.field ?? '', label: fieldLabel(f.field ?? '') })), [fields, fieldLabel])
  const delimiterOptions: { value: string; label: string }[] = DELIMITERS.map((d) => ({ value: d, label: t(`consignees.import.delimiters.${DELIMITER_KEYS[d]}`) }))
  const currentDelimiter = template?.delimiter
  if (currentDelimiter && !DELIMITERS.some((d) => d === currentDelimiter)) delimiterOptions.push({ value: currentDelimiter, label: `${currentDelimiter}` })
  // una plantilla de un cliente solo admite ese cliente por defecto (o ninguno)
  const clientOptions = template?.clientPublicId ? clients.filter((c) => c.value === template.clientPublicId) : [...clients]
  if (template?.defaultClientPublicId && !clientOptions.some((c) => c.value === template.defaultClientPublicId))
    clientOptions.push({ value: template.defaultClientPublicId, label: template.defaultClientName ?? template.defaultClientPublicId })

  const headerOf = (position: number) => inspection?.columns?.find((c) => c.position === position)?.header ?? null
  const columnLabel = (position: number) => {
    const header = headerOf(position)
    return header ? t('consignees.import.columnNamed', { n: position, header }) : t('consignees.import.columnN', { n: position })
  }

  /** Cambia la lista de variantes (quitar, mover) y vuelve a montar el panel en `nextActive`. */
  const applyVariants = (list: VariantValues[], joinList: JoinDef[][], nextActive: number) => {
    const [first, ...rest] = list
    form.setValue('variantName', first.name, { shouldDirty: true })
    form.setValue('conditions', rest.length === 0 ? [] : first.conditions, { shouldDirty: true })
    form.setValue('columns', first.columns, { shouldDirty: true })
    moreVariants.replace(rest)
    setJoins(joinList)
    setActive(nextActive)
    setVersion((v) => v + 1)
  }
  const currentVariants = () => variantsOf(form.getValues())
  const copyColumns = (cols: readonly MappingColumn[]): MappingColumn[] => cols.map((c) => ({ fields: c.fields.map((f) => ({ field: f.field })) }))
  const addVariant = () => {
    const list = currentVariants()
    const source = list[active] ?? list[0]
    moreVariants.append({ name: '', conditions: [{ position: '', operator: 'EQUALS', value: '' }], columns: copyColumns(source.columns) })
    setJoins([...synced, (synced[active] ?? []).map((j) => ({ ...j, positions: [...j.positions], separators: [...j.separators] }))])
    setActive(list.length)
  }
  const removeVariant = (k: number) => {
    const list = currentVariants().filter((_, i) => i !== k)
    applyVariants(list, synced.filter((_, i) => i !== k), Math.max(0, k - 1))
  }
  const moveEarlier = (k: number) => {
    if (k === 0) return
    const list = currentVariants()
    ;[list[k - 1], list[k]] = [list[k], list[k - 1]]
    const joinList = [...synced]
    ;[joinList[k - 1], joinList[k]] = [joinList[k], joinList[k - 1]]
    applyVariants(list, joinList, k - 1)
  }
  const variantTitle = (k: number) => {
    const name = (k === 0 ? watchedFirstName : watchedMore?.[k - 1]?.name)?.trim()
    return name ? t('consignees.import.variantNamed', { n: k + 1, name }) : t('consignees.import.variantN', { n: k + 1 })
  }
  const width = Math.max(fileColumns, ...[watchedColumns ?? [], ...(watchedMore ?? []).map((v) => v?.columns ?? [])].map((c) => c.length))
  const conditionColumns = Array.from({ length: Math.max(width, 1) }, (_, i) => ({ value: String(i + 1), label: columnLabel(i + 1) }))

  return (
    <Modal
      open
      size="lg"
      title={isNew ? t('consignees.import.tplNewTitle') : t('consignees.import.tplEditTitle')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn sm" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form="consignee-template" className="btn sm flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('consignees.import.tplSave')}
          </button>
        </>
      }
    >
      <Form
        id="consignee-template"
        form={form}
        onSubmit={async (v) => {
          const saved = template?.publicId
            ? await save.mutateAsync({ action: 'update', publicId: template.publicId, body: buildConsigneeTemplatePatch(v, synced[0], synced.slice(1), hadVariants) })
            : await save.mutateAsync({ action: 'create', body: buildConsigneeTemplateCreate(v, synced[0], synced.slice(1)) })
          toast.success(t(isNew ? 'consignees.import.tplCreated' : 'consignees.import.tplUpdated', { name: saved.name ?? '' }))
          onSaved(saved)
        }}
      >
        <Field name="name" label={t('consignees.import.tplName')} required>
          <TextInput maxLength={TEMPLATE_NAME_MAX} placeholder={t('consignees.import.tplNamePlaceholder')} />
        </Field>
        <div className="r2">
          <Field name="delimiter" label={t('consignees.import.delimiter')} help={t('consignees.import.delimiterHelp')}>
            <Select options={delimiterOptions} />
          </Field>
          <Field name="hasHeader" label={t('consignees.import.header')}>
            <Toggle text={t('consignees.import.hasHeader')} />
          </Field>
        </div>
        <Field name="defaultClientPublicId" label={t('consignees.import.defaultClient')} help={t('consignees.import.defaultClientHelp')}>
          <Select options={clientOptions} placeholder={t('consignees.import.defaultClientNone')} />
        </Field>
        <p className="help cs-hint">
          {inspection ? t('consignees.import.tplEditorFileHint', { file: fileName ?? '', sheet: inspection.sheet ?? '' }) : t('consignees.import.tplEditorHint')}
        </p>

        <section className="cs-variants" aria-label={t('consignees.import.variantsTitle')}>
          {variantCount > 1 ? (
            <>
              <h3 className="cs-sub">{t('consignees.import.variantsTitle')}</h3>
              <p className="help">{t('consignees.import.variantsHelp')}</p>
              <div className="cs-vtabs" role="tablist" aria-label={t('consignees.import.variantsTitle')}>
                {Array.from({ length: variantCount }, (_, k) => (
                  <button
                    key={k}
                    type="button"
                    role="tab"
                    aria-selected={k === active}
                    className={`cs-vtab${k === active ? ' on' : ''}${withIssues.includes(k) ? ' bad' : ''}`}
                    onClick={() => setActive(k)}
                  >
                    {variantTitle(k)}
                    {withIssues.includes(k) && <span className="cs-vbad"> · {t('consignees.import.variantHasErrors')}</span>}
                  </button>
                ))}
                <button type="button" className="btn sm" onClick={addVariant} disabled={variantCount >= MAX_VARIANTS}>
                  <IconPlus /> {t('consignees.import.addVariant')}
                </button>
              </div>
            </>
          ) : (
            <p className="help cs-vsingle">
              {t('consignees.import.variantsSingleHelp')}{' '}
              <button type="button" className="cs-addtarget" onClick={addVariant}>
                + {t('consignees.import.addVariant')}
              </button>
            </p>
          )}

          <VariantPanel
            key={`${active}-${version}`}
            k={active}
            count={variantCount}
            control={form.control}
            targetOptions={targetOptions}
            inspection={inspection ?? null}
            columnLabel={columnLabel}
            conditionColumns={conditionColumns}
            joins={synced[active] ?? []}
            fieldLabel={fieldLabel}
            onJoin={(next) => setJoins(synced.map((list, k) => (k === active ? list.map((j) => (j.field === next.field ? next : j)) : list)))}
            onRemove={() => removeVariant(active)}
            onMoveEarlier={() => moveEarlier(active)}
          />
        </section>

        <h3 className="cs-sub">{t('consignees.import.defaultsTitle')}</h3>
        <p className="help">{variantCount > 1 ? t('consignees.import.defaultsVariantsHelp') : t('consignees.import.defaultsHelp')}</p>
        {defaults.fields.map((d, i) => (
          <div className="cs-default" key={d.id}>
            <Field name={`defaults.${i}.field`} label={t('consignees.import.defaultField', { n: i + 1 })} hideLabel>
              <Select options={defaultOptions} placeholder={t('consignees.import.defaultPick')} />
            </Field>
            <Field name={`defaults.${i}.value`} label={t('consignees.import.defaultValue', { n: i + 1 })} hideLabel>
              <TextInput maxLength={200} placeholder={t('consignees.import.defaultValuePlaceholder')} />
            </Field>
            <button type="button" className="rowbtn" title={t('consignees.import.removeDefault')} aria-label={t('consignees.import.removeDefaultN', { n: i + 1 })} onClick={() => defaults.remove(i)}>
              <IconTrash />
            </button>
          </div>
        ))}
        <button type="button" className="btn sm" onClick={() => defaults.append({ field: '', value: '' })}>
          <IconPlus /> {t('consignees.import.addDefault')}
        </button>
      </Form>
    </Modal>
  )
}

interface VariantPanelProps {
  /** Variante (0 = la 1). */
  k: number
  count: number
  control: Control<ConsigneeTemplateValues>
  targetOptions: { value: string; label: string }[]
  inspection: ImportFileInspection | null
  columnLabel: (position: number) => string
  conditionColumns: { value: string; label: string }[]
  joins: JoinDef[]
  fieldLabel: (field: string) => string
  onJoin: (next: JoinDef) => void
  onRemove: () => void
  onMoveEarlier: () => void
}

/** Una variante: nombre y condiciones (si hay varias), su tabla de columnas y sus uniones. */
function VariantPanel({ k, count, control, targetOptions, inspection, columnLabel, conditionColumns, joins, fieldLabel, onJoin, onRemove, onMoveEarlier }: VariantPanelProps) {
  const t = useT()
  const base = variantPath(k)
  const { formState } = useFormContext<ConsigneeTemplateValues>()
  // rutas dinámicas de la variante: la 1 en los campos de arriba, las demás en moreVariants.N
  const columns = useFieldArray({ control, name: `${base}columns` as 'columns' })
  const conditions = useFieldArray({ control, name: `${base}conditions` as 'conditions' })
  const watchedConditions = useWatch({ control, name: `${base}conditions` as 'conditions' })
  const multi = count > 1

  const errorAt = (path: string) => {
    let node: unknown = formState.errors
    for (const part of path.split('.')) node = node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined
    const e = node as { message?: string; root?: { message?: string } } | undefined
    return e?.root?.message ?? e?.message
  }
  const columnsError = errorAt(`${base}columns`)
  const conditionsError = errorAt(`${base}conditions`)

  /** Fila de muestra para la vista previa de una unión: la primera que trae algo en esas columnas. */
  const cellOf = (join: JoinDef) => {
    const rows = inspection?.sampleRows ?? []
    if (rows.length === 0) return () => undefined
    const row = rows.find((r) => join.positions.some((p) => (r[p - 1] ?? '').trim() !== '')) ?? rows[0]
    return (p: number) => row[p - 1] ?? ''
  }
  const operatorOptions = CONDITION_OPERATORS.map((op) => ({ value: op, label: t(`consignees.import.operators.${op}`) }))

  return (
    <div className="cs-vpanel" role={multi ? 'tabpanel' : undefined} aria-label={multi ? t('consignees.import.variantN', { n: k + 1 }) : undefined}>
      {multi && (
        <>
          <div className="cs-vhead">
            <Field name={`${base}${k === 0 ? 'variantName' : 'name'}`} label={t('consignees.import.variantName', { n: k + 1 })} help={t('consignees.import.variantNameHelp')}>
              <TextInput maxLength={VARIANT_NAME_MAX} placeholder={t('consignees.import.variantN', { n: k + 1 })} />
            </Field>
            <span className="cs-vbtns">
              {k > 0 && (
                <button type="button" className="btn sm" onClick={onMoveEarlier} aria-label={t('consignees.import.moveVariantN', { n: k + 1 })}>
                  ← {t('consignees.import.moveVariant')}
                </button>
              )}
              <button type="button" className="btn sm" onClick={onRemove} aria-label={t('consignees.import.removeVariantN', { n: k + 1 })}>
                <IconTrash /> {t('consignees.import.removeVariant')}
              </button>
            </span>
          </div>
          <div className="cs-conds">
            <b className="cs-condtitle">{t('consignees.import.conditionsTitle')}</b>
            <p className="help">{t('consignees.import.conditionsHelp')}</p>
            {conditions.fields.map((c, j) => {
              const op = watchedConditions?.[j]?.operator ?? 'EQUALS'
              return (
                <div className="cs-cond" key={c.id}>
                  <Field name={`${base}conditions.${j}.position`} label={t('consignees.import.conditionColumn', { n: j + 1 })} hideLabel>
                    <Select options={conditionColumns} placeholder={t('consignees.import.conditionColumnPick')} />
                  </Field>
                  <Field name={`${base}conditions.${j}.operator`} label={t('consignees.import.conditionOperator', { n: j + 1 })} hideLabel>
                    <Select options={operatorOptions} />
                  </Field>
                  {conditionNeedsValue(op) ? (
                    <Field name={`${base}conditions.${j}.value`} label={t('consignees.import.conditionValue', { n: j + 1 })} hideLabel>
                      <TextInput maxLength={CONDITION_VALUE_MAX} placeholder={t('consignees.import.conditionValuePlaceholder')} />
                    </Field>
                  ) : (
                    <span className="cs-condnone" />
                  )}
                  <button type="button" className="rowbtn" title={t('consignees.import.removeCondition')} aria-label={t('consignees.import.removeConditionN', { n: j + 1 })} onClick={() => conditions.remove(j)}>
                    <IconTrash />
                  </button>
                </div>
              )
            })}
            {conditions.fields.length === 0 && <p className="help cs-always">{t('consignees.import.conditionsNone')}</p>}
            {conditionsError && (
              <p className="ferr" role="alert">
                {conditionsError}
              </p>
            )}
            <button type="button" className="btn sm" onClick={() => conditions.append({ position: '', operator: 'EQUALS', value: '' })} disabled={conditions.fields.length >= MAX_CONDITIONS}>
              <IconPlus /> {t('consignees.import.addCondition')}
            </button>
          </div>
        </>
      )}

      <div className="cs-tablewrap cs-tplwrap">
        <table className="lst densetbl cs-maptbl">
          <thead>
            <tr>
              <th>{t('consignees.import.colHeader')}</th>
              {inspection && <th>{t('consignees.import.inFile')}</th>}
              <th>{t('consignees.import.fieldHeader')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {columns.fields.map((col, i) => (
              <MappingRow
                key={col.id}
                control={control}
                base={base}
                index={i}
                targetOptions={targetOptions}
                file={inspection ? (inspection.columns?.find((c) => c.position === i + 1) ?? { position: i + 1, header: null, samples: [] }) : null}
                canRemove={columns.fields.length > 1}
                onRemove={() => columns.remove(i)}
              />
            ))}
          </tbody>
        </table>
      </div>
      {columnsError && (
        <p className="ferr" role="alert">
          {columnsError}
        </p>
      )}
      <p className="help">{t('consignees.import.requiredHelp')}</p>
      <button type="button" className="btn sm" onClick={() => columns.append({ fields: [{ field: '' }] })}>
        <IconPlus /> {t('consignees.import.addColumn')}
      </button>

      {joins.length > 0 && (
        <section className="cs-joins" aria-label={t('consignees.import.joinsTitle')}>
          <h3 className="cs-sub">{t('consignees.import.joinsTitle')}</h3>
          <p className="help">{t('consignees.import.joinsHelp')}</p>
          {joins.map((join) => {
            const label = fieldLabel(join.field)
            return (
              <div className="cs-join" key={join.field}>
                <div className="cs-joinhead">
                  <b>{label}</b>
                  <span className="cs-joinprev">
                    {t('consignees.import.joinPreview')}: <q>{joinPreview(join, cellOf(join), columnLabel)}</q>
                  </span>
                </div>
                <ol className="cs-joinparts">
                  {join.positions.map((p, x) => (
                    <li key={p}>
                      {x > 0 && (
                        <label className="cs-sep">
                          <span>{t('consignees.import.separator')}</span>
                          <input
                            type="text"
                            value={join.separators[x - 1] ?? ''}
                            maxLength={SEPARATOR_MAX}
                            aria-label={t('consignees.import.separatorN', { field: label, n: x })}
                            onChange={(e) => onJoin({ ...join, separators: join.separators.map((s, y) => (y === x - 1 ? e.target.value : s)) })}
                          />
                        </label>
                      )}
                      <span className="cs-part">{columnLabel(p)}</span>
                      <span className="cs-partbtns">
                        <button
                          type="button"
                          className="rowbtn"
                          disabled={x === 0}
                          title={t('consignees.import.moveUp')}
                          aria-label={t('consignees.import.moveUpN', { field: label, column: columnLabel(p) })}
                          onClick={() => onJoin(moveJoinPart(join, x, -1))}
                        >
                          ↑
                        </button>
                        <button
                          type="button"
                          className="rowbtn"
                          disabled={x === join.positions.length - 1}
                          title={t('consignees.import.moveDown')}
                          aria-label={t('consignees.import.moveDownN', { field: label, column: columnLabel(p) })}
                          onClick={() => onJoin(moveJoinPart(join, x, 1))}
                        >
                          ↓
                        </button>
                      </span>
                    </li>
                  ))}
                </ol>
              </div>
            )
          })}
          <p className="help">{t('consignees.import.separatorHelp')}</p>
        </section>
      )}
    </div>
  )
}

interface MappingRowProps {
  control: Control<ConsigneeTemplateValues>
  /** Prefijo de la variante ('' = la 1; `moreVariants.N.` las demás). */
  base: string
  index: number
  targetOptions: { value: string; label: string }[]
  /** Columna del archivo revisado (null = sin archivo: no se pinta la celda). */
  file: { position?: number; header?: string | null; samples?: string[] | null } | null
  canRemove: boolean
  onRemove: () => void
}

/** Una columna del archivo: su campo (o «(No usar)») y, si se pide, otros campos que también alimenta. */
function MappingRow({ control, base, index, targetOptions, file, canRemove, onRemove }: MappingRowProps) {
  const t = useT()
  const n = index + 1
  const name = `${base}columns.${index}.fields` as `columns.${number}.fields`
  const targets = useFieldArray({ control, name })
  const { setValue } = useFormContext<ConsigneeTemplateValues>()
  const first = useWatch({ control, name: `${name}.0.field` as `columns.${number}.fields.0.field` })
  // «(No usar)» en el primer campo quita los demás (una columna que no se usa no alimenta nada)
  useEffect(() => {
    if (!first && targets.fields.length > 1) setValue(name, [{ field: '' }], { shouldDirty: true })
  }, [first, targets.fields.length, name, setValue])

  return (
    <tr>
      <td>
        <b>{t('consignees.import.columnN', { n })}</b>
      </td>
      {file && (
        <td className="cs-filecol">
          <span className="cs-head">{file.header || '—'}</span>
          {(file.samples?.length ?? 0) > 0 && <span className="cs-sample">{file.samples?.join(' · ')}</span>}
        </td>
      )}
      <td>
        {targets.fields.map((f, j) => (
          <div className="cs-target" key={f.id}>
            <Field name={`${name}.${j}.field`} label={j === 0 ? t('consignees.import.columnN', { n }) : t('consignees.import.columnNAlso', { n, k: j + 1 })} hideLabel>
              <Select options={targetOptions} placeholder={j === 0 ? t('consignees.import.ignore') : t('consignees.import.defaultPick')} />
            </Field>
            {j > 0 && (
              <button type="button" className="rowbtn" title={t('consignees.import.removeTarget')} aria-label={t('consignees.import.removeTargetN', { n, k: j + 1 })} onClick={() => targets.remove(j)}>
                <IconTrash />
              </button>
            )}
          </div>
        ))}
        {first && (
          <button type="button" className="cs-addtarget" onClick={() => targets.append({ field: '' })}>
            + {t('consignees.import.addTarget')}
          </button>
        )}
      </td>
      <td className="cs-rm">
        {canRemove && (
          <button type="button" className="rowbtn" title={t('consignees.import.removeColumn')} aria-label={t('consignees.import.removeColumnN', { n })} onClick={onRemove}>
            <IconTrash />
          </button>
        )}
      </td>
    </tr>
  )
}
