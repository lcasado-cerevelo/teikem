import type { CSSProperties } from 'react'

/** Estilo de `.chip` con el color del estatus (texto del color, fondo del mismo color atenuado). Sin color → neutro. */
export function statusChipStyle(color: string | null | undefined): CSSProperties | undefined {
  if (!color) return undefined
  return { color, background: `color-mix(in srgb, ${color} 16%, transparent)` }
}
