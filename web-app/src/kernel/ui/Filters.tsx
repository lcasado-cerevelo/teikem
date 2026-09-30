import { useId, useRef, type ReactNode } from 'react'
import { useLang, useT } from '../i18n/useT'
import type { DateRange } from './dateRange'
import { dateRangeFilterText, joinFilterValues } from './filterRegistry'
import { useRegisterFilter } from './filterScopeContext'
import './ui.css'

export interface FilterOption {
  value: string
  label: string
}

export interface FiltersProps {
  children: ReactNode
  /** Botón "Limpiar" al final de la fila (solo si se pasa). */
  onClear?: () => void
  /** Etiqueta accesible del grupo (por defecto "Filtros"). */
  label?: string
}

/**
 * Fila de filtros estructurados (`.filters`, flex que envuelve): cada filtro parte de 140 px y crece hasta repartirse el
 * ancho del panel, sin columnas vacías; uno por renglón a 480 px o menos. "Limpiar" usa solo el ancho de su contenido.
 */
export function Filters({ children, onClear, label }: FiltersProps) {
  const t = useT()
  return (
    <div className="filters" role="group" aria-label={label ?? t('ui.filters.label')}>
      {children}
      {onClear && (
        // .filters-clear: ocupa el ancho de su contenido, no el de la columna del grid (maestro, convenciones de interfaz)
        <button type="button" className="btn sm filters-clear" onClick={onClear}>
          {t('ui.filters.clear')}
        </button>
      )}
    </div>
  )
}

export interface SelectFilterProps {
  label: string
  value: string
  onChange: (value: string) => void
  options: readonly FilterOption[]
  /** Texto de la opción vacía ('' = sin filtro). Por defecto "Todos". `null` = sin opción vacía. */
  allLabel?: string | null
}

/** Filtro de selección única. `value === ''` significa "todos". Con valor, se anota en el ámbito (`FilterScope`) con la
 *  etiqueta de la opción (línea de filtros de las exportaciones). */
export function SelectFilter({ label, value, onChange, options, allLabel }: SelectFilterProps) {
  const t = useT()
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  useRegisterFilter(label, value === '' ? null : joinFilterValues([options.find((o) => o.value === value)?.label ?? value], t), ref)
  return (
    <div className="f" ref={ref}>
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
        {allLabel !== null && <option value="">{allLabel ?? t('ui.filters.all')}</option>}
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  )
}

export type { DateRange } from './dateRange'

export interface DateRangeFilterProps {
  label: string
  value: DateRange
  onChange: (value: DateRange) => void
}

/** Rango de fechas (desde/hasta, inclusivo). Ocupa el doble que un filtro normal (`.span2`; todo el renglón a 480 px). */
export function DateRangeFilter({ label, value, onChange }: DateRangeFilterProps) {
  const t = useT()
  const lang = useLang()
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  // en el ámbito: "del 01/09/2026 al 30/09/2026" / "desde …" / "hasta …"
  useRegisterFilter(label, dateRangeFilterText(value, lang, t), ref)
  return (
    <div className="f span2" role="group" aria-labelledby={`${id}-l`} ref={ref}>
      <label id={`${id}-l`} htmlFor={`${id}-from`}>
        {label}
      </label>
      <div className="drange">
        <input
          id={`${id}-from`}
          type="date"
          aria-label={t('ui.filters.from')}
          value={value.from}
          max={value.to || undefined}
          onChange={(e) => onChange({ ...value, from: e.target.value })}
        />
        <input
          type="date"
          aria-label={t('ui.filters.to')}
          value={value.to}
          min={value.from || undefined}
          onChange={(e) => onChange({ ...value, to: e.target.value })}
        />
      </div>
    </div>
  )
}
