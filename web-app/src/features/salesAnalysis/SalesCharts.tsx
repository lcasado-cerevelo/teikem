// Lote 241 — gráficos del Análisis de ventas (recharts), pedido expreso del dueño para presentar a los accionistas:
// venta por mes (total o apilada por línea), venta contra cuota por representante (barra + marca de la cuota), participación por
// línea (dona), unidades por mes de los productos principales (líneas) y cobertura por producto (barras horizontales con color).
// Colores de la paleta del tema (`var(--…)`: cambian con claro/oscuro); leyenda propia en HTML (legible y accesible); cada gráfico
// lleva título, un resumen textual en `aria-label` y «Ver los datos» con la tabla alternativa.
import { useState, type ReactNode } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ComposedChart,
  Label,
  Line,
  LineChart,
  Pie,
  PieChart,
  ReferenceLine,
  ResponsiveContainer,
  Scatter,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { useLang, useT } from '../../kernel/i18n/useT'
import {
  COMPLIANCE_COLOR,
  COVERAGE_COLOR,
  COVERAGE_HIGH_WEEKS,
  COVERAGE_LOW_WEEKS,
  complianceLevel,
  monthRows,
  monthShort,
  seriesColor,
  type CoveragePoint,
  type RepQuotaPoint,
  type SalesChartSeries,
  type SharePoint,
} from './salesAnalysisRules'
import { useSaFormat } from './saFormat'

const TOOLTIP_STYLE = { background: 'var(--panel)', border: '1px solid var(--line)', borderRadius: 8, color: 'var(--text)', fontSize: 12 }
const TICK = { fontSize: 11, fill: 'var(--muted)' }

export interface DataTableSpec {
  headers: string[]
  rows: (string | number)[][]
}

export interface ChartFrameProps {
  title: string
  subtitle?: ReactNode
  /** Resumen textual (lectores de pantalla). */
  summary: string
  table: DataTableSpec
  height: number
  legend?: { label: string; color: string; kind?: 'box' | 'mark' | 'line' }[]
  actions?: ReactNode
  children: ReactNode
  empty?: boolean
}

/** Marco de un gráfico: título, acciones, el dibujo (role="img" con el resumen), la leyenda y «Ver los datos». */
export function ChartFrame({ title, subtitle, summary, table, height, legend, actions, children, empty }: ChartFrameProps) {
  const t = useT()
  return (
    <figure className="sa-chart">
      <figcaption className="sa-chart-head">
        <div className="sa-chart-title">
          <h3>{title}</h3>
          {subtitle && <p>{subtitle}</p>}
        </div>
        {actions && <div className="sa-chart-actions">{actions}</div>}
      </figcaption>
      {empty ? (
        <p className="sa-muted">{t('salesAnalysis.charts.noData')}</p>
      ) : (
        <>
          <div className="sa-chartbox" style={{ height }} role="img" aria-label={`${title}. ${summary}`}>
            <ResponsiveContainer width="100%" height="100%">
              {children as never}
            </ResponsiveContainer>
          </div>
          {legend && legend.length > 0 && (
            <ul className="sa-legend" aria-hidden="true">
              {legend.map((l) => (
                <li key={l.label}>
                  <span className={`sa-sw ${l.kind ?? 'box'}`} style={{ background: l.kind === 'mark' ? undefined : l.color, borderColor: l.color }} />
                  {l.label}
                </li>
              ))}
            </ul>
          )}
          <details className="sa-chart-data">
            <summary>{t('salesAnalysis.charts.seeData')}</summary>
            <div className="sa-scroll">
              <table className="sa-mini">
                <thead>
                  <tr>
                    {table.headers.map((h) => (
                      <th key={h} scope="col">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {table.rows.map((r, i) => (
                    <tr key={i}>
                      {r.map((c, j) => (j === 0 ? <th key={j} scope="row">{c}</th> : <td key={j}>{c}</td>))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
        </>
      )}
    </figure>
  )
}

const short = (s: string, n: number) => (s.length > n ? `${s.slice(0, n - 1)}…` : s)

// ---------------------------------------------------------------- venta por mes ----------------------------------------------------------------

export interface MonthlySalesChartProps {
  months: readonly string[]
  total: SalesChartSeries | null | undefined
  byLine: readonly SalesChartSeries[]
  /** false = sin animación (presentación e impresión: el dibujo queda completo al imprimir). */
  animate?: boolean
}

/** Venta neta por mes: total (barras) o apilada por línea (los descuentos, negativos, debajo de cero). */
export function MonthlySalesChart({ months, total, byLine, animate = true }: MonthlySalesChartProps) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const [stacked, setStacked] = useState(false)
  const series = stacked ? byLine : total ? [total] : []
  const data = monthRows(months, series, lang)
  const label = (s: SalesChartSeries) => s.label ?? s.key ?? ''
  const totals = total?.values ?? []
  const summary = months.map((m, i) => `${monthShort(m, lang)}: ${fm.money(totals[i] ?? 0)}`).join('; ')
  const table: DataTableSpec = {
    headers: [t('salesAnalysis.charts.month'), ...(stacked ? byLine.map(label) : []), t('salesAnalysis.charts.total')],
    rows: months.map((m, i) => [monthShort(m, lang), ...(stacked ? byLine.map((s) => fm.money(s.values?.[i] ?? 0)) : []), fm.money(totals[i] ?? 0)]),
  }
  return (
    <ChartFrame
      title={t('salesAnalysis.charts.monthly')}
      subtitle={t(stacked ? 'salesAnalysis.charts.monthlyByLineSub' : 'salesAnalysis.charts.monthlySub')}
      summary={summary}
      table={table}
      height={280}
      empty={months.length === 0}
      legend={stacked ? byLine.map((s, i) => ({ label: label(s), color: seriesColor(i) })) : [{ label: t('salesAnalysis.charts.netSales'), color: seriesColor(0) }]}
      actions={
        <div className="seg sm" role="group" aria-label={t('salesAnalysis.charts.monthlyMode')}>
          <button type="button" className={stacked ? undefined : 'on'} aria-pressed={!stacked} onClick={() => setStacked(false)}>
            {t('salesAnalysis.charts.total')}
          </button>
          <button type="button" className={stacked ? 'on' : undefined} aria-pressed={stacked} onClick={() => setStacked(true)}>
            {t('salesAnalysis.charts.byLine')}
          </button>
        </div>
      }
    >
      <BarChart data={data} stackOffset="sign" accessibilityLayer={false} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" />
        <XAxis dataKey="label" tick={TICK} stroke="var(--line)" />
        <YAxis tickFormatter={fm.compactMoney} width={56} tick={TICK} stroke="var(--line)" />
        <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: 'rgba(127,127,127,.12)' }} formatter={(v, n) => [fm.money(Number(v)), String(n)]} />
        <ReferenceLine y={0} stroke="var(--muted)" />
        {series.map((s, i) => (
          <Bar key={s.key ?? i} dataKey={`s${i}`} name={label(s)} stackId={stacked ? 'a' : undefined} fill={seriesColor(i)} radius={stacked ? 0 : [4, 4, 0, 0]} isAnimationActive={animate} />
        ))}
      </BarChart>
    </ChartFrame>
  )
}

// ---------------------------------------------------------------- venta contra cuota ----------------------------------------------------------------

function QuotaMark(props: { cx?: number; cy?: number; payload?: RepQuotaPoint }) {
  const { cx, cy, payload } = props
  if (cx === undefined || cy === undefined || payload?.quota === null || payload?.quota === undefined) return <g />
  return (
    <g>
      <line x1={cx} x2={cx} y1={cy - 11} y2={cy + 11} stroke="var(--text)" strokeWidth={3} strokeLinecap="round" />
    </g>
  )
}

/** Venta del mes contra la cuota de cada representante: barra (color por cumplimiento) y la marca vertical de la cuota. */
export function RepQuotaChart({ points, month, title, animate = true }: { points: readonly RepQuotaPoint[]; month?: string | null; title?: string; animate?: boolean }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const data = points.map((p) => ({ ...p, name: p.rep, quota: p.quota ?? undefined }))
  const summary = points.map((p) => t('salesAnalysis.charts.repQuotaPoint', { rep: p.rep, sales: fm.money(p.sales), quota: p.quota === null ? '—' : fm.money(p.quota), pct: fm.pct(p.compliance) })).join('; ')
  return (
    <ChartFrame
      title={title ?? t('salesAnalysis.charts.repQuota')}
      subtitle={month ? monthShort(month, lang) : undefined}
      summary={summary}
      height={Math.max(180, points.length * 40 + 40)}
      empty={points.length === 0}
      legend={[
        { label: t('salesAnalysis.charts.metQuota'), color: COMPLIANCE_COLOR.met },
        { label: t('salesAnalysis.charts.nearQuota'), color: COMPLIANCE_COLOR.near },
        { label: t('salesAnalysis.charts.belowQuota'), color: COMPLIANCE_COLOR.below },
        { label: t('salesAnalysis.charts.quotaMark'), color: 'var(--text)', kind: 'mark' },
      ]}
      table={{
        headers: [t('salesAnalysis.col.rep'), t('salesAnalysis.col.route'), t('salesAnalysis.col.sales'), t('salesAnalysis.col.quota'), t('salesAnalysis.col.compliance')],
        rows: points.map((p) => [p.rep, p.route, fm.money(p.sales), p.quota === null ? '—' : fm.money(p.quota), fm.pct(p.compliance)]),
      }}
    >
      <ComposedChart data={data} layout="vertical" accessibilityLayer={false} margin={{ top: 4, right: 16, bottom: 0, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" horizontal={false} />
        <XAxis type="number" tickFormatter={fm.compactMoney} tick={TICK} stroke="var(--line)" />
        <YAxis type="category" dataKey="name" width={118} tick={TICK} tickFormatter={(v) => short(String(v), 17)} stroke="var(--line)" />
        <Tooltip
          contentStyle={TOOLTIP_STYLE}
          cursor={{ fill: 'rgba(127,127,127,.12)' }}
          formatter={(v, n) => [fm.money(Number(v)), n === 'quota' ? t('salesAnalysis.col.quota') : t('salesAnalysis.col.sales')]}
          labelFormatter={(l, p) => {
            const row = p?.[0]?.payload as RepQuotaPoint | undefined
            return row ? `${row.rep}${row.route ? ` · ${row.route}` : ''} — ${fm.pct(row.compliance)}` : String(l)
          }}
        />
        <Bar dataKey="sales" name="sales" barSize={18} radius={[0, 4, 4, 0]} isAnimationActive={animate}>
          {data.map((p) => (
            <Cell key={p.key} fill={COMPLIANCE_COLOR[complianceLevel(p.compliance)]} />
          ))}
        </Bar>
        <Scatter dataKey="quota" name="quota" shape={<QuotaMark />} isAnimationActive={false} />
      </ComposedChart>
    </ChartFrame>
  )
}

// ---------------------------------------------------------------- participación por línea ----------------------------------------------------------------

/** Participación de cada línea en la venta de producto (dona con el total al centro). */
export function LineShareChart({ points, title, animate = true }: { points: readonly SharePoint[]; title?: string; animate?: boolean }) {
  const t = useT()
  const fm = useSaFormat()
  const total = points.reduce((s, p) => s + p.value, 0)
  const summary = points.map((p) => `${p.name}: ${fm.money(p.value)} (${fm.pct(p.share)})`).join('; ')
  return (
    <ChartFrame
      title={title ?? t('salesAnalysis.charts.lineShare')}
      subtitle={t('salesAnalysis.charts.lineShareSub')}
      summary={summary}
      height={240}
      empty={points.length === 0}
      legend={points.map((p, i) => ({ label: `${p.name} · ${fm.pct(p.share)}`, color: seriesColor(i) }))}
      table={{ headers: [t('salesAnalysis.col.line'), t('salesAnalysis.col.sales'), t('salesAnalysis.col.share')], rows: points.map((p) => [p.name, fm.money(p.value), fm.pct(p.share)]) }}
    >
      <PieChart accessibilityLayer={false}>
        <Pie data={points as SharePoint[]} dataKey="value" nameKey="name" innerRadius="55%" outerRadius="88%" paddingAngle={points.length > 1 ? 2 : 0} stroke={points.length > 1 ? 'var(--panel)' : 'none'} isAnimationActive={animate}>
          {points.map((p, i) => (
            <Cell key={p.name} fill={seriesColor(i)} />
          ))}
          <Label value={fm.compactMoney(total)} position="center" fill="var(--text)" fontSize={16} fontWeight={700} />
        </Pie>
        <Tooltip contentStyle={TOOLTIP_STYLE} formatter={(v, n) => [fm.money(Number(v)), String(n)]} />
      </PieChart>
    </ChartFrame>
  )
}

// ---------------------------------------------------------------- unidades por mes de los principales ----------------------------------------------------------------

/** Unidades por mes de los productos principales (una línea por producto, como el gráfico del Excel). */
export function TopUnitsChart({ months, series, title, animate = true }: { months: readonly string[]; series: readonly SalesChartSeries[]; title?: string; animate?: boolean }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const data = monthRows(months, series, lang)
  const label = (s: SalesChartSeries) => s.label ?? s.key ?? ''
  const summary = series.map((s) => `${label(s)}: ${(s.values ?? []).map((v) => fm.units(v)).join(', ')}`).join('; ')
  return (
    <ChartFrame
      title={title ?? t('salesAnalysis.charts.topUnits', { n: series.length })}
      subtitle={t('salesAnalysis.charts.topUnitsSub')}
      summary={summary}
      height={280}
      empty={months.length === 0 || series.length === 0}
      legend={series.map((s, i) => ({ label: label(s), color: seriesColor(i), kind: 'line' as const }))}
      table={{ headers: [t('salesAnalysis.charts.month'), ...series.map(label)], rows: months.map((m, i) => [monthShort(m, lang), ...series.map((s) => fm.units(s.values?.[i] ?? 0))]) }}
    >
      <LineChart data={data} accessibilityLayer={false} margin={{ top: 8, right: 12, bottom: 0, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" />
        <XAxis dataKey="label" tick={TICK} stroke="var(--line)" />
        <YAxis tickFormatter={(v) => fm.units(Number(v))} width={56} tick={TICK} stroke="var(--line)" />
        <Tooltip contentStyle={TOOLTIP_STYLE} formatter={(v, n) => [fm.units(Number(v)), String(n)]} />
        {series.map((s, i) => (
          <Line key={s.key ?? i} type="monotone" dataKey={`s${i}`} name={label(s)} stroke={seriesColor(i)} strokeWidth={2} dot={months.length <= 13 ? { r: 3, fill: seriesColor(i), strokeWidth: 0 } : false} isAnimationActive={animate} />
        ))}
      </LineChart>
    </ChartFrame>
  )
}

// ---------------------------------------------------------------- cobertura ----------------------------------------------------------------

/** Semanas de cobertura por producto (de menor a mayor): rojo poco, ámbar, verde mucho; las rayas marcan los umbrales. */
export function CoverageChart({ points, title, total, animate = true }: { points: readonly CoveragePoint[]; title?: string; total?: number; animate?: boolean }) {
  const t = useT()
  const fm = useSaFormat()
  const summary = points.map((p) => t('salesAnalysis.charts.coveragePoint', { sku: p.label, weeks: fm.dec1(p.weeks) })).join('; ')
  return (
    <ChartFrame
      title={title ?? t('salesAnalysis.charts.coverage')}
      subtitle={total !== undefined && total > points.length ? t('salesAnalysis.charts.coverageSubTop', { n: points.length, total }) : t('salesAnalysis.charts.coverageSub')}
      summary={summary}
      height={Math.max(160, points.length * 26 + 40)}
      empty={points.length === 0}
      legend={[
        { label: t('salesAnalysis.coverage.low', { n: COVERAGE_LOW_WEEKS }), color: COVERAGE_COLOR.low },
        { label: t('salesAnalysis.coverage.mid', { from: COVERAGE_LOW_WEEKS, to: COVERAGE_HIGH_WEEKS }), color: COVERAGE_COLOR.mid },
        { label: t('salesAnalysis.coverage.high', { n: COVERAGE_HIGH_WEEKS }), color: COVERAGE_COLOR.high },
      ]}
      table={{
        headers: [t('salesAnalysis.col.sku'), t('salesAnalysis.col.name'), t('salesAnalysis.col.available'), t('salesAnalysis.col.weeklyAverage'), t('salesAnalysis.col.coverageWeeks')],
        rows: points.map((p) => [p.label, p.name, fm.units(p.available), fm.dec1(p.weeklyAverage), fm.dec1(p.weeks)]),
      }}
    >
      <BarChart data={points as CoveragePoint[]} layout="vertical" accessibilityLayer={false} margin={{ top: 4, right: 16, bottom: 0, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" horizontal={false} />
        <XAxis type="number" tick={TICK} stroke="var(--line)" tickFormatter={(v) => fm.units(Number(v))} />
        <YAxis type="category" dataKey="label" width={92} tick={TICK} tickFormatter={(v) => short(String(v), 13)} stroke="var(--line)" />
        <Tooltip
          contentStyle={TOOLTIP_STYLE}
          cursor={{ fill: 'rgba(127,127,127,.12)' }}
          formatter={(v) => [t('salesAnalysis.charts.weeks', { n: fm.dec1(Number(v)) }), t('salesAnalysis.col.coverageWeeks')]}
          labelFormatter={(l, p) => {
            const row = p?.[0]?.payload as CoveragePoint | undefined
            return row ? `${row.label} · ${row.name}` : String(l)
          }}
        />
        <ReferenceLine x={COVERAGE_LOW_WEEKS} stroke="var(--danger)" strokeDasharray="4 3" />
        <ReferenceLine x={COVERAGE_HIGH_WEEKS} stroke="var(--ok)" strokeDasharray="4 3" />
        <Bar dataKey="weeks" barSize={14} radius={[0, 4, 4, 0]} minPointSize={2} isAnimationActive={animate}>
          {points.map((p) => (
            <Cell key={p.key} fill={COVERAGE_COLOR[p.level]} />
          ))}
        </Bar>
      </BarChart>
    </ChartFrame>
  )
}
