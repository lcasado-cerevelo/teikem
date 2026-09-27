import type { CSSProperties, ReactNode } from 'react'
import './ui.css'

/** Tonos de la maqueta: cap (neutro), wh (almacén), disp (despacho), route (en ruta), deliv (entregado/ok),
 * cod (dinero), fail (error), warn (atención). */
export type ChipTone = 'neutral' | 'cap' | 'wh' | 'disp' | 'route' | 'deliv' | 'cod' | 'fail' | 'warn'

export interface ChipProps {
  tone?: ChipTone
  /** Color hex del catálogo (p. ej. `colorHex` de un StatusCode); tiene prioridad sobre `tone`. */
  color?: string | null
  title?: string
  children: ReactNode
}

const HEX = /^#[0-9a-f]{3,8}$/i

/** Píldora de estatus/etiqueta. Nunca envuelve el texto (white-space: nowrap). */
export function Chip({ tone = 'neutral', color, title, children }: ChipProps) {
  const style: CSSProperties | undefined =
    color && HEX.test(color) ? { color, background: `color-mix(in srgb, ${color} 15%, transparent)` } : undefined
  const cls = tone === 'neutral' || style ? 'chip' : `chip s-${tone}`
  return (
    <span className={cls} style={style} title={title}>
      {children}
    </span>
  )
}
