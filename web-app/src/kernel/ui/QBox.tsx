import { useRef } from 'react'
import { useT } from '../i18n/useT'
import { textFilterValue } from './filterRegistry'
import { useRegisterFilter } from './filterScopeContext'
import { IconSearch } from './icons'

export interface QBoxProps {
  value: string
  onChange: (value: string) => void
  /** Texto de ayuda (por defecto "Buscar…"). */
  placeholder?: string
  /** Etiqueta accesible (por defecto la del placeholder). */
  label?: string
}

/** Buscador libre dentro de una sección (`.qbox`). Filtra lo ya cargado con `matchesQ`; no llama al API. Con texto se
 *  anota en el ámbito (`FilterScope`) como 'Buscar "texto"' (línea de filtros de las exportaciones). */
export function QBox({ value, onChange, placeholder, label }: QBoxProps) {
  const t = useT()
  const ph = placeholder ?? t('ui.qbox.placeholder')
  const ref = useRef<HTMLLabelElement>(null)
  useRegisterFilter(t('ui.filters.applied.search'), textFilterValue(value), ref)
  return (
    <label className="qbox" ref={ref}>
      <IconSearch />
      <input type="search" value={value} placeholder={ph} aria-label={label ?? ph} onChange={(e) => onChange(e.target.value)} />
    </label>
  )
}
