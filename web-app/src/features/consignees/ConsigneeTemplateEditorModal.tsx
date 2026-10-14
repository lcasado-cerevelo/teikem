// Lote F-B2 — Editor de plantilla de importación de CONSIGNATARIOS (reemplaza en esta pantalla al editor compartido con órdenes,
// que sigue sirviendo a `features/orders`): nombre, delimitador, ¿encabezado?, cliente por defecto, tabla «Columna N → Campo destino»
// con «(No usar)» (se guarda en la plantilla) y, si hay un archivo revisado (`inspect-file`), el encabezado y valores de muestra de
// cada columna. Una columna puede ir a más de un campo («Usar también en otro campo»: p. ej. el código y parte del nombre) y un campo
// de texto puede unir varias columnas con un separador por hueco y orden propio (sección «Campos con varias columnas», con vista
// previa del resultado). Valores por defecto como antes. POST o PATCH con `locations.create`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useFieldArray, useForm, useFormContext, useWatch, type Control } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Form, IconPlus, IconTrash, Modal, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { useSaveImportTemplate } from './api'
import {
  buildConsigneeTemplateCreate,
  buildConsigneeTemplatePatch,
  consigneeTemplateSchema,
  consigneeTemplateValuesOf,
  DELIMITERS,
  joinPreview,
  moveJoinPart,
  SEPARATOR_MAX,
  syncJoins,
  TEMPLATE_NAME_MAX,
  type ConsigneeTemplateValues,
  type ImportField,
  type ImportFileInspection,
  type ImportTemplate,
  type JoinDef,
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

export function ConsigneeTemplateEditorModal({ template, fields, fieldLabel, clients, inspection, fileName, onClose, onSaved }: ConsigneeTemplateEditorModalProps) {
  const t = useT()
  const save = useSaveImportTemplate()
  const isNew = template === null
  const fileColumns = inspection?.columns?.length ?? 0
  // valores iniciales una sola vez: el archivo revisado agrega filas «(No usar)» hasta su ancho
  const [initial] = useState(() => consigneeTemplateValuesOf(template, fileColumns))
  const [joins, setJoins] = useState<JoinDef[]>(initial.joins)

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
        },
        fields,
      ),
    [t, fields, fieldLabel],
  )
  const form = useForm<ConsigneeTemplateValues>({ resolver: zodResolver(schema), defaultValues: initial.values })
  const columns = useFieldArray({ control: form.control, name: 'columns' })
  const defaults = useFieldArray({ control: form.control, name: 'defaults' })
  const watchedColumns = useWatch({ control: form.control, name: 'columns' })
  const synced = useMemo(() => syncJoins(watchedColumns ?? [], joins), [watchedColumns, joins])

  // tras un intento de guardar, el error de la lista completa se vuelve a evaluar con cada cambio (si no, queda aunque ya esté corregido)
  const submitted = form.formState.isSubmitted
  useEffect(() => {
    if (!submitted) return
    const sub = form.watch((_, info) => {
      if (info.name?.startsWith('columns') || info.name?.startsWith('defaults')) void form.trigger('columns')
    })
    return () => sub.unsubscribe()
  }, [form, submitted])

  const columnsErrors = form.formState.errors.columns as { message?: string; root?: { message?: string } } | undefined
  const columnsError = columnsErrors?.root?.message ?? columnsErrors?.message

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
  /** Fila de muestra para la vista previa de una unión: la primera que trae algo en esas columnas. */
  const cellOf = (join: JoinDef) => {
    const rows = inspection?.sampleRows ?? []
    if (rows.length === 0) return () => undefined
    const row = rows.find((r) => join.positions.some((p) => (r[p - 1] ?? '').trim() !== '')) ?? rows[0]
    return (p: number) => row[p - 1] ?? ''
  }
  const updateJoin = (next: JoinDef) => setJoins(synced.map((j) => (j.field === next.field ? next : j)))

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
            ? await save.mutateAsync({ action: 'update', publicId: template.publicId, body: buildConsigneeTemplatePatch(v, synced) })
            : await save.mutateAsync({ action: 'create', body: buildConsigneeTemplateCreate(v, synced) })
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
                  control={form.control}
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

        {synced.length > 0 && (
          <section className="cs-joins" aria-label={t('consignees.import.joinsTitle')}>
            <h3 className="cs-sub">{t('consignees.import.joinsTitle')}</h3>
            <p className="help">{t('consignees.import.joinsHelp')}</p>
            {synced.map((join) => {
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
                    {join.positions.map((p, k) => (
                      <li key={p}>
                        {k > 0 && (
                          <label className="cs-sep">
                            <span>{t('consignees.import.separator')}</span>
                            <input
                              type="text"
                              value={join.separators[k - 1] ?? ''}
                              maxLength={SEPARATOR_MAX}
                              aria-label={t('consignees.import.separatorN', { field: label, n: k })}
                              onChange={(e) => updateJoin({ ...join, separators: join.separators.map((s, x) => (x === k - 1 ? e.target.value : s)) })}
                            />
                          </label>
                        )}
                        <span className="cs-part">{columnLabel(p)}</span>
                        <span className="cs-partbtns">
                          <button
                            type="button"
                            className="rowbtn"
                            disabled={k === 0}
                            title={t('consignees.import.moveUp')}
                            aria-label={t('consignees.import.moveUpN', { field: label, column: columnLabel(p) })}
                            onClick={() => updateJoin(moveJoinPart(join, k, -1))}
                          >
                            ↑
                          </button>
                          <button
                            type="button"
                            className="rowbtn"
                            disabled={k === join.positions.length - 1}
                            title={t('consignees.import.moveDown')}
                            aria-label={t('consignees.import.moveDownN', { field: label, column: columnLabel(p) })}
                            onClick={() => updateJoin(moveJoinPart(join, k, 1))}
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

        <h3 className="cs-sub">{t('consignees.import.defaultsTitle')}</h3>
        <p className="help">{t('consignees.import.defaultsHelp')}</p>
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

interface MappingRowProps {
  control: Control<ConsigneeTemplateValues>
  index: number
  targetOptions: { value: string; label: string }[]
  /** Columna del archivo revisado (null = sin archivo: no se pinta la celda). */
  file: { position?: number; header?: string | null; samples?: string[] | null } | null
  canRemove: boolean
  onRemove: () => void
}

/** Una columna del archivo: su campo (o «(No usar)») y, si se pide, otros campos que también alimenta. */
function MappingRow({ control, index, targetOptions, file, canRemove, onRemove }: MappingRowProps) {
  const t = useT()
  const n = index + 1
  const targets = useFieldArray({ control, name: `columns.${index}.fields` })
  const { setValue } = useFormContext<ConsigneeTemplateValues>()
  const first = useWatch({ control, name: `columns.${index}.fields.0.field` })
  // «(No usar)» en el primer campo quita los demás (una columna que no se usa no alimenta nada)
  useEffect(() => {
    if (!first && targets.fields.length > 1) setValue(`columns.${index}.fields`, [{ field: '' }], { shouldDirty: true })
  }, [first, targets.fields.length, index, setValue])

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
            <Field name={`columns.${index}.fields.${j}.field`} label={j === 0 ? t('consignees.import.columnN', { n }) : t('consignees.import.columnNAlso', { n, k: j + 1 })} hideLabel>
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
