// Lote 240 — foto que se amplía AL DAR CLIC (no al pasar el mouse: el hover estorba al recorrer una lista). Se ve en su tamaño de lista o de ficha y, al tocarla o darle clic, se
// abre grande ahí mismo (sobre la pantalla); se cierra con clic fuera, con la X o con Esc. Es la única forma de mostrar una foto de producto en toda la web.
import { useEffect, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../i18n'

export interface ZoomPhotoProps {
  /** Dirección de la foto ya pedida con la sesión (blob). */
  src: string
  alt: string
  /** Clase de la miniatura (su tamaño lo decide quien la pone: la altura de la fila, la ficha…). */
  className?: string
  style?: CSSProperties
}

export function ZoomPhoto({ src, alt, className, style }: ZoomPhotoProps) {
  const t = useT()
  const [open, setOpen] = useState(false)
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])
  return (
    <>
      <button
        type="button"
        className="zoomphoto"
        aria-label={t('common.zoomPhoto', { name: alt })}
        onClick={(e) => {
          e.stopPropagation()
          setOpen(true)
        }}
      >
        <img src={src} alt="" className={className} style={style} />
      </button>
      {open &&
        createPortal(
          <div
            className="zoomphoto-ov"
            role="dialog"
            aria-modal="true"
            aria-label={alt}
            onClick={(e) => {
              e.stopPropagation()
              setOpen(false)
            }}
          >
            <img src={src} alt={alt} />
            <button type="button" className="zoomphoto-x" aria-label={t('common.close')} onClick={() => setOpen(false)}>
              ×
            </button>
          </div>,
          document.body,
        )}
    </>
  )
}
