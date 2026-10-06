// Lote F18 (Rentas F-R2) — franja de pestañas del submódulo Rentas: Rentas · Devoluciones · Proceso de equipos · Reportes. Cada
// pestaña es una pantalla con su propia dirección (enlaces, no `?tab=`): /warehouse/rentals, /warehouse/rental-returns,
// /warehouse/rental-processes y /warehouse/rental-reports. "Reportes" solo con `analytics.view` y el módulo Análisis (sus datos
// salen del motor de Análisis). Se pinta con `.seg` (se desplaza dentro de su franja si no cabe: sin scroll de página).
import { Link } from 'react-router-dom'
import { useCan, useModule } from '../../kernel/access'
import { useT } from '../../kernel/i18n'

export type RentalTabKey = 'rentals' | 'returns' | 'processes' | 'reports'

const RENTAL_TAB_PATHS: Record<RentalTabKey, string> = {
  rentals: '/warehouse/rentals',
  returns: '/warehouse/rental-returns',
  processes: '/warehouse/rental-processes',
  reports: '/warehouse/rental-reports',
}

export function RentalTabs({ current }: { current: RentalTabKey }) {
  const t = useT()
  const canAnalytics = useCan('analytics.view')
  const analyticsOn = useModule('ANALYTICS')
  const canReports = canAnalytics && analyticsOn
  const keys: RentalTabKey[] = canReports ? ['rentals', 'returns', 'processes', 'reports'] : ['rentals', 'returns', 'processes']
  return (
    <nav className="seg ren-tabs" aria-label={t('rentals.tabs.label')}>
      {keys.map((k) => (
        <Link key={k} to={RENTAL_TAB_PATHS[k]} aria-current={k === current ? 'page' : undefined} className={k === current ? 'on' : undefined}>
          {t(`rentals.tabs.${k}`)}
        </Link>
      ))}
    </nav>
  )
}
