// Lote F18 (Rentas F-R2) — reportes e indicadores de rentas. `/warehouse/rental-reports` (`rental.view` + RENTAL_EQUIPMENT por la
// ruta; además `analytics.view` + módulo Análisis con `ModuleGate`, porque los datos salen del motor de Análisis; manual 11 §10).
// No hay un segundo motor en la web: lo sembrado por R3 (Lote 29) se lee del API de Análisis —
// - Indicadores y gráficos cuya fuente es de rentas (`RENTAL`, `RENTAL_RETURN`, `RENTAL_PROCESS`): valor
//   (`/indicators/{id}/value`) y puntos (`/charts/{id}/data` → `ChartVisual`), con enlace a Análisis para encenderlos en el Pulso.
// - Vistas (reportes) sobre esas fuentes: las 5 de sistema (Equipos en renta por cliente, Rentas por vencer (7 días), Rentas
//   vencidas, Devoluciones de renta por motivo, Equipos en proceso) y las que haya creado la compañía; la elegida se corre
//   (`/reports/{id}/run`, rango "Todo") y se pinta tal cual (`ReportResultTable`). `?view=<id>` abre una vista directamente.
import { useMemo } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ModuleGate } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { CARDS_QUERY, Chip, EmptyState, Panel, Spinner, useMediaQuery } from '../../kernel/ui'
import { IconChart, IconDoc } from '../../kernel/ui/screenIcons'
import { useChartData, useCharts, useIndicators, useIndicatorValue, type AnalyticsDefinition } from '../analytics/api'
import { ChartVisual } from '../analytics/ChartVisual'
import { formatValue } from '../analytics/format'
import { ReportResultTable } from '../analytics/ReportResultTable'
import { isRentalSource, useRentalReports, useReportRun } from './api'
import { RentalTabs } from './RentalTabs'
import '../analytics/pulse.css'
import './rentals.css'

export default function RentalReportsScreen() {
  const t = useT()
  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('rentalReports.title')}</h1>
          <p>{t('rentalReports.subtitle')}</p>
        </div>
      </div>
      <RentalTabs current="reports" />
      <ModuleGate module="ANALYTICS" perm="analytics.view">
        <RentalReports />
      </ModuleGate>
    </div>
  )
}

function errorText(err: unknown, fallback: string): string {
  return err instanceof ApiError ? err.title : fallback
}

function IndicatorCard({ indicator }: { indicator: AnalyticsDefinition }) {
  const t = useT()
  const value = useIndicatorValue(indicator.id ?? null)
  return (
    <div className="ren-report-card" role="group" aria-label={indicator.name ?? ''}>
      <div className="v">
        {value.isLoading ? <Spinner label={t('common.loading')} /> : value.error ? '—' : formatValue(value.data?.value ?? null, indicator.isMoney ?? false)}
      </div>
      <p>
        <b>{indicator.name}</b>
      </p>
      {indicator.description && <p className="help">{indicator.description}</p>}
    </div>
  )
}

function ChartCard({ chart }: { chart: AnalyticsDefinition }) {
  const t = useT()
  const data = useChartData(chart.id ?? null)
  return (
    <Panel icon={<IconChart />} title={chart.name ?? ''} subtitle={chart.description ?? undefined}>
      {data.isLoading ? (
        <Spinner block label={t('common.loading')} />
      ) : data.error ? (
        <p className="ferr">{errorText(data.error, t('errors.generic'))}</p>
      ) : (
        <div className="pulse">
          <ChartVisual chartType={data.data?.chartType ?? chart.chartType ?? 'BAR'} points={data.data?.points ?? []} isMoney={data.data?.isMoney ?? false} name={chart.name ?? ''} />
        </div>
      )}
    </Panel>
  )
}

function RentalReports() {
  const t = useT()
  const cards = useMediaQuery(CARDS_QUERY)
  const [params, setParams] = useSearchParams()
  const indicators = useIndicators()
  const charts = useCharts()
  const reports = useRentalReports()
  const rentalIndicators = useMemo(() => (indicators.data ?? []).filter((d) => isRentalSource(d.dataSource)), [indicators.data])
  const rentalCharts = useMemo(() => (charts.data ?? []).filter((d) => isRentalSource(d.dataSource)), [charts.data])
  const views = reports.data ?? []
  const wanted = Number(params.get('view'))
  const selected = views.find((v) => v.id === wanted) ?? views[0] ?? null
  const run = useReportRun(selected?.id ?? null)

  const pick = (id: number) => {
    const next = new URLSearchParams(params)
    next.set('view', String(id))
    setParams(next, { replace: true })
  }

  return (
    <>
      <Panel
        icon={<IconChart />}
        title={t('rentalReports.indicators')}
        actions={
          <Link className="btn sm" to="/analytics/indicators">
            {t('rentalReports.toAnalytics')}
          </Link>
        }
      >
        <p className="help">{t('rentalReports.pulseHint')}</p>
        {indicators.isLoading ? (
          <Spinner block label={t('common.loading')} />
        ) : rentalIndicators.length === 0 ? (
          <p className="note">{t('rentalReports.noIndicators')}</p>
        ) : (
          <div className="ren-report-defs">
            {rentalIndicators.map((d) => (
              <IndicatorCard key={d.id} indicator={d} />
            ))}
          </div>
        )}
      </Panel>

      {rentalCharts.map((c) => (
        <ChartCard key={c.id} chart={c} />
      ))}

      <Panel flush icon={<IconDoc />} title={t('rentalReports.views')} badge={views.length}>
        {reports.isLoading ? (
          <Spinner block label={t('common.loading')} />
        ) : reports.error ? (
          <p className="pb ferr" role="alert">
            {errorText(reports.error, t('errors.generic'))}
          </p>
        ) : views.length === 0 ? (
          <EmptyState title={t('rentalReports.noViews')} />
        ) : (
          <div className="md">
            {cards ? (
              <div className="pb">
                <div className="f">
                  <label htmlFor="rental-report-view">{t('rentalReports.pickView')}</label>
                  <select id="rental-report-view" value={selected?.id ?? ''} onChange={(e) => pick(Number(e.target.value))}>
                    {views.map((v) => (
                      <option key={v.id} value={v.id}>
                        {v.name}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            ) : (
              <nav className="domlist" aria-label={t('rentalReports.views')}>
                {views.map((v) => (
                  <button
                    key={v.id}
                    type="button"
                    className={v.id === selected?.id ? 'domit on' : 'domit'}
                    aria-current={v.id === selected?.id ? 'true' : undefined}
                    onClick={() => pick(v.id ?? 0)}
                  >
                    <span>{v.name}</span>
                    <span className="cnt">{v.isSystem ? t('rentalReports.system') : t('rentalReports.company')}</span>
                  </button>
                ))}
              </nav>
            )}
            <section aria-label={selected?.name ?? ''} style={{ minWidth: 0 }}>
              {selected && (
                <div className="pb">
                  <h3 className="ren-h3">
                    {selected.name} {selected.isSystem && <Chip tone="cap">{t('rentalReports.system')}</Chip>}
                  </h3>
                  {selected.description && <p className="help">{selected.description}</p>}
                  {run.error && (
                    <p className="ferr" role="alert">
                      {errorText(run.error, t('errors.generic'))}
                    </p>
                  )}
                </div>
              )}
              {selected && !run.error && <ReportResultTable result={run.data} name={selected.name ?? ''} loading={run.isLoading} emptyText={t('rentalReports.emptyView')} />}
            </section>
          </div>
        )}
      </Panel>
    </>
  )
}
