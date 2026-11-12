// Lote 241 — pestaña «Gráficos»: reúne los gráficos del análisis para presentar (venta por mes total o por línea, venta contra
// cuota por representante, participación por línea, unidades por mes de los principales y cobertura por producto) y
// «Imprimir presentación»: hoja de estilos de impresión limpia (tema claro siempre, logo y nombre de la compañía, título y
// período; un gráfico por bloque sin partirse; sin animación, así el dibujo queda completo al imprimir) para guardar como PDF desde el diálogo del navegador y llevar a los accionistas.
import { useMemo, useState } from 'react'
import { useSession } from '../../app/session'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { pickLogoUrl, useCompanyLogos } from '../../kernel/ui/brandLogos'
import { Filters } from '../../kernel/ui/Filters'
import { Spinner } from '../../kernel/ui/Spinner'
import { useSalesCharts } from './salesAnalysisApi'
import { collapseShares, coveragePoints, lineShareFromCharts, monthLong, monthOptions, repQuotaFromCharts } from './salesAnalysisRules'
import { MonthSelect, QueryError } from './SaParts'
import { CoverageChart, LineShareChart, MonthlySalesChart, RepQuotaChart, TopUnitsChart } from './SalesCharts'

const COVERAGE_TOP = 20
const MAX_SLICES = 8

export function ChartsTab() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const { me } = useSession()
  const logos = useCompanyLogos()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [month, setMonth] = useState('')
  const q = useSalesCharts(from, to, month)
  const d = q.data
  const options = useMemo(() => monthOptions(f.today(), 36), [f])
  const months = d?.months ?? []
  const logo = pickLogoUrl(logos.urls, 'lockup', 'light')
  const company = me?.tenantName ?? ''

  return (
    <div className="sa-tab">
      <div className="sa-toolbar sa-noprint">
        <Filters label={t('salesAnalysis.charts.filters')}>
          <MonthSelect id="sa-ch-from" label={t('salesAnalysis.movement.from')} value={from || (months[0] ?? '')} options={options} onChange={setFrom} />
          <MonthSelect id="sa-ch-to" label={t('salesAnalysis.movement.to')} value={to || (months[months.length - 1] ?? '')} options={options} onChange={setTo} />
          <MonthSelect id="sa-ch-month" label={t('salesAnalysis.charts.quotaMonth')} value={month || (d?.month ?? '')} options={options} onChange={setMonth} />
        </Filters>
        <div className="sa-toolbar-act">
          <button type="button" className="btn flow" onClick={() => window.print()} disabled={!d}>
            {t('salesAnalysis.charts.print')}
          </button>
        </div>
      </div>
      <p className="sa-muted sa-noprint">{t('salesAnalysis.charts.printHint')}</p>
      {q.isError ? (
        <QueryError error={q.error} />
      ) : !d ? (
        <Spinner block label={t('common.loading')} />
      ) : (
        <section className="sa-present" aria-label={t('salesAnalysis.charts.presentation')}>
          <header className="sa-present-head">
            {logo ? <img src={logo} alt={company} className="sa-present-logo" /> : null}
            <div>
              <p className="sa-present-company">{company}</p>
              <h2>{t('salesAnalysis.charts.presentationTitle')}</h2>
              <p className="sa-muted">
                {t('salesAnalysis.charts.presentationPeriod', { from: monthLong(months[0] ?? '', lang), to: monthLong(months[months.length - 1] ?? '', lang), month: monthLong(d.month ?? '', lang), date: f.dateLong(f.today(), { day: 'numeric', month: 'long', year: 'numeric' }) })}
              </p>
            </div>
          </header>
          <div className="sa-chart-grid">
            <div className="sa-block wide">
              <MonthlySalesChart animate={false} months={months} total={d.totalByMonth} byLine={d.byLineByMonth ?? []} />
            </div>
            <div className="sa-block">
              <RepQuotaChart animate={false} points={repQuotaFromCharts(d.repVsQuota ?? [])} month={d.month} />
            </div>
            <div className="sa-block">
              <LineShareChart animate={false} points={collapseShares(lineShareFromCharts(d.lineShare ?? []), MAX_SLICES, t('salesAnalysis.charts.others'))} />
            </div>
            <div className="sa-block wide">
              <TopUnitsChart animate={false} months={months} series={d.topProductUnits ?? []} />
            </div>
            <div className="sa-block wide">
              <CoverageChart animate={false} points={coveragePoints(d.coverage ?? [], COVERAGE_TOP)} total={(d.coverage ?? []).filter((c) => c.coverageWeeks !== null && c.coverageWeeks !== undefined).length} />
            </div>
          </div>
        </section>
      )}
    </div>
  )
}
