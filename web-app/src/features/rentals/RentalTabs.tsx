// Lote F18 (Rentas F-R2) — pestañas del submódulo Rentas: Rentas · Devoluciones · Proceso de equipos · Reportes. Son las mismas
// pestañas del kit (`Tabs`, como Productos/Categorías); cada una es una pantalla con su propia dirección (no `?tab=`):
// /warehouse/rentals, /warehouse/rental-returns, /warehouse/rental-processes y /warehouse/rental-reports. En el menú lateral hay
// UN solo ítem, "Rentas" (decisión del dueño); estas pestañas llevan a lo demás. "Reportes" solo con `analytics.view` y el módulo
// Análisis (sus datos salen del motor de Análisis).
import { useNavigate } from 'react-router-dom'
import { useCan, useModule } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'

export type RentalTabKey = 'rentals' | 'returns' | 'processes' | 'reports'

const RENTAL_TAB_PATHS: Record<RentalTabKey, string> = {
  rentals: '/warehouse/rentals',
  returns: '/warehouse/rental-returns',
  processes: '/warehouse/rental-processes',
  reports: '/warehouse/rental-reports',
}

export function RentalTabs({ current }: { current: RentalTabKey }) {
  const t = useT()
  const navigate = useNavigate()
  const canAnalytics = useCan('analytics.view')
  const analyticsOn = useModule('ANALYTICS')
  const keys: RentalTabKey[] = canAnalytics && analyticsOn ? ['rentals', 'returns', 'processes', 'reports'] : ['rentals', 'returns', 'processes']
  return (
    <div className="ren-tabs">
      <Tabs<RentalTabKey>
        label={t('rentals.tabs.label')}
        value={current}
        onChange={(k) => {
          if (k !== current) navigate(RENTAL_TAB_PATHS[k])
        }}
        tabs={keys.map((k) => ({ key: k, label: t(`rentals.tabs.${k}`) }))}
      />
    </div>
  )
}
