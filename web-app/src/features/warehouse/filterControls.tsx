// Lote 1 (cambios de Almacén) — controles sueltos para la fila `Filters` de las pantallas de almacenes (lista y ficha):
// texto libre con etiqueta (`TextFilter`) e interruptor booleano (`ToggleFilter`, p. ej. "Incluir inactivas").
import { useId } from 'react'

/** Filtro de texto (`.f` con etiqueta). El valor se aplica tal cual; si va al API, la pantalla lo pasa por `useDebounced`. */
export function TextFilter({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (v: string) => void; placeholder?: string }) {
  const id = useId()
  return (
    <div className="f">
      <label htmlFor={id}>{label}</label>
      <input id={id} type="search" value={value} placeholder={placeholder} autoComplete="off" onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

/** Interruptor simple (fuera de un `Form`), para filtros booleanos como `includeInactive`/`onlyWithStock`. */
export function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="f">
      <label className="sw">
        <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
    </div>
  )
}
