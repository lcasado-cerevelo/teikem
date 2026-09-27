import type { ReactNode } from 'react'
import { useCan } from './accessContext'

interface Props {
  /** Código exacto del permiso del API (p. ej. `orders.create`); con arreglo exige todos. */
  perm: string | readonly string[]
  /** Qué pintar sin permiso (por defecto nada: la acción no se pinta). */
  fallback?: ReactNode
  children: ReactNode
}

/** `<Can perm="orders.create"><button>…</button></Can>` */
export function Can({ perm, fallback = null, children }: Props) {
  const allowed = useCan(...(typeof perm === 'string' ? [perm] : perm))
  return <>{allowed ? children : fallback}</>
}
