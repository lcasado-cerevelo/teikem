import { useT } from '../i18n/useT'
import './ui.css'

export interface SpinnerProps {
  /** Texto visible junto al spinner; sin él se anuncia "Cargando…" solo a lectores de pantalla. */
  label?: string
  /** Centrado y con aire (estado de carga de una sección). */
  block?: boolean
}

/** Indicador de carga (`.spin`). */
export function Spinner({ label, block }: SpinnerProps) {
  const t = useT()
  return (
    <span className={block ? 'spin-wrap block' : 'spin-wrap'} role="status" aria-live="polite">
      <span className="spin" aria-hidden="true" />
      {label ? <span>{label}</span> : <span className="sr-only">{t('common.loading')}</span>}
    </span>
  )
}
