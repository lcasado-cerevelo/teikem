import { useT } from '../kernel/i18n/useT'
import { BrandLockup } from '../kernel/ui/Brand'

/** Indicador de carga del shell (pantalla completa —con el lockup de Teikem centrado arriba— o dentro del área de trabajo). */
export function Splash({ full = false }: { full?: boolean }) {
  const t = useT()
  return (
    <div className={full ? 'splash full' : 'splash'} role="status" aria-live="polite">
      {full && <BrandLockup className="splash-brand" />}
      <div className="spin" />
      <span className="sr-only">{t('common.loading')}</span>
    </div>
  )
}
