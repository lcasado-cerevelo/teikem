// Lote 1 (cambios de Almacén) — controles sueltos para la fila `Filters` de las pantallas de almacenes (lista y ficha):
// texto libre con etiqueta (`TextFilter`) e interruptor booleano (`ToggleFilter`, p. ej. "Incluir inactivas"). Con valor
// se anotan en el ámbito de filtros del kit (`useRegisterFilter`: línea "Filtros: …" de las exportaciones).
import { useId, useRef } from 'react'
import { textFilterValue, useRegisterFilter } from '../../kernel/ui'

/** Filtro de texto (`.f` con etiqueta). El valor se aplica tal cual; si va al API, la pantalla lo pasa por `useDebounced`. */
export function TextFilter({
  label,
  value,
  onChange,
  placeholder,
  maxLength,
  type = 'search',
}: {
  label: string
  value: string
  onChange: (v: string) => void
  placeholder?: string
  maxLength?: number
  /** 'search' (por defecto) o 'text'. */
  type?: 'search' | 'text'
}) {
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  useRegisterFilter(label, textFilterValue(value), ref)
  return (
    <div className="f" ref={ref}>
      <label htmlFor={id}>{label}</label>
      <input id={id} type={type} value={value} placeholder={placeholder} maxLength={maxLength} autoComplete="off" onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

/** Interruptor simple (fuera de un `Form`), para filtros booleanos como `includeInactive`/`onlyWithStock`. Encendido se
 *  anota solo con su etiqueta ("Solo manuales"). */
export function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  const ref = useRef<HTMLDivElement>(null)
  useRegisterFilter(label, checked ? '' : null, ref)
  return (
    <div className="f" ref={ref}>
      <label className="sw">
        <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
    </div>
  )
}
