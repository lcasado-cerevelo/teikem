import { useT } from '../kernel/i18n/useT'

/** Indicador de carga del shell (pantalla completa o dentro del área de trabajo). */
export function Splash({ full = false }: { full?: boolean }) {
  const t = useT()
  return (
    <div className={full ? 'splash full' : 'splash'} role="status" aria-live="polite">
      <div className="spin" />
      <span className="sr-only">{t('common.loading')}</span>
    </div>
  )
}
