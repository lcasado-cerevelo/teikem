import type { ReactNode } from 'react'
import { PanelTitleContext } from './panelContext'
import './ui.css'

export interface PanelProps {
  /** Título del panel (ya traducido). Sin título, ícono, contador ni acciones no se pinta la cabecera. */
  title?: ReactNode
  /** Ícono antes del título (de `screenIcons`: el que la maqueta da a la pantalla en el menú). */
  icon?: ReactNode
  /** Contador a la derecha del título, en la misma línea (`.r`), p. ej. el número de filas de la tabla. */
  badge?: string | number
  /** Línea secundaria bajo el título, solo para texto descriptivo (un conteo va en `badge`). */
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

/**
 * Panel de contenido en pantalla de la maqueta: `.panel` (radio 13 px, sin sombra) con cabecera `.ph2` de una sola
 * línea (ícono + título + contador `.r` + acciones), cuerpo `.pb` y pie `.ft`. No es un modal: los modales usan
 * `Modal` (`.scrim > .pal`, radio 15 px con sombra).
 */
export function Panel({ title, icon, badge, subtitle, actions, footer, flush, className, children }: PanelProps) {
  const hasHead = title != null || icon != null || badge != null || actions != null
  return (
    <section className={['panel kit-panel', className].filter(Boolean).join(' ')}>
      {hasHead && (
        <header className="ph2 kit-ph2">
          {icon != null && <span className="kit-ic">{icon}</span>}
          {title != null && <h2 className="kit-ph2-title">{title}</h2>}
          {badge != null && <span className="r">{badge}</span>}
          {actions != null && <div className="kit-acts">{actions}</div>}
          {subtitle != null && <p className="kit-sub">{subtitle}</p>}
        </header>
      )}
      <div className={flush ? 'pb flush' : 'pb'}>
        {/* el título en texto llega a las tablas del cuerpo (nombre del archivo exportado) */}
        <PanelTitleContext.Provider value={typeof title === 'string' ? title : null}>{children}</PanelTitleContext.Provider>
      </div>
      {footer != null && <footer className="ft kit-ft">{footer}</footer>}
    </section>
  )
}
