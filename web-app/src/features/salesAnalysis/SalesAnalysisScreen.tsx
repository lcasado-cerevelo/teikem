// Lote 241 — Análisis → Análisis de ventas (`/analytics/sales`, `sales.analysis.view|manage` + módulo SALES_ORDERS). Pestañas en
// `?tab=` (rep = sin parámetro, movement, charts, setup; la sección de Configuración en `?sec=`): «Por representante» (el libro
// mensual del dueño), «Movimiento de productos» (su segundo libro), «Gráficos» (para presentar) y «Configuración». Arriba, el
// aviso del ESTADO del análisis (clientes sin representante, marcas sin línea, historial sin emparejar, sin fecha de corte o
// traslape, sin historial) con enlaces a la sección donde se arregla.
import { Link, useSearchParams } from 'react-router-dom'
import { useT } from '../../kernel/i18n/useT'
import { Tabs } from '../../kernel/ui/Tabs'
import { ByRepTab } from './ByRepTab'
import { ChartsTab } from './ChartsTab'
import { MovementTab } from './MovementTab'
import { SetupTab } from './SetupTab'
import { useSalesStatus } from './salesAnalysisApi'
import { statusIssues } from './salesAnalysisRules'
import './salesAnalysis.css'

const SA_TABS = ['rep', 'movement', 'charts', 'setup'] as const
type SaTab = (typeof SA_TABS)[number]

export default function SalesAnalysisScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab') as SaTab | null
  const tab: SaTab = raw && SA_TABS.includes(raw) ? raw : 'rep'
  const setTab = (k: SaTab) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete('sec')
        if (k === 'rep') next.delete('tab')
        else next.set('tab', k)
        return next
      },
      { replace: true },
    )
  return (
    <div className="sa-screen">
      <div className="head sa-noprint">
        <div>
          <h1>{t('salesAnalysis.title')}</h1>
          <p>{t('salesAnalysis.subtitle')}</p>
        </div>
      </div>
      <StatusBanner />
      <div className="sa-noprint">
        <Tabs<SaTab>
          label={t('salesAnalysis.tabs.label')}
          value={tab}
          onChange={setTab}
          tabs={SA_TABS.map((k) => ({ key: k, label: t(`salesAnalysis.tabs.${k}`) }))}
        />
      </div>
      {tab === 'rep' ? <ByRepTab /> : tab === 'movement' ? <MovementTab /> : tab === 'charts' ? <ChartsTab /> : <SetupTab />}
    </div>
  )
}

/** Aviso del estado del análisis con enlaces a donde se arregla (solo si hay algo que arreglar). */
export function StatusBanner() {
  const t = useT()
  const status = useSalesStatus()
  const issues = statusIssues(status.data)
  if (issues.length === 0) return null
  return (
    <aside className="sa-status sa-noprint" aria-label={t('salesAnalysis.statusBanner.title')}>
      <strong>{t('salesAnalysis.statusBanner.title')}</strong>
      <ul>
        {issues.map((i) => (
          <li key={i.key}>
            <span>{t(`salesAnalysis.statusBanner.${i.key}`, { n: i.count })}</span>{' '}
            <Link to={`?tab=setup&sec=${i.section}`}>{t(`salesAnalysis.statusBanner.fix.${i.section}`)}</Link>
          </li>
        ))}
      </ul>
    </aside>
  )
}
