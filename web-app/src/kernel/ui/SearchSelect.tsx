import { useCallback, useId, useMemo, useRef, useState, type Ref } from 'react'
import { useT } from '../i18n/useT'
import type { FilterOption } from './Filters'
import { joinFilterValues } from './filterRegistry'
import { useRegisterFilter } from './filterScopeContext'
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
  /** Búsqueda en el servidor (catálogos grandes: productos, posiciones). Ver `SearchMultiSelectProps.onSearch`. */
  onSearch?: (q: string) => void
  chosen?: readonly FilterOption[]
  loading?: boolean
}

/** Selección múltiple con buscador (`.msel`), para filtros con muchas opciones (clientes, bodegas, estatus). */
export function SearchSelect({ label, options, value, onChange, placeholder, onSearch, chosen, loading }: SearchSelectProps) {
  const t = useT()
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  // en el ámbito (línea de filtros de las exportaciones): las etiquetas de lo elegido
  useRegisterFilter(label, joinFilterValues(value.map((v) => chosen?.find((o) => o.value === v)?.label ?? options.find((o) => o.value === v)?.label ?? v), t), ref)
  return (
    <div className="f" ref={ref}>
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
        onSearch={onSearch}
        chosen={chosen}
        loading={loading}
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
  /**
   * Modo remoto (2026-10-12; catálogos grandes como productos o posiciones): lo que se escribe en el buscador sale por `onSearch` (el
   * padre pausa, consulta y entrega `options` ya filtradas por el servidor) y aquí no se filtra. Lo elegido se muestra siempre arriba con
   * su etiqueta (`chosen`, porque puede no estar en los resultados actuales). Sin "Todos": no se puede elegir un catálogo entero.
   */
  onSearch?: (q: string) => void
  chosen?: readonly FilterOption[]
  /** Con `onSearch`: la búsqueda está en curso. */
  loading?: boolean
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
  onSearch,
  chosen,
  loading,
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
  const remote = onSearch !== undefined
  const labelOf = (v: string) => chosen?.find((o) => o.value === v)?.label ?? options.find((o) => o.value === v)?.label ?? v
  const shown = remote
    ? [...(chosen ?? []).filter((o) => selected.has(o.value) && matchesQ(q, o.label, o.value)), ...options.filter((o) => !selected.has(o.value))]
    : options.filter((o) => matchesQ(q, o.label, o.value))
  const summary =
    value.length === 0
      ? (placeholder ?? t('ui.filters.all'))
      : value.length === 1
        ? labelOf(value[0])
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
              onChange={(e) => {
                setQ(e.target.value)
                onSearch?.(e.target.value)
              }}
            />
          </div>
          <div className="milist" role="listbox" aria-multiselectable="true" aria-labelledby={labelledBy}>
            {shown.length === 0 && <div className="mnone">{loading ? t('common.loading') : t('ui.searchSelect.none')}</div>}
            {shown.map((o) => (
              <label key={o.value} className="mi" role="option" aria-selected={selected.has(o.value)}>
                <input type="checkbox" checked={selected.has(o.value)} onChange={() => toggle(o.value)} />
                <span>{o.label}</span>
              </label>
            ))}
          </div>
          <div className="mf">
            {!remote && (
              <button type="button" onClick={() => onChange(options.map((o) => o.value))}>
                {t('ui.searchSelect.all')}
              </button>
            )}
            <button type="button" onClick={() => onChange([])}>
              {t('ui.searchSelect.clear')}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
