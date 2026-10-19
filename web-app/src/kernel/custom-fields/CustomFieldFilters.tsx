// Lote 130 (dictado de Luis, 2026-10-18): los campos personalizados con «Mostrar en la búsqueda» ofrecen un filtro, del tipo del campo, en la lista de su entidad.
// El valor vive en un mapa plano con las mismas claves de la dirección del API: `cf.clave` (texto, lista, sí/no) o `cf.clave.from` / `cf.clave.to` (número y fecha).
import { useMemo } from 'react'
import { useLookups } from '../catalogs/api'
import { useT } from '../i18n/useT'
import { DateRangeFilter, Filters, SelectFilter } from '../ui'
import { TextFilter } from '../../features/warehouse/filterControls'
import { useCustomFieldDefinitions } from './api'
import { CustomFieldDataTypes, activeOptions, dataTypeOf, type CustomFieldDefinitionDto } from './values'

/** Valores de los filtros: clave de la dirección (`cf.division`, `cf.peso.from`…) → texto ('' = sin filtro). */
export type CustomFieldFilterValues = Record<string, string>

export const EMPTY_CUSTOM_FIELD_FILTERS: CustomFieldFilterValues = {}

/** Solo los filtros con valor, listos para mezclar en la consulta del API. */
export function customFieldQuery(values: CustomFieldFilterValues): CustomFieldFilterValues {
  return Object.fromEntries(Object.entries(values).filter(([k, v]) => k.startsWith('cf.') && v.trim() !== ''))
}

/** Los campos que se ofrecen como filtro: activos y con «Mostrar en la búsqueda». */
export function searchableDefinitions(defs: readonly CustomFieldDefinitionDto[] | undefined): CustomFieldDefinitionDto[] {
  return (defs ?? []).filter((d) => d.showInList && d.isActive !== false && !!d.fieldKey)
}

/** Mezcla los filtros con valor en la consulta del API (el tipado generado no conoce las claves `cf.*`). */
export function withCustomFields<T extends object>(query: T, values: CustomFieldFilterValues): T {
  return { ...query, ...customFieldQuery(values) } as T
}

export interface CustomFieldFiltersProps {
  /** Código del EntityType (p. ej. `TRANSPORT_ORDER`, `CLIENT`). */
  entityType: string
  value: CustomFieldFilterValues
  onChange: (value: CustomFieldFilterValues) => void
}

/** Los filtros de los campos personalizados de la entidad; va dentro de `<Filters>` junto a los demás. Sin campos habilitados no pinta nada. */
export function CustomFieldFilters({ entityType, value, onChange }: CustomFieldFiltersProps) {
  const defs = useCustomFieldDefinitions(entityType)
  const searchable = useMemo(() => searchableDefinitions(defs.data), [defs.data])
  const set = (patch: CustomFieldFilterValues) => onChange({ ...value, ...patch })
  return (
    <>
      {searchable.map((d) => (
        <CustomFieldFilter key={d.fieldKey} def={d} value={value} set={set} />
      ))}
    </>
  )
}

/** Fila de filtros propia (con «Limpiar») para las listas que no tienen una fila `Filters`; no pinta nada si la entidad no tiene campos habilitados para la búsqueda. */
export function CustomFieldFilterBar({ entityType, value, onChange }: CustomFieldFiltersProps) {
  const defs = useCustomFieldDefinitions(entityType)
  if (searchableDefinitions(defs.data).length === 0) return null
  return (
    <Filters onClear={() => onChange({})}>
      <CustomFieldFilters entityType={entityType} value={value} onChange={onChange} />
    </Filters>
  )
}

function CustomFieldFilter({ def, value, set }: { def: CustomFieldDefinitionDto; value: CustomFieldFilterValues; set: (patch: CustomFieldFilterValues) => void }) {
  const t = useT()
  const type = dataTypeOf(def)
  const key = `cf.${(def.fieldKey ?? '').toLowerCase()}`
  const label = def.label || def.fieldKey || ''
  const usesCatalog =
    type === CustomFieldDataTypes.LookupRef || ((type === CustomFieldDataTypes.Select || type === CustomFieldDataTypes.MultiSelect) && !!def.refEntity)
  const lookups = useLookups(usesCatalog ? def.refEntity : null)
  const options = useMemo(
    () => (usesCatalog ? (lookups.data ?? []).map((o) => ({ value: o.code, label: o.label })) : activeOptions(def).map((o) => ({ value: o.code, label: o.label }))),
    [usesCatalog, lookups.data, def],
  )

  switch (type) {
    case CustomFieldDataTypes.Number:
      return (
        <>
          <TextFilter label={t('customFields.filter.from', { label })} value={value[`${key}.from`] ?? ''} onChange={(v) => set({ [`${key}.from`]: v })} type="text" />
          <TextFilter label={t('customFields.filter.to', { label })} value={value[`${key}.to`] ?? ''} onChange={(v) => set({ [`${key}.to`]: v })} type="text" />
        </>
      )
    case CustomFieldDataTypes.Date:
    case CustomFieldDataTypes.DateTime:
      return (
        <DateRangeFilter
          label={label}
          mode="both"
          value={{ from: value[`${key}.from`] ?? '', to: value[`${key}.to`] ?? '' }}
          onChange={(r) => set({ [`${key}.from`]: r.from, [`${key}.to`]: r.to })}
        />
      )
    case CustomFieldDataTypes.Bool:
      return (
        <SelectFilter
          label={label}
          value={value[key] ?? ''}
          onChange={(v) => set({ [key]: v })}
          options={[
            { value: 'true', label: t('customFields.yes') },
            { value: 'false', label: t('customFields.no') },
          ]}
        />
      )
    case CustomFieldDataTypes.Select:
    case CustomFieldDataTypes.MultiSelect:
    case CustomFieldDataTypes.LookupRef:
      return <SelectFilter label={label} value={value[key] ?? ''} onChange={(v) => set({ [key]: v })} options={options} />
    default:
      return <TextFilter label={label} value={value[key] ?? ''} onChange={(v) => set({ [key]: v })} />
  }
}
