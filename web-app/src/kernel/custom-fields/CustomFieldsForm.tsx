import { useEffect, useId, useMemo } from 'react'
import { Controller, type FieldValues, type Path, type PathValue, type UseFormReturn } from 'react-hook-form'
import { applyProblemDetails } from '../api/problem'
import { useLookups } from '../catalogs/api'
import { useT } from '../i18n'
import { SearchMultiSelect } from '../ui/SearchSelect'
import { CUSTOM_FIELDS_NAME, useCustomFieldDefinitions, useCustomFieldValues, useCustomFieldsEnabled } from './api'
import {
  CustomFieldDataTypes,
  activeOptions,
  dataTypeOf,
  issueMessageKey,
  toFormValue,
  validateCustomField,
  type CustomFieldDefinitionDto,
  type CustomFieldFormValue,
} from './values'

export interface CustomFieldsFormProps<T extends FieldValues> {
  /** Código EntityType dueño de los campos (ej. `CLIENT`). */
  entityType: string
  /** Id del registro; sin id (alta) se muestran los valores por defecto de cada definición. */
  entityId?: number | null
  /** Formulario (react-hook-form) de la pantalla: los campos viven en `<name>.<fieldKey>`. */
  form: UseFormReturn<T>
  /** Objeto del formulario donde se guardan (por defecto `customFields`). */
  name?: string
  disabled?: boolean
}

/**
 * Pinta los campos personalizados activos del tipo de entidad dentro del formulario de la pantalla, según su tipo
 * (texto, número, fecha, fecha y hora, sí/no, opción, varias opciones, catálogo). Valida en cliente obligatorio, tipo y
 * ValidationJson; se guardan con `useSaveCustomFields(entityType).save(entityId, form)`. Con el módulo CUSTOM_FIELDS
 * apagado no consulta ni pinta nada.
 */
export function CustomFieldsForm<T extends FieldValues>({
  entityType,
  entityId,
  form,
  name = CUSTOM_FIELDS_NAME,
  disabled,
}: CustomFieldsFormProps<T>) {
  const t = useT()
  const on = useCustomFieldsEnabled()
  const definitions = useCustomFieldDefinitions(entityType)
  const values = useCustomFieldValues(entityType, entityId)
  const hasRecord = typeof entityId === 'number' && entityId > 0

  // Valor inicial de cada campo: el guardado (o su default, que el API ya aplica) o el DefaultValue en el alta.
  const initial = useMemo(() => {
    const map = new Map<string, CustomFieldFormValue>()
    for (const def of definitions.data ?? []) {
      const key = def.fieldKey ?? ''
      const saved = values.data?.find((v) => v.fieldKey?.toLowerCase() === key.toLowerCase())
      map.set(key, toFormValue(dataTypeOf(def), hasRecord ? saved?.value : def.defaultValue))
    }
    return map
  }, [definitions.data, values.data, hasRecord])

  const ready = !!definitions.data && (!hasRecord || !!values.data)

  useEffect(() => {
    if (!ready) return
    for (const [key, value] of initial) {
      const path = `${name}.${key}` as Path<T>
      if (!form.getFieldState(path).isDirty) form.resetField(path, { defaultValue: value as PathValue<T, Path<T>> })
    }
  }, [ready, initial, name, form])

  // Módulo CUSTOM_FIELDS apagado: no hay campos personalizados (ni spinner ni aviso).
  if (!on) return null
  if (definitions.isPending || (hasRecord && values.isPending)) {
    return <div className="spin" role="status" aria-label={t('common.loading')} />
  }
  if (definitions.isError) return <p className="note">{applyProblemDetails(definitions.error).title}</p>
  if (values.isError) return <p className="note">{applyProblemDetails(values.error).title}</p>
  if (!definitions.data || definitions.data.length === 0) return null

  return (
    <div className="r2">
      {definitions.data.map((def) => (
        <CustomFieldInput
          key={def.fieldKey}
          def={def}
          form={form}
          path={`${name}.${def.fieldKey ?? ''}` as Path<T>}
          defaultValue={initial.get(def.fieldKey ?? '') ?? null}
          disabled={disabled}
        />
      ))}
    </div>
  )
}

interface CustomFieldInputProps<T extends FieldValues> {
  def: CustomFieldDefinitionDto
  form: UseFormReturn<T>
  path: Path<T>
  defaultValue: CustomFieldFormValue
  disabled?: boolean
}

function CustomFieldInput<T extends FieldValues>({ def, form, path, defaultValue, disabled }: CustomFieldInputProps<T>) {
  const t = useT()
  const id = useId()
  const type = dataTypeOf(def)
  const usesCatalog =
    type === CustomFieldDataTypes.LookupRef ||
    ((type === CustomFieldDataTypes.Select || type === CustomFieldDataTypes.MultiSelect) && !!def.refEntity)
  const lookups = useLookups(usesCatalog ? def.refEntity : null)
  const options = usesCatalog ? (lookups.data ?? []).map((o) => ({ code: o.code, label: o.label })) : activeOptions(def)
  const label = def.label || def.fieldKey || ''
  const helpId = `${id}-help`
  const errId = `${id}-err`

  return (
    <Controller
      control={form.control}
      name={path}
      defaultValue={defaultValue as PathValue<T, Path<T>>}
      disabled={disabled}
      rules={{
        validate: (value: unknown) => {
          const issues = validateCustomField(def, value as CustomFieldFormValue)
          if (issues.length === 0) return true
          const { key, params } = issueMessageKey(issues[0])
          return t(key, params)
        },
      }}
      render={({ field, fieldState }) => {
        const value = field.value as CustomFieldFormValue | undefined
        const invalid = fieldState.invalid || undefined
        const describedBy = [def.description ? helpId : null, fieldState.error ? errId : null].filter(Boolean).join(' ') || undefined
        const common = { id, name: field.name, ref: field.ref, onBlur: field.onBlur, disabled: field.disabled, 'aria-invalid': invalid, 'aria-describedby': describedBy }
        let control
        switch (type) {
          case CustomFieldDataTypes.Bool:
            control = (
              <label className="sw" htmlFor={id}>
                <input type="checkbox" {...common} checked={value === true} onChange={(e) => field.onChange(e.target.checked)} />
                <span className="tk" />
                {value === true ? t('customFields.yes') : t('customFields.no')}
              </label>
            )
            break
          case CustomFieldDataTypes.MultiSelect: {
            // Selección múltiple con buscador (maestro: nunca la lista cruda de casillas). Los códigos guardados se
            // comparan sin distinguir mayúsculas; uno que ya no está en la lista se conserva como opción propia.
            const saved = Array.isArray(value) ? value : []
            const canonical = (code: string) => options.find((o) => o.code.toLowerCase() === code.toLowerCase())?.code ?? code
            const selected = saved.map(canonical)
            const known = new Set(options.map((o) => o.code))
            const choices = [
              ...options.map((o) => ({ value: o.code, label: o.label })),
              ...selected.filter((c) => !known.has(c)).map((c) => ({ value: c, label: c })),
            ]
            control = (
              <SearchMultiSelect
                id={id}
                labelledBy={`${id}-lbl`}
                buttonRef={field.ref}
                options={choices}
                value={selected}
                onChange={(next) => field.onChange(next)}
                onBlur={field.onBlur}
                placeholder={t('customFields.choose')}
                disabled={field.disabled}
                invalid={invalid}
                describedBy={describedBy}
              />
            )
            break
          }
          case CustomFieldDataTypes.Select:
          case CustomFieldDataTypes.LookupRef: {
            const current = typeof value === 'string' ? value : ''
            const known = current === '' || options.some((o) => o.code.toLowerCase() === current.toLowerCase())
            control = (
              <select {...common} value={current} onChange={(e) => field.onChange(e.target.value)}>
                <option value="">{t('customFields.choose')}</option>
                {!known && <option value={current}>{current}</option>}
                {options.map((o) => (
                  <option key={o.code} value={o.code}>
                    {o.label}
                  </option>
                ))}
              </select>
            )
            break
          }
          default: {
            const inputType =
              type === CustomFieldDataTypes.Date ? 'date' : type === CustomFieldDataTypes.DateTime ? 'datetime-local' : 'text'
            control = (
              <input
                {...common}
                type={inputType}
                inputMode={type === CustomFieldDataTypes.Number ? 'decimal' : undefined}
                value={typeof value === 'string' ? value : ''}
                onChange={(e) => field.onChange(e.target.value)}
              />
            )
          }
        }
        return (
          <div className="f">
            {type === CustomFieldDataTypes.MultiSelect ? (
              <label id={`${id}-lbl`} htmlFor={id}>
                {label}
                {def.isRequired && ' *'}
              </label>
            ) : (
              <label htmlFor={id}>
                {label}
                {def.isRequired && ' *'}
              </label>
            )}
            {control}
            {def.description && (
              <div className="help" id={helpId}>
                {def.description}
              </div>
            )}
            {fieldState.error?.message && (
              <p className="ferr" id={errId}>
                {fieldState.error.message}
              </p>
            )}
          </div>
        )
      }}
    />
  )
}
