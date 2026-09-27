import { useT } from '../i18n/useT'
import { IconSearch } from './icons'

export interface QBoxProps {
  value: string
  onChange: (value: string) => void
  /** Texto de ayuda (por defecto "Buscar…"). */
  placeholder?: string
  /** Etiqueta accesible (por defecto la del placeholder). */
  label?: string
}

/** Buscador libre dentro de una sección (`.qbox`). Filtra lo ya cargado con `matchesQ`; no llama al API. */
export function QBox({ value, onChange, placeholder, label }: QBoxProps) {
  const t = useT()
  const ph = placeholder ?? t('ui.qbox.placeholder')
  return (
    <label className="qbox">
      <IconSearch />
      <input type="search" value={value} placeholder={ph} aria-label={label ?? ph} onChange={(e) => onChange(e.target.value)} />
    </label>
  )
}
