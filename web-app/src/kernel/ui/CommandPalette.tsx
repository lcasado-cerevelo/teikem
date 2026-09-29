import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../i18n/useT'
import { filterCommands, groupCommands, type CommandItem } from './commandPaletteStore'
import { IconClose, IconSearch } from './icons'
import './ui.css'

export interface CommandPaletteProps<T extends CommandItem = CommandItem> {
  open: boolean
  /** Destinos (ya filtrados por permisos y módulos), en el orden en que se muestran. */
  items: readonly T[]
  /** Clic o Enter sobre un destino. La paleta no se cierra sola: el que la monta decide (p. ej. navegar y cerrar). */
  onSelect: (item: T) => void
  onClose: () => void
}

/**
 * Paleta de comandos de la maqueta ("Buscar o ejecutar…", `.scrim > .pal`): lista agrupada con título y subtítulo,
 * filtro libre sin acentos, ↑/↓ + Enter abre, Esc cierra. Bajo 720 px ocupa toda la pantalla.
 */
export function CommandPalette<T extends CommandItem>({ open, ...rest }: CommandPaletteProps<T>) {
  if (!open) return null
  // el cuerpo se monta al abrir: texto y selección empiezan de cero cada vez
  return createPortal(<PaletteBody {...rest} />, document.body)
}

function PaletteBody<T extends CommandItem>({ items, onSelect, onClose }: Omit<CommandPaletteProps<T>, 'open'>) {
  const t = useT()
  const baseId = useId()
  const listId = `${baseId}-list`
  const [q, setQ] = useState('')
  const [active, setActive] = useState(0)
  const listRef = useRef<HTMLDivElement>(null)

  const hits = useMemo(() => filterCommands(items, q), [items, q])
  const groups = useMemo(() => groupCommands(hits), [hits])
  const indexOf = useMemo(() => new Map(hits.map((h, i) => [h.id, i])), [hits])
  const current = Math.min(active, Math.max(hits.length - 1, 0))
  const optionId = (i: number) => `${baseId}-opt-${i}`

  // al cerrar, el foco vuelve a donde estaba (la barra, la lupa o el botón de la pantalla pendiente); se toma al
  // pintar, antes de que el buscador se lleve el foco
  const [previous] = useState(() => (document.activeElement instanceof HTMLElement ? document.activeElement : null))
  useEffect(() => () => previous?.focus(), [previous])
  // foco al buscador en un efecto (no autoFocus): en StrictMode el desmontaje simulado devuelve el foco y este lo retoma
  const inputRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    listRef.current?.querySelector('.it.on')?.scrollIntoView?.({ block: 'nearest' })
  }, [current, q])

  function onKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      e.preventDefault()
      e.stopPropagation()
      onClose()
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive(Math.min(current + 1, hits.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive(Math.max(current - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const hit = hits[current]
      if (hit) onSelect(hit)
    }
  }

  return (
    <div
      className="scrim on cmdp"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div className="pal cmdp-pal" role="dialog" aria-modal="true" aria-label={t('shell.palette.label')} onKeyDown={onKeyDown}>
        <div className="pi">
          <IconSearch />
          <input
            ref={inputRef}
            value={q}
            onChange={(e) => {
              setQ(e.target.value)
              setActive(0)
            }}
            placeholder={t('shell.palette.placeholder')}
            aria-label={t('shell.palette.placeholder')}
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={hits.length > 0 ? optionId(current) : undefined}
            autoComplete="off"
            spellCheck={false}
          />
          <button type="button" className="iconbtn" aria-label={t('ui.modal.close')} title={t('ui.modal.close')} onClick={onClose}>
            <IconClose />
          </button>
        </div>
        <div className="pl" id={listId} role="listbox" aria-label={t('shell.palette.label')} ref={listRef}>
          {hits.length === 0 ? (
            <div className="cmdp-empty" role="status">
              {t('shell.palette.empty')}
            </div>
          ) : (
            groups.map((g) => (
              <div key={g.group} role="group" aria-labelledby={`${baseId}-g-${g.group}`}>
                <div className="pgrp" id={`${baseId}-g-${g.group}`}>
                  {g.label}
                </div>
                {g.items.map((it) => {
                  const i = indexOf.get(it.id) ?? 0
                  const on = i === current
                  return (
                    <div
                      key={it.id}
                      id={optionId(i)}
                      role="option"
                      aria-selected={on}
                      className={on ? 'it on' : 'it'}
                      onMouseMove={() => {
                        if (!on) setActive(i)
                      }}
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => onSelect(it)}
                    >
                      <div className="pic" aria-hidden="true">
                        {it.icon}
                      </div>
                      <div className="cmdp-tx">
                        <div className="nm">{it.title}</div>
                        {it.subtitle && <div className="ds">{it.subtitle}</div>}
                      </div>
                    </div>
                  )
                })}
              </div>
            ))
          )}
        </div>
        <div className="ft" aria-hidden="true">
          <span>
            <kbd>↑↓</kbd> {t('shell.palette.hint.navigate')}
          </span>
          <span>
            <kbd>↵</kbd> {t('shell.palette.hint.open')}
          </span>
          <span>
            <kbd>esc</kbd> {t('shell.palette.hint.close')}
          </span>
        </div>
      </div>
    </div>
  )
}
