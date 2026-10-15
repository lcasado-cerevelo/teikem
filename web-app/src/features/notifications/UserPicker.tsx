// Buscador de personas (y de roles) con selección (Lotes 52–54): se escribe parte del nombre, del correo o del rol y se listan las coincidencias
// con su NOMBRE y su CORREO (los roles, con cuántas personas tienen); se escoge una con el ratón o con las flechas y Enter. Con `onPickAll` aparece
// arriba «Seleccionar todo (N)»: escoge TODAS las que coinciden con lo escrito (no solo las que caben en la lista), nada más. Lo usan el modal de
// reglas, «Nuevo mensaje» y «Enviar avisos». Solo lista; no carga nada por sí mismo (recibe las personas).
import { useId, useMemo, useRef, useState } from 'react'
import { useT } from '../../kernel/i18n'
import { matchesQ } from '../../kernel/ui'

export interface PersonOption {
  id: number
  name: string
  email: string
  /** Si viene, se pinta el punto de «en línea» (mensajería). */
  online?: boolean
}

export const MAX_SUGGESTIONS = 8

/** Todas las que coinciden con lo escrito (nombre o correo, sin importar mayúsculas ni tildes) y no están excluidas. */
export function matchingPeople(people: readonly PersonOption[], q: string, exclude: ReadonlySet<number> = new Set()): PersonOption[] {
  return people.filter((p) => !exclude.has(p.id) && matchesQ(q, p.name, p.email))
}

/** Las que se muestran en la lista (con tope); `matchingPeople` es lo que «Seleccionar todo» escoge. */
export function filterPeople(people: readonly PersonOption[], q: string, exclude: ReadonlySet<number> = new Set()): PersonOption[] {
  return matchingPeople(people, q, exclude).slice(0, MAX_SUGGESTIONS)
}

export function UserPicker({
  people,
  onPick,
  onPickAll,
  exclude,
  placeholder,
  label,
  autoFocus,
  onQuery,
}: {
  people: readonly PersonOption[]
  onPick: (p: PersonOption) => void
  /** Si viene, aparece «Seleccionar todo (N)» y escoge de un golpe todas las que coinciden con lo escrito. */
  onPickAll?: (all: PersonOption[]) => void
  exclude?: ReadonlySet<number>
  placeholder: string
  label: string
  autoFocus?: boolean
  /** Avisa cada vez que cambia lo escrito (quien carga las personas del servidor pide con ese texto). */
  onQuery?: (q: string) => void
}) {
  const t = useT()
  const id = useId()
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const input = useRef<HTMLInputElement>(null)
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const cancelClose = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current)
    closeTimer.current = null
  }
  const all = useMemo(() => matchingPeople(people, q, exclude), [people, q, exclude])
  const list = useMemo(() => all.slice(0, MAX_SUGGESTIONS), [all])
  const hasAll = Boolean(onPickAll) && all.length > 0
  const rows = hasAll ? ['all' as const, ...list] : list   // la fila 0 es «Seleccionar todo»

  const reset = () => {
    setQ('')
    onQuery?.('')
    setOpen(false)
    setActive(0)
    input.current?.focus()
  }
  const choose = (row: (typeof rows)[number] | undefined) => {
    if (!row) return
    if (row === 'all') onPickAll?.(all)
    else onPick(row)
    reset()
  }
  const rowId = (row: (typeof rows)[number]) => (row === 'all' ? `${id}-all` : `${id}-o${row.id}`)

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
        aria-activedescendant={open && rows[active] ? rowId(rows[active]) : undefined}
        autoComplete="off"
        autoFocus={autoFocus}
        placeholder={placeholder}
        value={q}
        onChange={(e) => {
          cancelClose()
          setQ(e.target.value)
          onQuery?.(e.target.value)
          setOpen(true)
          setActive(0)
        }}
        onFocus={() => {
          cancelClose()
          setOpen(true)
        }}
        onBlur={() => {
          cancelClose()
          closeTimer.current = setTimeout(() => setOpen(false), 120)   // deja terminar el clic en una opción; un foco nuevo lo cancela
        }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') {
            e.preventDefault()
            setOpen(true)
            setActive((a) => Math.min(a + 1, Math.max(0, rows.length - 1)))
          } else if (e.key === 'ArrowUp') {
            e.preventDefault()
            setActive((a) => Math.max(a - 1, 0))
          } else if (e.key === 'Enter' && open && rows[active]) {
            e.preventDefault()
            choose(rows[active])
          } else if (e.key === 'Escape' && open) {
            e.stopPropagation()
            setOpen(false)
          }
        }}
      />
      {open && (
        <ul id={`${id}-list`} role="listbox" className="upick-list" aria-label={label}>
          {rows.length === 0 ? (
            <li className="upick-empty" role="presentation">
              {t('notifications.picker.none')}
            </li>
          ) : (
            rows.map((row, i) => (
              <li
                key={row === 'all' ? 'all' : row.id}
                id={rowId(row)}
                role="option"
                aria-selected={i === active}
                className={`${i === active ? 'on' : ''}${row === 'all' ? ' upick-all' : ''}`.trim() || undefined}
                onMouseDown={(e) => {
                  e.preventDefault()
                  choose(row)
                }}
                onMouseEnter={() => setActive(i)}
              >
                {row === 'all' ? (
                  <b>{t('notifications.picker.selectAll', { n: all.length })}</b>
                ) : (
                  <>
                    <b>
                      {row.online !== undefined && <i className={`upick-dot${row.online ? ' on' : ''}`} aria-hidden="true" />}
                      {row.name}
                    </b>
                    <span>{row.email}</span>
                  </>
                )}
              </li>
            ))
          )}
          {all.length > list.length && (
            <li className="upick-more" role="presentation">
              {t('notifications.picker.more', { n: all.length - list.length })}
            </li>
          )}
        </ul>
      )}
    </div>
  )
}
