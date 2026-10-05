import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { IconChevronDown } from './icons'
import { toast } from './toast'
import { useDismiss } from './useDismiss'
import './ui.css'

export interface ReportMenuItem {
  key: string
  label: string
  /** Texto largo (se ve al pasar el mouse): qué hace esa opción. */
  hint?: string
  /** Genera el informe con esta opción; si lanza, se avisa con un toast de error. */
  run: () => Promise<void>
}

export interface ReportMenuButtonProps {
  label: string
  /** Qué hace el botón (al pasar el mouse). */
  hint?: string
  icon?: ReactNode
  items: readonly ReportMenuItem[]
  /** Mensaje del toast si una opción falla. */
  errorMessage: string
  /** Texto del botón mientras se genera. */
  workingLabel: string
  /** Nombre accesible del menú. */
  menuLabel: string
  className?: string
}

/**
 * Botón de informe con menú de opciones (2026-10-05): al tocarlo se despliegan las opciones y al elegir una se genera el PDF
 * (sin un selector aparte al lado). El menú se abre hacia abajo, se cierra con Esc / clic afuera y se recorre con las flechas.
 */
export function ReportMenuButton({ label, hint, icon, items, errorMessage, workingLabel, menuLabel, className }: ReportMenuButtonProps) {
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const ref = useRef<HTMLSpanElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuId = useId()

  const close = useCallback(() => setOpen(false), [])
  useDismiss(ref, open, close)

  // al abrir, el foco pasa a la primera opción (teclado)
  useEffect(() => {
    if (open) menuRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
  }, [open])

  const choose = async (item: ReportMenuItem) => {
    setOpen(false)
    triggerRef.current?.focus()
    setBusy(true)
    try {
      await item.run()
    } catch {
      toast.error(errorMessage)
    } finally {
      setBusy(false)
    }
  }

  const onMenuKey = (e: KeyboardEvent<HTMLDivElement>) => {
    const els = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? [])
    const i = els.indexOf(document.activeElement as HTMLButtonElement)
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const next = e.key === 'ArrowDown' ? (i + 1) % els.length : (i - 1 + els.length) % els.length
      els[next]?.focus()
    } else if (e.key === 'Escape') {
      e.stopPropagation()
      setOpen(false)
      triggerRef.current?.focus()
    } else if (e.key === 'Tab') {
      setOpen(false)
    }
  }

  return (
    <span className={open ? 'rm open' : 'rm'} ref={ref}>
      <button
        ref={triggerRef}
        type="button"
        className={className ?? 'btn'}
        title={hint}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-busy={busy || undefined}
        disabled={busy}
        onClick={() => setOpen((o) => !o)}
      >
        {icon}
        <span>{busy ? workingLabel : label}</span>
        {!busy && <IconChevronDown />}
      </button>
      {open && (
        <div id={menuId} ref={menuRef} className="rm-menu" role="menu" aria-label={menuLabel} onKeyDown={onMenuKey}>
          {items.map((it) => (
            <button key={it.key} type="button" role="menuitem" title={it.hint} onClick={() => void choose(it)}>
              {it.label}
            </button>
          ))}
        </div>
      )}
    </span>
  )
}
