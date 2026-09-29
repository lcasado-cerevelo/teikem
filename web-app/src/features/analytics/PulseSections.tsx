// Lote F8a (P2) — secciones INDICATORS y CHARTS del Pulso, a la maqueta (teikem-mockups.html, `pulseIndicatorsHtml` y
// `pulseChartsHtml`): "Tus indicadores" como río (`.streamlabel` + `.river` + `.node flow|money`: azul cantidad, naranja
// dinero) y "Tus gráficos" en grilla. Cada tarjeta conserva el botón "Rango" de P3 (mi rango de fecha, `analytics.view`).
// Reciben solo los elementos visibles y ya ordenados (`shownItems`); sin elementos no pintan nada.
import { useMemo, useState, type ReactNode } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Label,
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
import { Link } from 'react-router-dom'
import { IconBox, IconCash, IconChart, IconLayers } from '../../app/icons'
import { Can, ModuleKeys, useCan, useModule } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Panel } from '../../kernel/ui/Panel'
import type { PulseItemKind } from './api'
import { chartKind, customRangeDays, CUSTOM_RANGE, formatValue, formatYmd } from './format'
import { IconSignal } from './pulseIcons'
import { moduleGroup, type ChartDatum, type Indicator, type ModuleGroup } from './pulseLayout'
import { RangeModal } from './RangeModal'
import './pulse.css'

const CHART_COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#a855f7', '#84cc16', '#ec4899']
/** Con esta cantidad de puntos o menos, un gráfico dice poco: se muestra como lista "etiqueta · valor". */
export const CHART_LIST_MAX_POINTS = 3
/** Destino de un clic en un nodo del río. */
export const INDICATORS_ROUTE = '/analytics/indicators'

const MODULE_ICON: Record<ModuleGroup, () => ReactNode> = { ops: IconBox, warehouse: IconLayers, money: IconCash }

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

/** Etiqueta de sección de la maqueta: ícono, texto en mayúsculas espaciadas y línea que se desvanece. */
export function StreamLabel({ icon, children, id }: { icon: ReactNode; children: ReactNode; id?: string }) {
  return (
    <h2 className="streamlabel" id={id}>
      {icon}
      <span>{children}</span>
      <span className="ln" aria-hidden="true" />
    </h2>
  )
}

/** Nodo del río: módulo (ícono) y nombre, valor y, debajo, el rango o el módulo. Todo el nodo lleva a Indicadores. */
function IndicatorNode({ indicator, canOpen }: { indicator: Indicator; canOpen: boolean }) {
  const t = useT()
  const caption = useRangeCaption(indicator.dateRangeMode, indicator.fromUtc, indicator.toUtc)
  const group = moduleGroup(indicator.businessModule)
  const ModuleIcon = MODULE_ICON[group]
  const body = (
    <>
      <div className="ph">
        <ModuleIcon />
        <span>{indicator.name}</span>
      </div>
      <div className="big">{formatValue(indicator.value, indicator.isMoney ?? false)}</div>
      <div className="sub">{caption ?? t(`nav.groups.${group}`)}</div>
    </>
  )
  return (
    <div className={indicator.isMoney ? 'node money' : 'node flow'}>
      {canOpen ? (
        <Link className="node-link" to={INDICATORS_ROUTE}>
          {body}
        </Link>
      ) : (
        <div>{body}</div>
      )}
      <div className="nact">
        <RangeButton kind="indicator" item={indicator} />
      </div>
    </div>
  )
}

/** Sección INDICATORS: "Tus indicadores" como río. Sin indicadores visibles no se pinta. */
export function IndicatorsRiver({ indicators }: { indicators: Indicator[] }) {
  const t = useT()
  // El nodo lleva a Indicadores solo si el usuario puede entrar (analytics.view + ANALYTICS); si no, es solo lectura.
  const hasPerm = useCan('analytics.view')
  const moduleOn = useModule(ModuleKeys.Analytics)
  const canOpen = hasPerm && moduleOn
  if (indicators.length === 0) return null
  return (
    <section aria-labelledby="pulse-indicators">
      <StreamLabel icon={<IconChart />} id="pulse-indicators">
        {t('analytics.pulse.panels.INDICATORS')}
      </StreamLabel>
      <div className="river">
        {indicators.map((ind, i) => (
          <IndicatorNodeWithPipe key={ind.id} indicator={ind} first={i === 0} canOpen={canOpen} />
        ))}
      </div>
    </section>
  )
}

function IndicatorNodeWithPipe({ indicator, first, canOpen }: { indicator: Indicator; first: boolean; canOpen: boolean }) {
  return (
    <>
      {!first && <div className={indicator.isMoney ? 'pipe money' : 'pipe'} aria-hidden="true" />}
      <IndicatorNode indicator={indicator} canOpen={canOpen} />
    </>
  )
}

/** Tarjeta de gráfico: Recharts; con ≤ 3 puntos, lista "etiqueta · valor"; la dona lleva el total al centro. */
function ChartCard({ chart }: { chart: ChartDatum }) {
  const t = useT()
  const caption = useRangeCaption(chart.dateRangeMode, chart.fromUtc, chart.toUtc)
  const data = useMemo(
    () => (chart.points ?? []).map((p) => ({ name: String(p.label ?? p.key ?? ''), value: p.value ?? 0 })),
    [chart.points],
  )
  const kind = chartKind(chart.chartType)
  const isMoney = chart.isMoney ?? false
  const valueFmt = (v: number) => formatValue(v, isMoney)
  const total = data.reduce((sum, d) => sum + d.value, 0)

  let content: ReactNode
  if (data.length === 0) {
    content = <p className="pulse-muted">{t('analytics.pulse.chartEmpty')}</p>
  } else if (data.length <= CHART_LIST_MAX_POINTS) {
    content = (
      <ul className="pulse-pts" aria-label={chart.name ?? undefined}>
        {data.map((d, i) => (
          <li key={i}>
            <span className="lbl">{d.name}</span>
            <span aria-hidden="true"> · </span>
            <b>{valueFmt(d.value)}</b>
          </li>
        ))}
      </ul>
    )
  } else {
    content = (
      <div className="pulse-chartbox">
        <ResponsiveContainer width="100%" height="100%">
          {kind === 'line' ? (
            <LineChart data={data}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="name" tick={{ fontSize: 11 }} />
              <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11 }} />
              <Tooltip formatter={(v) => valueFmt(Number(v))} />
              <Line type="monotone" dataKey="value" stroke={CHART_COLORS[0]} strokeWidth={2} dot={false} />
            </LineChart>
          ) : kind === 'donut' || kind === 'pie' ? (
            <PieChart>
              <Pie data={data} dataKey="value" nameKey="name" innerRadius={kind === 'donut' ? 55 : 0} outerRadius={90} paddingAngle={2}>
                {data.map((_, i) => (
                  <Cell key={i} fill={CHART_COLORS[i % CHART_COLORS.length]} />
                ))}
                {kind === 'donut' && <Label value={valueFmt(total)} position="center" fill="var(--text)" fontSize={16} fontWeight={700} />}
              </Pie>
              <Tooltip formatter={(v) => valueFmt(Number(v))} />
              <Legend wrapperStyle={{ fontSize: 11 }} />
            </PieChart>
          ) : (
            <BarChart data={data}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="name" tick={{ fontSize: 11 }} />
              <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11 }} />
              <Tooltip formatter={(v) => valueFmt(Number(v))} />
              <Bar dataKey="value" fill={CHART_COLORS[0]} radius={[4, 4, 0, 0]} />
            </BarChart>
          )}
        </ResponsiveContainer>
      </div>
    )
  }

  return (
    <Panel className="pulse-chart" title={chart.name} subtitle={caption ?? undefined} actions={<RangeButton kind="chart" item={chart} />}>
      {content}
    </Panel>
  )
}

/** Sección CHARTS: "Tus gráficos" en grilla (una columna a 360 px). Sin gráficos visibles no se pinta. */
export function ChartsGrid({ charts }: { charts: ChartDatum[] }) {
  const t = useT()
  if (charts.length === 0) return null
  return (
    <section aria-labelledby="pulse-charts">
      <StreamLabel icon={<IconSignal />} id="pulse-charts">
        {t('analytics.pulse.panels.CHARTS')}
      </StreamLabel>
      <div className="pulse-charts">
        {charts.map((chart) => (
          <ChartCard key={chart.id} chart={chart} />
        ))}
      </div>
    </section>
  )
}
