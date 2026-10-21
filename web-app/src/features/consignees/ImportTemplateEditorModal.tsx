// Editor de plantilla de importación de consignatarios (modal aparte de la maqueta `renderConsImpTplEditorHtml`): nombre,
// delimitador, ¿tiene encabezado?, tabla «Columna N → Campo destino» (campos de GET /locations/import/fields; «— no importar —»),
// agregar y quitar columnas y valores por defecto (solo de los campos que los admiten). POST o PATCH (`locations.create`);
// las plantillas se desactivan y reactivan en lugar de «eliminarse» (el servidor no tiene DELETE).
// Lote F-C3: el MISMO editor sirve a las plantillas de ÓRDENES (`kind="ORDER"`, `/import-templates`, `orders.edit`): cambian las reglas de la
// definición (consignatario + paquete), los textos (`orders.import.*`) y el endpoint.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Form, IconPlus, IconTrash, Modal, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { useSaveOrderImportTemplate } from '../orders/importApi'
import { orderTemplateIssues } from '../orders/orderImportRules'
import { useSaveEntityImportTemplate } from '../imports/entityImportApi'
import { entityTemplateIssues, type ImportEntity } from '../imports/entityImportRules'
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
  /** Tipo de plantilla: consignatarios (por omisión) u órdenes (Lote F-C3). */
  kind?: 'CONSIGNEE' | 'ORDER' | ImportEntity
  /** null = plantilla nueva. */
  template: ImportTemplate | null
  fields: readonly ImportField[]
  /** Etiqueta del campo destino (ya traducida). */
  fieldLabel: (field: string) => string
  onClose: () => void
  onSaved: (template: ImportTemplate) => void
}

const DELIMITER_KEYS: Record<string, string> = { ',': 'comma', ';': 'semicolon', '\t': 'tab', '|': 'pipe' }

export function ImportTemplateEditorModal({ kind = 'CONSIGNEE', template, fields, fieldLabel, onClose, onSaved }: ImportTemplateEditorModalProps) {
  const tr = useT()
  const isOrder = kind === 'ORDER'
  const isProduct = kind === 'PRODUCT' || kind === 'SUPPLIER'   // 2026-10-21: plantilla de una entidad del motor genérico (productos, proveedores); sus textos comunes son los de `orders.import.*`
  // los textos de las plantillas de órdenes viven en `orders.import.*`; los comunes (cancelar, cargando, tope de nombre) no cambian
  const ns = isOrder || isProduct ? 'orders.import' : 'consignees.import'
  // el editor se escribió con las claves `consignees.import.*`: con órdenes se leen de `orders.import.*` (mismas subclaves, mismos textos comunes)
  const t = (key: string, params?: Record<string, string | number>) => (key.startsWith('consignees.import.') ? tr(`${ns}.${key.slice('consignees.import.'.length)}`, params) : tr(key, params))
  const saveConsignee = useSaveImportTemplate()
  const saveOrder = useSaveOrderImportTemplate()
  const entity: ImportEntity = kind === 'SUPPLIER' ? 'SUPPLIER' : 'PRODUCT'
  const saveEntity = useSaveEntityImportTemplate(entity)
  const save = isProduct ? saveEntity : isOrder ? saveOrder : saveConsignee
  const formId = isProduct ? 'entity-template' : isOrder ? 'order-template' : 'consignee-template'
  const isNew = template === null

  const schema = useMemo(
    () =>
      isProduct
        ? templateSchema(
            { nameRequired: tr('orders.import.errors.nameRequired'), nameMax: tr('consignees.errors.max', { max: TEMPLATE_NAME_MAX }) },
            fields,
            (v) =>
              entityTemplateIssues(entity, v, fields, {
                columnsRequired: tr('orders.import.errors.columnsRequired'),
                requirement: (key) => tr(key),
                repeated: (field) => tr('orders.import.errors.repeated', { field }),
                noDefault: (field) => tr('orders.import.errors.noDefault', { field }),
                defaultValueRequired: (field) => tr('orders.import.errors.defaultValueRequired', { field: fieldLabel(field) }),
              }),
          )
        : isOrder
        ? templateSchema(
            { nameRequired: tr('orders.import.errors.nameRequired'), nameMax: tr('consignees.errors.max', { max: TEMPLATE_NAME_MAX }) },
            fields,
            (v) =>
              orderTemplateIssues(v, fields, {
                columnsRequired: tr('orders.import.errors.columnsRequired'),
                consignee: tr('orders.import.errors.consignee'),
                packages: tr('orders.import.errors.packages'),
                repeated: (field) => tr('orders.import.errors.repeated', { field }),
                noDefault: (field) => tr('orders.import.errors.noDefault', { field }),
                defaultValueRequired: (field) => tr('orders.import.errors.defaultValueRequired', { field: fieldLabel(field) }),
                multiLineOrder: tr('orders.import.errors.multiLineOrder'),
                ruleColumn: (n) => tr('orders.import.errors.ruleColumn', { n }),
                ruleValue: (n) => tr('orders.import.errors.ruleValue', { n }),
              }),
          )
        : templateSchema(
            {
              nameRequired: tr('consignees.import.errors.nameRequired'),
              nameMax: tr('consignees.errors.max', { max: TEMPLATE_NAME_MAX }),
              columnsRequired: tr('consignees.import.errors.columnsRequired'),
              nameLine1: tr('consignees.import.errors.nameLine1'),
              city: tr('consignees.import.errors.city'),
              clientBoth: tr('consignees.import.errors.clientBoth'),
              repeated: (field) => tr('consignees.import.errors.repeated', { field }),
              noDefault: (field) => tr('consignees.import.errors.noDefault', { field }),
              defaultValueRequired: (field) => tr('consignees.import.errors.defaultValueRequired', { field: fieldLabel(field) }),
            },
            fields,
          ),
    [tr, isOrder, isProduct, entity, fields, fieldLabel],
  )
  const initial = useMemo(() => {
    const v = templateValuesOf(template)
    return isOrder ? { ...v, multiLine: v.multiLine ?? false, lineRules: v.lineRules ?? [] } : v
  }, [template, isOrder])
  const form = useForm({ resolver: zodResolver(schema), values: initial })
  const columns = useFieldArray({ control: form.control, name: 'columns' })
  const defaults = useFieldArray({ control: form.control, name: 'defaults' })
  const lineRules = useFieldArray({ control: form.control, name: 'lineRules' })
  const multiLine = form.watch('multiLine') === true

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
            ? await save.mutateAsync({ action: 'update', publicId: template.publicId, body: buildTemplatePatch(v, template.multiLine === true) })
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

        <div className="cs-tablewrap cs-tplwrap">
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

        {isOrder && (
          <>
            <h3 className="cs-sub">{t('consignees.import.multiLine')}</h3>
            <Field name="multiLine" label={t('consignees.import.multiLine')} hideLabel>
              <Toggle text={t('consignees.import.multiLine')} />
            </Field>
            <p className="help">{t('consignees.import.multiLineHelp')}</p>
            {multiLine && (
              <>
                <h4 className="cs-sub">{t('consignees.import.rulesTitle')}</h4>
                <p className="help">{t('consignees.import.rulesHelp')}</p>
                {lineRules.fields.map((r, i) => (
                  <div className="cs-default cs-rule" key={r.id}>
                    <Field name={`lineRules.${i}.kind`} label={t('consignees.import.ruleKind', { n: i + 1 })} hideLabel>
                      <Select options={['DESCRIPTION', 'INVOICE'].map((k) => ({ value: k, label: t(`consignees.import.lineKinds.${k}`) }))} />
                    </Field>
                    <Field name={`lineRules.${i}.position`} label={t('consignees.import.ruleColumn', { n: i + 1 })} hideLabel>
                      <TextInput maxLength={3} inputMode="numeric" placeholder={t('consignees.import.colHeader')} />
                    </Field>
                    <Field name={`lineRules.${i}.operator`} label={t('consignees.import.ruleOperator', { n: i + 1 })} hideLabel>
                      <Select options={['EQUALS', 'IN', 'CONTAINS', 'NOT_EMPTY', 'EMPTY'].map((o) => ({ value: o, label: t(`consignees.import.lineOperators.${o}`) }))} />
                    </Field>
                    <Field name={`lineRules.${i}.value`} label={t('consignees.import.ruleValue', { n: i + 1 })} hideLabel>
                      <TextInput maxLength={200} placeholder={t('consignees.import.defaultValuePlaceholder')} />
                    </Field>
                    <button type="button" className="rowbtn" title={t('consignees.import.removeRuleN', { n: i + 1 })} aria-label={t('consignees.import.removeRuleN', { n: i + 1 })} onClick={() => lineRules.remove(i)}>
                      <IconTrash />
                    </button>
                  </div>
                ))}
                <button type="button" className="btn sm" onClick={() => lineRules.append({ kind: 'DESCRIPTION', position: '', operator: 'EQUALS', value: '' })}>
                  <IconPlus /> {t('consignees.import.addRule')}
                </button>
              </>
            )}
          </>
        )}
      </Form>
    </Modal>
  )
}
