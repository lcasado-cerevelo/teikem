// Lote 13 (plan de cambios, lote 3) — filtro "No. de orden" de la lista de recolecciones: combobox de texto libre que sugiere
// números de orden existentes (`GET /api/v1/pick-batches?orderNumber=<texto>&take=20`, 250 ms entre teclas, sin repetir) y
// fija el texto al elegir uno. El valor es el texto (el API filtra por "contiene"); Enter sin sugerencia resaltada deja lo
// escrito. Mismo patrón de teclado que los selectores del almacén (↑/↓/Enter/Escape) y aviso sin sacar de la pantalla si el
// usuario no puede leer (403).
import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useT } from '../../kernel/i18n'
import { textFilterValue } from '../../kernel/ui/filterRegistry'
import { useRegisterFilter } from '../../kernel/ui/filterScopeContext'
import { IconClose } from '../../kernel/ui/icons'
import { useDismiss } from '../../kernel/ui/useDismiss'
import { usePickBatches } from './api'
import { orderNumberSuggestions } from './pickBatchView'

export interface OrderNumberFilterProps {
  label: string
  value: string
  onChange: (value: string) => void
}

export function OrderNumberFilter({ label, value, onChange }: OrderNumberFilterProps) {
  const t = useT()
  const inputId = useId()
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [search, setSearch] = useState(value.trim())
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)
  // en el ámbito de filtros (línea "Filtros: …" de las exportaciones)
  useRegisterFilter(label, textFilterValue(value), boxRef)

  useEffect(() => {
    const h = setTimeout(() => setSearch(value.trim()), 250)
    return () => clearTimeout(h)
  }, [value])

  const list = usePickBatches({ orderNumber: search, take: 20 }, { enabled: open && search.length > 0, handleAccessDenied: false })
  const options = useMemo(() => orderNumberSuggestions(list.data?.items ?? []), [list.data])

  const choose = (n: string) => {
    onChange(n)
    setOpen(false)
    setActive(-1)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) setOpen(true)
      else setActive((i) => Math.min(i + 1, options.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter' && open) {
      e.preventDefault()
      const n = options[active]
      if (n) choose(n)
      else setOpen(false)
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
    }
  }

  const showList = open && search.length > 0
  let status: string | null = null
  if (list.isLoading) status = t('common.loading')
  else if (list.error) status = t('warehouse.pickBatches.orderFilter.noAccess')
  else if (options.length === 0) status = t('warehouse.pickBatches.orderFilter.none')

  return (
    <div className="f">
      <label htmlFor={inputId}>{label}</label>
      <div ref={boxRef} className={showList ? 'msel cpick open' : 'msel cpick'}>
        <div className="cpin">
          <input
            id={inputId}
            type="text"
            role="combobox"
            autoComplete="off"
            aria-autocomplete="list"
            aria-expanded={showList}
            aria-controls={listId}
            aria-activedescendant={showList && options[active] ? `${listId}-${active}` : undefined}
            placeholder={t('warehouse.pickBatches.orderFilter.placeholder')}
            value={value}
            onFocus={() => setOpen(true)}
            onChange={(e) => {
              onChange(e.target.value)
              setActive(-1)
              setOpen(true)
            }}
            onKeyDown={onKeyDown}
          />
          {value && (
            <button type="button" className="iconbtn" aria-label={t('warehouse.pickBatches.orderFilter.clear')} onClick={() => choose('')}>
              <IconClose />
            </button>
          )}
        </div>
        {showList && (
          <div className="mp">
            <div className="milist" id={listId} role="listbox" aria-label={label}>
              {status && <div className="mnone">{status}</div>}
              {!status &&
                options.map((n, i) => (
                  <div
                    key={n}
                    id={`${listId}-${i}`}
                    role="option"
                    aria-selected={n === value}
                    className={i === active ? 'mi on' : 'mi'}
                    onMouseDown={(e) => e.preventDefault()}
                    onMouseEnter={() => setActive(i)}
                    onClick={() => choose(n)}
                  >
                    <span className="code">{n}</span>
                  </div>
                ))}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
