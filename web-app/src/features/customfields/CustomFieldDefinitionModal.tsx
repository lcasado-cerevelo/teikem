// Alta y edición de una definición de campo personalizado (maqueta `renderCFEditorHtml`). Form + zod espejo del servidor
// (`CustomFieldService`): clave solo en el alta, tipo, etiquetas es/en, lista de valores, valor por defecto, validación amigable
// (nunca JSON a mano), requerido / único / mostrar en listados y orden. Los errores del servidor salen bajo el campo que los causa.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import {
  CUSTOM_FIELD_TYPES,
  CustomFieldDataTypes,
  buildUpsert,
  definitionSchema,
  definitionToValues,
  emptyDefinitionValues,
  hasLegacyOptions,
  isListType,
  legacyOptions,
  mapServerErrors,
  useSaveCustomFieldDefinition,
  validationFieldsFor,
  type CustomFieldDefinitionDto,
  type DefinitionFormValues,
} from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { DateInput, DateTimeInput, Field, NumberInput, Select, TextArea, TextInput, Toggle, toast } from '../../kernel/ui'
import { SearchMultiSelectInput } from '../../kernel/ui/SearchMultiSelectInput'
import { FormModal } from '../clients/contractUi'
import { CustomListPicker } from './CustomListPicker'

interface Props {
  /** Tipo de entidad del campo (en la edición, el de la definición). */
  entityType: string
  entityLabel: string
  /** null = alta. */
  definition: CustomFieldDefinitionDto | null
  onClose: () => void
}

/** Valor por defecto según el tipo: texto, número, fecha, casilla (BOOL) o las opciones de la lista (selección). */
function DefaultControl({ type, options }: { type: string; options: { value: string; label: string }[] }) {
  const t = useT()
  switch (type) {
    case CustomFieldDataTypes.Bool:
      return (
        <Field name="defaultBool" label={t('customFieldsAdmin.form.default')}>
          <Toggle text={t('customFieldsAdmin.form.defaultBool')} />
        </Field>
      )
    case CustomFieldDataTypes.MultiSelect:
      return (
        <Field name="defaultMulti" label={t('customFieldsAdmin.form.default')}>
          <SearchMultiSelectInput options={options} placeholder={t('customFieldsAdmin.form.defaultNone')} />
        </Field>
      )
    case CustomFieldDataTypes.Select:
    case CustomFieldDataTypes.LookupRef:
      return (
        <Field name="defaultText" label={t('customFieldsAdmin.form.default')}>
          <Select options={options} placeholder={t('customFieldsAdmin.form.defaultNone')} />
        </Field>
      )
    case CustomFieldDataTypes.Number:
      return (
        <Field name="defaultText" label={t('customFieldsAdmin.form.default')}>
          <TextInput inputMode="decimal" />
        </Field>
      )
    case CustomFieldDataTypes.Date:
      return (
        <Field name="defaultText" label={t('customFieldsAdmin.form.default')}>
          <DateInput />
        </Field>
      )
    case CustomFieldDataTypes.DateTime:
      return (
        <Field name="defaultText" label={t('customFieldsAdmin.form.default')}>
          <DateTimeInput />
        </Field>
      )
    default:
      return (
        <Field name="defaultText" label={t('customFieldsAdmin.form.default')}>
          <TextInput />
        </Field>
      )
  }
}

/** Validación opcional con controles según el tipo (formato y longitudes; mínimo y máximo; días para fechas). */
function ValidationControls({ type }: { type: string }) {
  const t = useT()
  const names = validationFieldsFor(type)
  if (names.length === 0) return null
  const isDate = type === CustomFieldDataTypes.Date || type === CustomFieldDataTypes.DateTime
  return (
    <fieldset className="cf-validation">
      <legend>{t('customFieldsAdmin.form.validation')}</legend>
      <p className="help">{t('customFieldsAdmin.form.validationHelp')}</p>
      {names.includes('regex') && (
        <Field name="regex" label={t('customFieldsAdmin.form.regex')}>
          <TextInput className="mono" placeholder={t('customFieldsAdmin.form.regexPlaceholder')} spellCheck={false} />
        </Field>
      )}
      {names.includes('minLength') && (
        <div className="r2">
          <Field name="minLength" label={t('customFieldsAdmin.form.minLength')}>
            <NumberInput min={0} step={1} />
          </Field>
          <Field name="maxLength" label={t('customFieldsAdmin.form.maxLength')}>
            <NumberInput min={0} step={1} />
          </Field>
        </div>
      )}
      {names.includes('min') && (
        <div className="r2">
          <Field name="min" label={t(isDate ? 'customFieldsAdmin.form.minDays' : 'customFieldsAdmin.form.min')} help={isDate ? t('customFieldsAdmin.form.daysHelp') : undefined}>
            <NumberInput step={isDate ? 1 : 'any'} />
          </Field>
          <Field name="max" label={t(isDate ? 'customFieldsAdmin.form.maxDays' : 'customFieldsAdmin.form.max')}>
            <NumberInput step={isDate ? 1 : 'any'} />
          </Field>
        </div>
      )}
    </fieldset>
  )
}

export function CustomFieldDefinitionModal({ entityType, entityLabel, definition, onClose }: Props) {
  const t = useT()
  const creating = definition === null
  const save = useSaveCustomFieldDefinition()
  const ownOptions = hasLegacyOptions(definition)
  const schema = useMemo(() => definitionSchema(t, { creating, ownOptions }), [t, creating, ownOptions])
  const initial = useMemo(() => (definition ? definitionToValues(definition) : emptyDefinitionValues()), [definition])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: initial })
  const type = useWatch({ control: form.control, name: 'dataType' })
  const refEntity = useWatch({ control: form.control, name: 'refEntity' })
  const { data: listValues = [] } = useLookups(isListType(type) && refEntity ? refEntity : null)
  const ownList = useMemo(() => legacyOptions(definition).map((o) => ({ value: o.value, label: o.labels?.es ?? o.value })), [definition])

  const typeOptions = useMemo(() => CUSTOM_FIELD_TYPES.map((v) => ({ value: v, label: t(`customFieldsAdmin.types.${v}`) })), [t])
  const defaultOptions = refEntity ? listValues.map((o) => ({ value: o.code, label: o.label })) : ownList
  const list = isListType(type)

  // Al cambiar de lista, el valor por defecto de la anterior ya no aplica.
  const lastList = useRef(initial.refEntity)
  useEffect(() => {
    if (lastList.current === refEntity) return
    lastList.current = refEntity
    form.setValue('defaultText', '')
    form.setValue('defaultMulti', [])
  }, [refEntity, form])

  return (
    <FormModal
      id="custom-field-definition"
      title={t(creating ? 'customFieldsAdmin.modal.newTitle' : 'customFieldsAdmin.modal.editTitle')}
      saveLabel={t('customFieldsAdmin.modal.save')}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: DefinitionFormValues) => {
        const body = buildUpsert(v, definition)
        try {
          const saved = await save.mutateAsync({ id: definition?.id, entityType, body })
          toast.success(t(creating ? 'customFieldsAdmin.toast.created' : 'customFieldsAdmin.toast.updated', { label: saved.label || v.labelEs.trim() }))
          onClose()
        } catch (err) {
          // El servidor nombra `labels`, `options` o `validationJson`: se llevan al control que los causa; un 409 va bajo la clave
          // (alta) o bajo el tipo (edición). Lo que no se pueda ubicar sale arriba como aviso.
          const problem = applyProblemDetails(err)
          const mapped = mapServerErrors(err instanceof ApiError ? err.status : 0, problem.title, problem.errors, creating, v.dataType)
          if (Object.keys(mapped).length > 0) throw new ApiError(err instanceof ApiError ? err.status : 400, { title: problem.title, code: problem.code, errors: mapped })
          throw err
        }
      }}
    >
      <div className="f">
        <label>{t('customFieldsAdmin.form.entity')}</label>
        <div className="mono">{entityLabel}</div>
      </div>
      <div className="r2">
        {creating ? (
          <Field name="fieldKey" label={t('customFieldsAdmin.form.key')} required help={t('customFieldsAdmin.form.keyHelp')}>
            <TextInput className="mono" placeholder={t('customFieldsAdmin.form.keyPlaceholder')} autoComplete="off" spellCheck={false} maxLength={80} />
          </Field>
        ) : (
          <div className="f">
            <label>{t('customFieldsAdmin.form.key')}</label>
            <div className="mono">{definition?.fieldKey}</div>
          </div>
        )}
        <Field name="dataType" label={t('customFieldsAdmin.form.type')} required>
          <Select options={typeOptions} />
        </Field>
      </div>
      <div className="r2">
        <Field name="labelEs" label={t('customFieldsAdmin.form.labelEs')} required>
          <TextInput maxLength={120} />
        </Field>
        <Field name="labelEn" label={t('customFieldsAdmin.form.labelEn')} help={t('customFieldsAdmin.form.labelEnHelp')}>
          <TextInput maxLength={120} />
        </Field>
      </div>
      <Field name="description" label={t('customFieldsAdmin.form.description')}>
        <TextArea rows={2} maxLength={300} />
      </Field>
      {list && <CustomListPicker ownOptionsText={ownOptions ? { n: ownList.length, items: ownList.map((o) => o.label).join(', ') } : null} />}
      <ValidationControls type={type} />
      {/* un campo de lista sin lista elegida todavía no tiene opciones entre las cuales escoger */}
      {(!list || refEntity !== '' || ownList.length > 0) && <DefaultControl type={type} options={defaultOptions} />}
      <div className="r2 cf-toggles">
        <Field name="isRequired" label={t('customFieldsAdmin.form.required')}>
          <Toggle />
        </Field>
        <Field name="isUnique" label={t('customFieldsAdmin.form.unique')}>
          <Toggle />
        </Field>
      </div>
      <div className="r2 cf-toggles">
        <Field name="showInList" label={t('customFieldsAdmin.form.showInList')}>
          <Toggle />
        </Field>
        <Field name="sortOrder" label={t('customFieldsAdmin.form.order')} help={t('customFieldsAdmin.form.orderHelp')}>
          <NumberInput min={0} step={1} />
        </Field>
      </div>
    </FormModal>
  )
}
