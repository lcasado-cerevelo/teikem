// Buscador de personas (Lote 52): se escribe parte del nombre o del correo y se listan las personas que coinciden con su NOMBRE y su
// CORREO; se escoge una con el ratón o con las flechas y Enter. Lo usa el modal de reglas para el destinatario «Usuario» y lo reutilizará
// «Enviar un mensaje a…» de Kemi. Solo lista; no carga nada por sí mismo (recibe las personas).
import { useId, useMemo, useRef, useState } from 'react'
import { useT } from '../../kernel/i18n'
import { matchesQ } from '../../kernel/ui'

export interface PersonOption {
  id: number
  name: string
  email: string
}

export const MAX_SUGGESTIONS = 8

/** Las personas que coinciden con lo escrito (nombre o correo, sin importar mayúsculas ni tildes), con tope. */
export function filterPeople(people: readonly PersonOption[], q: string, exclude: ReadonlySet<number> = new Set()): PersonOption[] {
  return people.filter((p) => !exclude.has(p.id) && matchesQ(q, p.name, p.email)).slice(0, MAX_SUGGESTIONS)
}

export function UserPicker({
  people,
  onPick,
  exclude,
  placeholder,
  label,
  autoFocus,
}: {
  people: readonly PersonOption[]
  onPick: (p: PersonOption) => void
  exclude?: ReadonlySet<number>
  placeholder: string
  label: string
  autoFocus?: boolean
}) {
  const t = useT()
  const id = useId()
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const input = useRef<HTMLInputElement>(null)
  const list = useMemo(() => filterPeople(people, q, exclude), [people, q, exclude])

  const pick = (p: PersonOption) => {
    onPick(p)
    setQ('')
    setOpen(false)
    setActive(0)
    input.current?.focus()
  }

  return (
    <div className="upick">
      <input
        ref={input}
        id={id}
        role="combobox"
        aria-label={label}
        aria-expanded={open}
        aria-controls={`${id}-list`}
        aria-autocomplete="list"
        aria-activedescendant={open && list[active] ? `${id}-o${list[active].id}` : undefined}
        autoComplete="off"
        autoFocus={autoFocus}
        placeholder={placeholder}
        value={q}
        onChange={(e) => {
          setQ(e.target.value)
          setOpen(true)
          setActive(0)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 120)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') {
            e.preventDefault()
            setOpen(true)
            setActive((a) => Math.min(a + 1, Math.max(0, list.length - 1)))
          } else if (e.key === 'ArrowUp') {
            e.preventDefault()
            setActive((a) => Math.max(a - 1, 0))
          } else if (e.key === 'Enter' && open && list[active]) {
            e.preventDefault()
            pick(list[active])
          } else if (e.key === 'Escape' && open) {
            e.stopPropagation()
            setOpen(false)
          }
        }}
      />
      {open && (
        <ul id={`${id}-list`} role="listbox" className="upick-list" aria-label={label}>
          {list.length === 0 ? (
            <li className="upick-empty" role="presentation">
              {t('notifications.picker.none')}
            </li>
          ) : (
            list.map((p, i) => (
              <li
                key={p.id}
                id={`${id}-o${p.id}`}
                role="option"
                aria-selected={i === active}
                className={i === active ? 'on' : undefined}
                onMouseDown={(e) => {
                  e.preventDefault()
                  pick(p)
                }}
                onMouseEnter={() => setActive(i)}
              >
                <b>{p.name}</b>
                <span>{p.email}</span>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  )
}
