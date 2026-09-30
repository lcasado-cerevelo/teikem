import { useCallback, useId, useMemo, useRef, useState, type Ref } from 'react'
import { useT } from '../i18n/useT'
import type { FilterOption } from './Filters'
import { IconChevronDown, IconSearch } from './icons'
import { matchesQ } from './matchesQ'
import { useDismiss } from './useDismiss'
import './ui.css'

export interface SearchSelectProps {
  label: string
  options: readonly FilterOption[]
  /** Valores elegidos; vacío = todos (sin filtro). */
  value: readonly string[]
  onChange: (value: string[]) => void
  /** Texto del botón cuando no hay nada elegido (por defecto "Todos"). */
  placeholder?: string
}

/** Selección múltiple con buscador (`.msel`), para filtros con muchas opciones (clientes, bodegas, estatus). */
export function SearchSelect({ label, options, value, onChange, placeholder }: SearchSelectProps) {
  const id = useId()
  return (
    <div className="f">
      <label id={`${id}-l`} htmlFor={`${id}-b`}>
        {label}
      </label>
      <SearchMultiSelect
        id={`${id}-b`}
        labelledBy={`${id}-l`}
        options={options}
        value={value}
        onChange={onChange}
        placeholder={placeholder}
      />
    </div>
  )
}

export interface SearchMultiSelectProps {
  options: readonly FilterOption[]
  value: readonly string[]
  onChange: (value: string[]) => void
  /** Texto del botón sin nada elegido (por defecto "Todos"). */
  placeholder?: string
  /** id del botón (para `<label htmlFor>`). */
  id?: string
  /** id de la etiqueta visible (nombre accesible de la lista). */
  labelledBy?: string
  disabled?: boolean
  invalid?: boolean
  describedBy?: string
  onBlur?: () => void
  /** Ref del botón (p. ej. `field.ref` de react-hook-form para enfocar el campo con error). */
  buttonRef?: Ref<HTMLButtonElement>
}

/**
 * Control `.msel` de selección múltiple con buscador, sin etiqueta propia: para usarlo dentro de un `.f` que ya
 * tiene su `<label>` (formularios, campos personalizados MULTISELECT). `SearchSelect` es este control con etiqueta.
 */
export function SearchMultiSelect({
  options,
  value,
  onChange,
  placeholder,
  id,
  labelledBy,
  disabled,
  invalid,
  describedBy,
  onBlur,
  buttonRef,
}: SearchMultiSelectProps) {
  const t = useT()
  const ref = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const close = useCallback(() => {
    setOpen(false)
    onBlur?.()
  }, [onBlur])
  useDismiss(ref, open, close)

  const selected = useMemo(() => new Set(value), [value])
  const shown = options.filter((o) => matchesQ(q, o.label, o.value))
  const summary =
    value.length === 0
      ? (placeholder ?? t('ui.filters.all'))
      : value.length === 1
        ? (options.find((o) => o.value === value[0])?.label ?? value[0])
        : t('ui.searchSelect.selected', { count: value.length })

  const toggle = (v: string) => {
    const next = selected.has(v) ? value.filter((x) => x !== v) : [...value, v]
    onChange(next)
  }

  return (
    <div ref={ref} className={open ? 'msel open' : 'msel'}>
      <button
        id={id}
        ref={buttonRef}
        type="button"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        disabled={disabled}
        onClick={() => setOpen((o) => !o)}
      >
        {/* la flecha va a la izquierda, antes del resumen (igual que los <select> nativos del kit, ver base.css) */}
        <IconChevronDown />
        <span className="sum">{summary}</span>
        {value.length > 0 && <span className="cnt">{value.length}</span>}
      </button>
      {open && !disabled && (
        <div className="mp">
          <div className="msearch">
            <IconSearch />
            <input
              type="search"
              autoFocus
              value={q}
              placeholder={t('ui.qbox.placeholder')}
              aria-label={t('ui.qbox.placeholder')}
              onChange={(e) => setQ(e.target.value)}
            />
          </div>
          <div className="milist" role="listbox" aria-multiselectable="true" aria-labelledby={labelledBy}>
            {shown.length === 0 && <div className="mnone">{t('ui.searchSelect.none')}</div>}
            {shown.map((o) => (
              <label key={o.value} className="mi" role="option" aria-selected={selected.has(o.value)}>
                <input type="checkbox" checked={selected.has(o.value)} onChange={() => toggle(o.value)} />
                <span>{o.label}</span>
              </label>
            ))}
          </div>
          <div className="mf">
            <button type="button" onClick={() => onChange(options.map((o) => o.value))}>
              {t('ui.searchSelect.all')}
            </button>
            <button type="button" onClick={() => onChange([])}>
              {t('ui.searchSelect.clear')}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
