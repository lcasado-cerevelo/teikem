import type { ReactNode } from 'react'
import { IconInbox } from './icons'

export interface EmptyStateProps {
  title: ReactNode
  body?: ReactNode
  /** Ícono SVG (por defecto, bandeja vacía). */
  icon?: ReactNode
  /** Acción sugerida (p. ej. botón "Nuevo" envuelto en <Can>). */
  action?: ReactNode
}

/** Estado vacío (`.empty`): sin datos, sin resultados de búsqueda, sección sin configurar. */
export function EmptyState({ title, body, icon, action }: EmptyStateProps) {
  return (
    <div className="empty" data-testid="empty-state">
      <div>
        <div className="ic">{icon ?? <IconInbox />}</div>
        <h2>{title}</h2>
        {body != null && <p>{body}</p>}
        {action}
      </div>
    </div>
  )
}
