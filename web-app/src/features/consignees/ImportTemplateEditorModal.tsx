// Editor de plantilla de importación de consignatarios (modal aparte de la maqueta `renderConsImpTplEditorHtml`): nombre,
// delimitador, ¿tiene encabezado?, tabla «Columna N → Campo destino» (campos de GET /locations/import/fields; «— no importar —»),
// agregar y quitar columnas y valores por defecto (solo de los campos que los admiten). POST o PATCH (`locations.create`);
// las plantillas se desactivan y reactivan en lugar de «eliminarse» (el servidor no tiene DELETE).
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Form, IconPlus, IconTrash, Modal, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { useSaveImportTemplate } from './api'
import {
  buildTemplateCreate,
  buildTemplatePatch,
  DELIMITERS,
  TEMPLATE_NAME_MAX,
  templateSchema,
  templateValuesOf,
  type ImportField,
  type ImportTemplate,
} from './consigneeRules'

export interface ImportTemplateEditorModalProps {
  /** null = plantilla nueva. */
  template: ImportTemplate | null
  fields: readonly ImportField[]
  /** Etiqueta del campo destino (ya traducida). */
  fieldLabel: (field: string) => string
  onClose: () => void
  onSaved: (template: ImportTemplate) => void
}

const DELIMITER_KEYS: Record<string, string> = { ',': 'comma', ';': 'semicolon', '\t': 'tab', '|': 'pipe' }

export function ImportTemplateEditorModal({ template, fields, fieldLabel, onClose, onSaved }: ImportTemplateEditorModalProps) {
  const t = useT()
  const save = useSaveImportTemplate()
  const formId = 'consignee-template'
  const isNew = template === null

  const schema = useMemo(
    () =>
      templateSchema(
        {
          nameRequired: t('consignees.import.errors.nameRequired'),
          nameMax: t('consignees.errors.max', { max: TEMPLATE_NAME_MAX }),
          columnsRequired: t('consignees.import.errors.columnsRequired'),
          nameLine1: t('consignees.import.errors.nameLine1'),
          city: t('consignees.import.errors.city'),
          clientBoth: t('consignees.import.errors.clientBoth'),
          repeated: (field) => t('consignees.import.errors.repeated', { field }),
          noDefault: (field) => t('consignees.import.errors.noDefault', { field }),
          defaultValueRequired: (field) => t('consignees.import.errors.defaultValueRequired', { field: fieldLabel(field) }),
        },
        fields,
      ),
    [t, fields, fieldLabel],
  )
  const form = useForm({ resolver: zodResolver(schema), values: templateValuesOf(template) })
  const columns = useFieldArray({ control: form.control, name: 'columns' })
  const defaults = useFieldArray({ control: form.control, name: 'defaults' })

  // tras un intento de guardar, el error de la lista completa («Indique al menos una columna», name y line1, ciudad…) se
  // vuelve a evaluar con cada cambio de columnas o valores por defecto; si no, se queda en pantalla aunque ya esté corregido
  const submitted = form.formState.isSubmitted
  useEffect(() => {
    if (!submitted) return
    const sub = form.watch((_, info) => {
      if (info.name?.startsWith('columns') || info.name?.startsWith('defaults')) void form.trigger('columns')
    })
    return () => sub.unsubscribe()
  }, [form, submitted])

  // el error de la lista completa («name y line1», ciudad…) no es de una fila: se ve bajo la tabla
  const columnsErrors = form.formState.errors.columns as { message?: string; root?: { message?: string } } | undefined
  const columnsError = columnsErrors?.root?.message ?? columnsErrors?.message

  const targetOptions = useMemo(
    () => fields.map((f) => ({ value: f.field ?? '', label: `${fieldLabel(f.field ?? '')}${f.required ? ' *' : ''}` })),
    [fields, fieldLabel],
  )
  const defaultOptions = useMemo(
    () => fields.filter((f) => f.defaultAllowed).map((f) => ({ value: f.field ?? '', label: fieldLabel(f.field ?? '') })),
    [fields, fieldLabel],
  )
  const delimiterOptions: { value: string; label: string }[] = DELIMITERS.map((d) => ({ value: d, label: t(`consignees.import.delimiters.${DELIMITER_KEYS[d]}`) }))
  // un delimitador guardado que el editor no ofrece (el servidor acepta cualquier carácter único) se conserva como opción
  const currentDelimiter = template?.delimiter
  if (currentDelimiter && !DELIMITERS.some((d) => d === currentDelimiter)) delimiterOptions.push({ value: currentDelimiter, label: `${currentDelimiter}` })

  return (
    <Modal
      open
      title={isNew ? t('consignees.import.tplNewTitle') : t('consignees.import.tplEditTitle')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn sm" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn sm flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('consignees.import.tplSave')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const saved = template?.publicId
            ? await save.mutateAsync({ action: 'update', publicId: template.publicId, body: buildTemplatePatch(v) })
            : await save.mutateAsync({ action: 'create', body: buildTemplateCreate(v) })
          toast.success(t(isNew ? 'consignees.import.tplCreated' : 'consignees.import.tplUpdated', { name: saved.name ?? '' }))
          onSaved(saved)
        }}
      >
        <Field name="name" label={t('consignees.import.tplName')} required>
          <TextInput maxLength={TEMPLATE_NAME_MAX} placeholder={t('consignees.import.tplNamePlaceholder')} />
        </Field>
        <div className="r2">
          <Field name="delimiter" label={t('consignees.import.delimiter')}>
            <Select options={delimiterOptions} />
          </Field>
          <Field name="hasHeader" label={t('consignees.import.header')}>
            <Toggle text={t('consignees.import.hasHeader')} />
          </Field>
        </div>
        <p className="help cs-hint">{t('consignees.import.tplEditorHint')}</p>

        <div className="cs-tablewrap">
          <table className="lst densetbl">
            <thead>
              <tr>
                <th>{t('consignees.import.colHeader')}</th>
                <th>{t('consignees.import.fieldHeader')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {columns.fields.map((col, i) => (
                <tr key={col.id}>
                  <td>
                    <b>{t('consignees.import.columnN', { n: i + 1 })}</b>
                  </td>
                  <td>
                    <Field name={`columns.${i}.field`} label={t('consignees.import.columnN', { n: i + 1 })} hideLabel>
                      <Select options={targetOptions} placeholder={t('consignees.import.ignore')} />
                    </Field>
                  </td>
                  <td className="cs-rm">
                    {columns.fields.length > 1 && (
                      <button type="button" className="rowbtn" title={t('consignees.import.removeColumn')} aria-label={t('consignees.import.removeColumnN', { n: i + 1 })} onClick={() => columns.remove(i)}>
                        <IconTrash />
                      </button>
                    )}
                  </td>
                </tr>
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
        <button type="button" className="btn sm" onClick={() => columns.append({ field: '' })}>
          <IconPlus /> {t('consignees.import.addColumn')}
        </button>

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
