import type { ReactNode } from 'react'
import './ui.css'

export interface PanelProps {
  /** Título del panel (ya traducido). Sin título no se pinta la cabecera, salvo que haya `actions`. */
  title?: ReactNode
  /** Línea secundaria bajo el título. */
  subtitle?: ReactNode
  /** Botones a la derecha de la cabecera (envuélvelos en <Can> si requieren permiso). */
  actions?: ReactNode
  /** Pie del panel (`.ft`), p. ej. totales o acciones del formulario. */
  footer?: ReactNode
  /** Sin padding en el cuerpo (tablas que llegan al borde). */
  flush?: boolean
  className?: string
  children?: ReactNode
}

/** Contenedor estándar de la maqueta: `.pal` con cabecera `.pi`, cuerpo `.pb` y pie `.ft`. */
export function Panel({ title, subtitle, actions, footer, flush, className, children }: PanelProps) {
  const hasHead = title != null || actions != null
  return (
    <section className={['pal kit-pal', className].filter(Boolean).join(' ')}>
      {hasHead && (
        <header className="pi kit-ph">
          {title != null && <h2 className="kit-title">{title}</h2>}
          {actions != null && <div className="kit-acts">{actions}</div>}
          {subtitle != null && <p className="kit-sub">{subtitle}</p>}
        </header>
      )}
      <div className={flush ? 'pb flush' : 'pb'}>{children}</div>
      {footer != null && <footer className="ft kit-ft">{footer}</footer>}
    </section>
  )
}
