// Lote 14 — franja de resumen de la maqueta (`.burst`: "Movimientos · Entradas (uds) · Salidas (uds)" del Kárdex): una
// fila de cifras con etiqueta pequeña en mayúsculas y el número en monoespaciada, separadas por una raya vertical. Envuelve
// en renglones a 360 px (sin scroll horizontal). Los textos llegan ya traducidos; `value` ya formateado (o número).
import type { ReactNode } from 'react'
import './ui.css'

/** Tono del número: `in` (entra, color de flujo), `out` (sale, peligro), `money` (dinero) o neutro. */
export type SummaryTone = 'in' | 'out' | 'money' | 'muted'

export interface SummaryItem {
  /** Clave estable (React); por defecto la etiqueta. */
  key?: string
  label: string
  value: ReactNode
  tone?: SummaryTone
  /** Texto de ayuda (tooltip) de la cifra. */
  title?: string
}

export interface SummaryBarProps {
  items: readonly SummaryItem[]
  /** Nombre accesible del grupo (p. ej. "Resumen de movimientos"). */
  label?: string
  /** Mientras carga, las cifras se atenúan (no se pintan ceros falsos). */
  loading?: boolean
  /** Contenido a la derecha (leyenda, aviso). */
  aside?: ReactNode
}

export function SummaryBar({ items, label, loading, aside }: SummaryBarProps) {
  return (
    <div className={loading ? 'burst loading' : 'burst'} role="group" aria-label={label} aria-busy={loading || undefined}>
      {items.map((it) => (
        <div key={it.key ?? it.label} className={it.tone ? `t ${it.tone}` : 't'} title={it.title}>
          <small>{it.label}</small>
          <b>{it.value}</b>
        </div>
      ))}
      {aside && <div className="leg">{aside}</div>}
    </div>
  )
}
