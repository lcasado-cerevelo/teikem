import { useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../i18n/useT'
import { IconClose } from './icons'
import { FilterScope } from './FilterScope'
import { PanelTitleContext } from './panelContext'
import './ui.css'

export interface ModalProps {
  open: boolean
  title: ReactNode
  onClose: () => void
  /** Botones del pie (`.ft`); el primario va al final. */
  footer?: ReactNode
  /** 'sm' 440 px, 'md' 640 px (por defecto), 'lg' 880 px; siempre ≤ ancho de pantalla. */
  size?: 'sm' | 'md' | 'lg'
  /** false = no se cierra con Escape ni con clic fuera (p. ej. mientras guarda). */
  dismissible?: boolean
  children?: ReactNode
}

const FOCUSABLE = 'input:not([disabled]),select:not([disabled]),textarea:not([disabled]),button:not([disabled]),[href],[tabindex]:not([tabindex="-1"])'

/** Modal con el mismo padding y tipografía del shell (`.scrim.on > .pal` con `.pi/.pb/.ft`). Se monta en un portal. */
export function Modal({ open, title, onClose, footer, size = 'md', dismissible = true, children }: ModalProps) {
  const t = useT()
  const titleId = useId()
  const boxRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef(onClose)
  const dismissRef = useRef(dismissible)
  useEffect(() => {
    closeRef.current = onClose
    dismissRef.current = dismissible
  })

  useEffect(() => {
    if (!open) return
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    // foco al primer control del cuerpo (o al diálogo)
    const box = boxRef.current
    const first = box?.querySelector('.pb')?.querySelector<HTMLElement>(FOCUSABLE)
    ;(first ?? box)?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && dismissRef.current) {
        e.stopPropagation()
        closeRef.current()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      previous?.focus()
    }
  }, [open])

  if (!open) return null
  return createPortal(
    <div
      className="scrim on"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && dismissible) onClose()
      }}
    >
      <div
        ref={boxRef}
        className={`pal kit-pal${size === 'md' ? '' : ` ${size}`}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <header className="pi kit-ph">
          <h2 id={titleId} className="kit-title">
            {title}
          </h2>
          <button type="button" className="iconbtn modal-x" aria-label={t('ui.modal.close')} onClick={onClose} disabled={!dismissible}>
            <IconClose />
          </button>
        </header>
        <div className="pb">
          {/* el portal hereda el contexto del Panel que abre el modal: aquí manda el título del modal */}
          {/* ni los filtros de la pantalla llegan al modal (sus tablas no llevan la línea de filtros) ni los de adentro se
              anotan en la pantalla */}
          <PanelTitleContext.Provider value={typeof title === 'string' ? title : null}>
            <FilterScope off>{children}</FilterScope>
          </PanelTitleContext.Provider>
        </div>
        {footer != null && <footer className="ft kit-ft">{footer}</footer>}
      </div>
    </div>,
    document.body,
  )
}
