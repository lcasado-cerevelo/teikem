// Fase 10b — panel "Vista previa" del editor de gráficos (maqueta `renderChartEditorHtml`): el gráfico con la
// configuración EN VIVO del formulario (fuente, agrupar por, cálculo, campo, tipo, rango y filtros), recalculado
// mientras se edita (400 ms después del último cambio). Dibuja con `ChartVisual`, igual que la tarjeta.
// Cómo se consulta y se replica el motor de gráficos: `chartPreview.ts`.
import { useMemo, type ReactNode } from 'react'
import { ApiError } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Panel } from '../../kernel/ui/Panel'
import { Spinner } from '../../kernel/ui/Spinner'
import { useDebounced } from '../warehouse/lineRules'
import { useChartPreview } from './api'
import { ChartVisual } from './ChartVisual'
import { chartPreviewPoints, planChartPreview, type ChartPreviewInput, type ChartPreviewRequest } from './chartPreview'
import './pulse.css'

export interface ChartPreviewProps extends ChartPreviewInput {
  isMoney: boolean
  name?: string | null
}

export function ChartPreview({ isMoney, name, ...input }: ChartPreviewProps) {
  const t = useT()
  const plan = planChartPreview(input)
  // Se espera a que el usuario deje de escribir (valor de un filtro) antes de consultar; la clave es el texto de la
  // petición para que un objeto nuevo con el mismo contenido no reinicie la espera.
  const planKey = plan ? JSON.stringify(plan) : ''
  const debouncedKey = useDebounced(planKey, 400)
  const req = useMemo(() => (debouncedKey ? (JSON.parse(debouncedKey) as ChartPreviewRequest) : null), [debouncedKey])
  const preview = useChartPreview(req)
  const result = useMemo(() => (req && preview.data ? chartPreviewPoints(preview.data, req) : null), [req, preview.data])

  let body: ReactNode
  if (!plan) body = <p className="pulse-muted">{t('analytics.charts.previewIncomplete')}</p>
  else if (preview.isError)
    body = <p className="pulse-muted">{preview.error instanceof ApiError ? preview.error.title : t('errors.generic')}</p>
  else if (!result) body = <Spinner label={t('common.loading')} />
  else
    body = (
      <>
        {/* El tipo sale de la petición ya consultada (línea = puntos en orden, no top 8) para que forma y datos cuadren. */}
        <ChartVisual chartType={req?.line ? 'LINE' : input.chartType === 'LINE' ? 'BAR' : input.chartType} points={result.points} isMoney={isMoney} name={name} />
        {result.truncated && <p className="help">{t('analytics.charts.previewTruncated')}</p>}
      </>
    )

  return (
    <div className="pulse chart-preview" aria-busy={preview.isFetching || planKey !== debouncedKey}>
      <Panel title={t('analytics.charts.previewTitle')}>{body}</Panel>
    </div>
  )
}
