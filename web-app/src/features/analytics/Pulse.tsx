// P3 — Pulso del día (inicio, solo lectura): GET /api/v1/analytics/pulse.
// Tarjetas de indicadores (un solo número) y gráficos (Recharts) marcados por cada usuario para Pulso.
// El servidor calcula el valor con el rango de fecha de cada tarjeta; el botón "Rango" de la tarjeta cambia MI rango
// (preferencia por usuario, PUT .../my-date-range con solo analytics.view) y Pulso se recalcula.
import { useMemo, useState, type CSSProperties } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { useSession } from '../../app/session'
import { Can, ModuleKeys, useCan, useModule } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n/useT'
import { EmptyState } from '../../kernel/ui/EmptyState'
import { Panel } from '../../kernel/ui/Panel'
import { Spinner } from '../../kernel/ui/Spinner'
import { usePulse, type PulseItemKind } from './api'
import { chartKind, customRangeDays, CUSTOM_RANGE, formatValue, formatYmd } from './format'
import { RangeModal } from './RangeModal'

type Indicator = components['schemas']['IndicatorValueDto']
type ChartDatum = components['schemas']['ChartDataDto']

const CHART_COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#a855f7', '#84cc16', '#ec4899']
const CARD_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16 }
const CHART_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 16 }

/** Texto del rango de fecha del indicador/gráfico si el DTO lo trae (`dateRangeMode`); null si no aplica (sin fecha o "todo").
 *  La etiqueta del modo sale del catálogo DateRangeMode del API (ya traducida); si el catálogo no responde, el código. */
function useRangeCaption(dateRangeMode: string | null | undefined, fromUtc: string | null | undefined, toUtc: string | null | undefined): string | null {
  const lang = useLang()
  const { data: modes = [] } = useLookups('DateRangeMode', { includeDisabled: true, enabled: !!dateRangeMode })
  if (!dateRangeMode || dateRangeMode === 'ALL') return null
  if (dateRangeMode === CUSTOM_RANGE) {
    if (!fromUtc && !toUtc) return null
    // toUtc es exclusivo (día siguiente a Hasta): se muestra el Hasta que eligió el usuario, como fecha UTC.
    const { from, to } = customRangeDays(fromUtc, toUtc)
    return `${formatYmd(from, lang)} → ${formatYmd(to, lang)}`
  }
  return modes.find((m) => m.code === dateRangeMode)?.label ?? dateRangeMode
}

/** Botón "Rango" de la tarjeta y su diálogo (mi rango de fecha para este indicador o gráfico). */
function RangeButton({ kind, item }: { kind: PulseItemKind; item: Indicator | ChartDatum }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  if (item.id == null) return null
  const name = item.name ?? ''
  return (
    <Can perm="analytics.view">
      <button type="button" className="btn sm" aria-label={t('analytics.range.editFor', { name })} onClick={() => setOpen(true)}>
        {t('analytics.range.edit')}
      </button>
      {open && (
        <RangeModal
          kind={kind}
          id={item.id}
          name={name}
          dateRangeMode={item.dateRangeMode}
          fromUtc={item.fromUtc}
          toUtc={item.toUtc}
          onClose={() => setOpen(false)}
        />
      )}
    </Can>
  )
}

function IndicatorCard({ indicator }: { indicator: Indicator }) {
  const caption = useRangeCaption(indicator.dateRangeMode, indicator.fromUtc, indicator.toUtc)
  return (
    <Panel title={indicator.name} actions={<RangeButton kind="indicator" item={indicator} />}>
      <div style={{ fontSize: 26, fontWeight: 800, lineHeight: 1.2 }}>{formatValue(indicator.value, indicator.isMoney)}</div>
      {caption != null && <div style={{ marginTop: 4, fontSize: 12.5, color: 'var(--muted)' }}>{caption}</div>}
    </Panel>
  )
}

function ChartCard({ chart }: { chart: ChartDatum }) {
  const t = useT()
  const caption = useRangeCaption(chart.dateRangeMode, chart.fromUtc, chart.toUtc)
  const data = useMemo(
    () => (chart.points ?? []).map((p) => ({ name: String(p.label ?? p.key ?? ''), value: p.value ?? 0 })),
    [chart.points],
  )
  const kind = chartKind(chart.chartType)
  const valueFmt = (v: number) => formatValue(v, chart.isMoney)

  return (
    <Panel title={chart.name} subtitle={caption ?? undefined} actions={<RangeButton kind="chart" item={chart} />}>
      {data.length === 0 ? (
        <p style={{ color: 'var(--muted)', fontSize: 13 }}>{t('analytics.pulse.chartEmpty')}</p>
      ) : (
        <div style={{ width: '100%', height: 240 }}>
          <ResponsiveContainer width="100%" height="100%">
            {kind === 'line' ? (
              <LineChart data={data}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11 }} />
                <Tooltip formatter={(v: number) => valueFmt(v)} />
                <Line type="monotone" dataKey="value" stroke={CHART_COLORS[0]} strokeWidth={2} dot={false} />
              </LineChart>
            ) : kind === 'donut' || kind === 'pie' ? (
              <PieChart>
                <Pie data={data} dataKey="value" nameKey="name" innerRadius={kind === 'donut' ? 55 : 0} outerRadius={90} paddingAngle={2}>
                  {data.map((_, i) => (
                    <Cell key={i} fill={CHART_COLORS[i % CHART_COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip formatter={(v: number) => valueFmt(v)} />
                <Legend wrapperStyle={{ fontSize: 11 }} />
              </PieChart>
            ) : (
              <BarChart data={data}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11 }} />
                <Tooltip formatter={(v: number) => valueFmt(v)} />
                <Bar dataKey="value" fill={CHART_COLORS[0]} radius={[4, 4, 0, 0]} />
              </BarChart>
            )}
          </ResponsiveContainer>
        </div>
      )}
    </Panel>
  )
}

/** Pantalla de inicio (`/`): indicadores y gráficos de Pulso, solo lectura. Sin `analytics.view` o sin el módulo ANALYTICS
 *  encendido en el tenant: bienvenida sin datos (no se consulta el API, así el inicio nunca redirige a 'Módulo apagado'). */
export default function Pulse() {
  const t = useT()
  const { me } = useSession()
  const moduleOn = useModule(ModuleKeys.Analytics)
  const hasPerm = useCan('analytics.view')
  const canView = moduleOn && hasPerm
  const { data, isLoading, error } = usePulse(canView)
  const name = me?.fullName ?? ''

  if (!canView) {
    const body = moduleOn ? t('analytics.pulse.welcomeBody') : t('analytics.pulse.moduleOffBody')
    return <EmptyState title={t('analytics.pulse.welcomeTitle', { name })} body={body} />
  }

  if (isLoading) return <Spinner block label={t('common.loading')} />

  if (error) {
    return <EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />
  }

  const indicators = data?.indicators ?? []
  const charts = data?.charts ?? []

  if (indicators.length === 0 && charts.length === 0) {
    return <EmptyState title={t('analytics.pulse.emptyTitle')} body={t('analytics.pulse.emptyBody')} />
  }

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('analytics.pulse.title')}</h1>
          <p>{t('analytics.pulse.subtitle', { name })}</p>
        </div>
      </div>

      {indicators.length > 0 && (
        <section style={{ marginBottom: 20 }}>
          <h2 style={{ fontSize: 14, fontWeight: 700, margin: '0 0 10px' }}>{t('analytics.pulse.indicators')}</h2>
          <div style={CARD_GRID}>
            {indicators.map((ind) => (
              <IndicatorCard key={ind.id} indicator={ind} />
            ))}
          </div>
        </section>
      )}

      {charts.length > 0 && (
        <section>
          <h2 style={{ fontSize: 14, fontWeight: 700, margin: '0 0 10px' }}>{t('analytics.pulse.charts')}</h2>
          <div style={CHART_GRID}>
            {charts.map((chart) => (
              <ChartCard key={chart.id} chart={chart} />
            ))}
          </div>
        </section>
      )}
    </div>
  )
}
