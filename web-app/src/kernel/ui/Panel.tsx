import { useCallback, useId, useState, type MouseEvent, type ReactNode } from 'react'
import { formatQuantity } from '../i18n/numberFormat'
import { useLang, useT } from '../i18n/useT'
import { IconChevronDown } from './icons'
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
  /** Panel desplegable: la cabecera lleva una flecha (y un clic en la cabecera) para abrir y cerrar el cuerpo. Cerrado muestra `summary`. */
  collapsible?: boolean
  /** Cerrado de entrada (por omisión abierto). Si hay `storageKey` y el usuario ya eligió, gana lo que eligió. */
  defaultCollapsed?: boolean
  /** Resumen de una línea de lo que hay en el panel, visible solo mientras está cerrado. */
  summary?: ReactNode
  /** Clave para recordar en este navegador si el panel quedó abierto o cerrado (p. ej. `clients.profile`). */
  storageKey?: string
  children?: ReactNode
}

const STORAGE_PREFIX = 'teikem.panel.'

function readCollapsed(storageKey: string | undefined, fallback: boolean): boolean {
  if (!storageKey) return fallback
  try {
    const v = window.localStorage.getItem(STORAGE_PREFIX + storageKey)
    return v === '1' ? true : v === '0' ? false : fallback
  } catch {
    return fallback
  }
}

/**
 * Panel de contenido en pantalla de la maqueta: `.panel` (radio 13 px, sin sombra) con cabecera `.ph2` de una sola
 * línea (ícono + título + contador `.r` + acciones), cuerpo `.pb` y pie `.ft`. No es un modal: los modales usan
 * `Modal` (`.scrim > .pal`, radio 15 px con sombra).
 */
export function Panel({ title, icon, badge, subtitle, actions, footer, flush, className, collapsible, defaultCollapsed = false, summary, storageKey, children }: PanelProps) {
  const lang = useLang()
  const t = useT()
  const bodyId = useId()
  const [collapsedState, setCollapsed] = useState(() => readCollapsed(storageKey, defaultCollapsed))
  const collapsed = collapsible === true && collapsedState
  const toggle = useCallback(() => {
    setCollapsed((c) => {
      const next = !c
      if (storageKey) {
        try {
          window.localStorage.setItem(STORAGE_PREFIX + storageKey, next ? '1' : '0')
        } catch {
          // sin almacenamiento (ventana privada): solo vale para esta visita
        }
      }
      return next
    })
  }, [storageKey])
  // un clic en la parte «muerta» de la cabecera también abre y cierra; los botones, enlaces y campos hacen lo suyo
  const onHeadClick = (e: MouseEvent<HTMLElement>) => {
    if (!collapsible) return
    if ((e.target as HTMLElement).closest('button, a, input, select, textarea, label, [role="menu"], [role="menuitem"]')) return
    toggle()
  }
  const hasHead = title != null || icon != null || badge != null || actions != null
  return (
    <section className={['panel kit-panel', collapsible ? 'kit-fold-panel' : '', collapsed ? 'kit-collapsed' : '', className].filter(Boolean).join(' ')}>
      {hasHead && (
        <header className={collapsible ? 'ph2 kit-ph2 kit-fold-head' : 'ph2 kit-ph2'} onClick={collapsible ? onHeadClick : undefined}>
          {collapsible && (
            <button
              type="button"
              className="kit-fold"
              aria-expanded={!collapsed}
              aria-controls={bodyId}
              aria-label={collapsed ? t('ui.panel.expand') : t('ui.panel.collapse')}
              onClick={toggle}
            >
              <IconChevronDown />
            </button>
          )}
          {icon != null && <span className="kit-ic">{icon}</span>}
          {title != null && <h2 className="kit-ph2-title">{title}</h2>}
          {badge != null && <span className="r">{typeof badge === 'number' ? formatQuantity(badge, lang) : badge}</span>}
          {actions != null && <div className="kit-acts">{actions}</div>}
          {subtitle != null && <p className="kit-sub">{subtitle}</p>}
        </header>
      )}
      {collapsed && summary != null && <div className="pb kit-summary">{summary}</div>}
      {/* cerrado el cuerpo sigue montado (conserva lo que se estaba escribiendo y las consultas) pero oculto */}
      <div id={bodyId} className={flush ? 'pb flush' : 'pb'} hidden={collapsed}>
        {/* el título en texto llega a las tablas del cuerpo (nombre del archivo exportado) */}
        <PanelTitleContext.Provider value={typeof title === 'string' ? title : null}>{children}</PanelTitleContext.Provider>
      </div>
      {footer != null && !collapsed && <footer className="ft kit-ft">{footer}</footer>}
    </section>
  )
}
