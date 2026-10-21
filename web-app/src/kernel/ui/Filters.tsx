import { useId, useRef, type ReactNode } from 'react'
import { useT } from '../i18n/useT'
import { joinFilterValues } from './filterRegistry'
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
  /** Clase extra de la fila (p. ej. `balanced`: cuadrícula pareja de columnas iguales). */
  className?: string
}

/**
 * Fila de filtros estructurados (`.filters`, flex que envuelve): cada filtro parte de 140 px y crece hasta repartirse el
 * ancho del panel, sin columnas vacías; uno por renglón a 480 px o menos. "Limpiar" usa solo el ancho de su contenido.
 */
export function Filters({ children, onClear, label, className }: FiltersProps) {
  const t = useT()
  return (
    <div className={className ? `filters ${className}` : 'filters'} role="group" aria-label={label ?? t('ui.filters.label')}>
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

// El filtro de fecha (desplegable de rangos + campos «Personalizado») vive en su propio archivo (2026-10-12); se re-exporta aquí para
// que quien lo importe de './Filters' siga igual.
export { DateRangeFilter } from './DateRangeFilter'
export type { DateRangeFilterProps } from './DateRangeFilter'
