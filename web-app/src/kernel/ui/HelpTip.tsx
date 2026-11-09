// Lote 230 — ayuda plegada junto a una etiqueta: un botón «?» (con nombre accesible completo) que abre y cierra un recuadro con el
// texto largo, para que un formulario ancho no se llene de párrafos de ayuda. Se abre con clic, Enter o espacio; se cierra con el
// mismo botón, con Escape (sin cerrar el modal que lo contiene: devuelve el foco al botón) o tocando fuera. El recuadro va en
// posición absoluta dentro del contenedor posicionado más cercano (en un <Field tip> ocupa el ancho del campo: nunca genera scroll
// horizontal a 360 px). Colores de la paleta del tema (claro y oscuro).
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import './ui.css'

export interface HelpTipProps {
  /** Nombre accesible del botón (ya traducido), p. ej. «Ayuda: Rotación». */
  label: string
  /** El texto de ayuda. */
  children: ReactNode
  className?: string
}

export function HelpTip({ label, children, className }: HelpTipProps) {
  const [open, setOpen] = useState(false)
  const bodyId = useId()
  const rootRef = useRef<HTMLSpanElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e: PointerEvent | MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onDown)
    return () => document.removeEventListener('pointerdown', onDown)
  }, [open])

  const onKeyDown = (e: KeyboardEvent<HTMLSpanElement>) => {
    if (e.key !== 'Escape' || !open) return
    // Escape cierra solo la ayuda, no el modal que la contiene
    e.stopPropagation()
    e.nativeEvent.stopImmediatePropagation()
    setOpen(false)
    buttonRef.current?.focus()
  }

  return (
    <span ref={rootRef} className={['help-tip', className].filter(Boolean).join(' ')} onKeyDown={onKeyDown}>
      <button
        ref={buttonRef}
        type="button"
        className="help-tip-btn"
        aria-expanded={open}
        aria-controls={open ? bodyId : undefined}
        onClick={() => setOpen((v) => !v)}
      >
        <span aria-hidden="true">?</span>
        <span className="sr-only">{label}</span>
      </button>
      {open && (
        <span id={bodyId} role="note" className="help-tip-body">
          {children}
        </span>
      )}
    </span>
  )
}
