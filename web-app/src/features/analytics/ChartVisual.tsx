// Dibujo de un gráfico: el ÚNICO componente que pinta un gráfico de Indicadores/Gráficos (tarjeta de Análisis → Gráficos,
// vista previa del editor y tarjeta de "Tus gráficos" del Pulso). Recharts para barra, línea, dona y pastel.
// Lote 15 (regla del dueño): SIEMPRE se dibuja como gráfico, tenga los puntos que tenga (antes, con ≤ 3 puntos, una lista);
// sin puntos, el aviso. Una línea con pocos puntos (≤ 12) pinta sus puntos, así un solo día se ve. El tooltip muestra la
// fecha larga (si la etiqueta es un día 'YYYY-MM-DD') y el valor con su formato; la dona lleva el total al centro.
// `size="mini"`: sin ejes, rejilla ni leyenda (barritas de una tarjeta; altura por CSS), `minPointSize` para que un día en
// 0 se vea. Cada punto puede traer su color. El envoltorio es `role="img"` con los valores en `aria-label` (el tooltip solo
// sale con el ratón).
import { useMemo, type ReactNode } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, Label, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useLang, useT } from '../../kernel/i18n/useT'
import { chartKind, donutCenter, formatValue, isDayLabel, longDay, shortDay } from './format'

const CHART_COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#a855f7', '#84cc16', '#ec4899']
/** Una línea con esta cantidad de puntos o menos pinta cada punto (un solo día sería invisible sin él). */
export const LINE_DOTS_MAX_POINTS = 12

export interface ChartVisualPoint {
  label?: string | null
  /** Clave del motor (p. ej. "$others" para "Otras"); si no hay etiqueta, se muestra la clave. */
  key?: unknown
  value?: number | null
  /** Color propio del punto (barra o rebanada); sin él, la paleta. */
  color?: string | null
}

export interface ChartVisualProps {
  chartType: string | null | undefined
  points: readonly ChartVisualPoint[] | null | undefined
  isMoney: boolean
  /** Nombre del gráfico: encabeza la etiqueta accesible con los valores. */
  name?: string | null
  /** Aviso sin puntos (por defecto `analytics.charts.noData`). */
  emptyText?: string
  /** 'card' (por defecto) o 'mini' (sin ejes, rejilla ni leyenda). */
  size?: 'card' | 'mini'
  /** Nombre de la serie en el tooltip (p. ej. "unidades"); por defecto "Valor". */
  unit?: string
  /** Texto del encabezado del tooltip para el punto `i` (por defecto la etiqueta, o la fecha larga si es un día). */
  tooltipLabel?: (label: string, index: number) => string
}

interface Datum {
  name: string
  value: number
  color: string | null
}

const TOOLTIP_STYLE = { background: 'var(--panel)', border: '1px solid var(--line)', borderRadius: 8, color: 'var(--text)', fontSize: 12 }

/** Barra, línea, dona (con el total al centro) o pastel sobre `points`; sin puntos, aviso. */
export function ChartVisual({ chartType, points, isMoney, name, emptyText, size = 'card', unit, tooltipLabel }: ChartVisualProps) {
  const t = useT()
  const lang = useLang()
  const data = useMemo<Datum[]>(
    () => (points ?? []).map((p) => ({ name: String(p.label ?? p.key ?? ''), value: p.value ?? 0, color: p.color ?? null })),
    [points],
  )
  const kind = chartKind(chartType)
  const mini = size === 'mini'
  const valueFmt = (v: number) => formatValue(v, isMoney)
  const seriesName = unit ?? t('analytics.charts.value')

  if (data.length === 0) return <p className="pulse-muted">{emptyText ?? t('analytics.charts.noData')}</p>

  const total = data.reduce((sum, d) => sum + d.value, 0)
  const labelOf = (label: string, index: number) => (tooltipLabel ? tooltipLabel(label, index) : isDayLabel(label) ? longDay(label, lang) : label)
  const tickOf = (label: unknown) => {
    const s = String(label ?? '')
    return isDayLabel(s) ? shortDay(s, lang) : s
  }
  const colorOf = (d: Datum, i: number) => d.color ?? CHART_COLORS[i % CHART_COLORS.length]
  const perPointColor = data.some((d) => d.color)
  const tooltip = (
    <Tooltip
      contentStyle={TOOLTIP_STYLE}
      cursor={kind === 'line' ? { stroke: 'var(--line)' } : { fill: 'rgba(127,127,127,.12)' }}
      formatter={(v, n) => [valueFmt(Number(v)), kind === 'donut' || kind === 'pie' ? n : seriesName]}
      labelFormatter={(label, payload) => {
        const s = String(label ?? '')
        const index = data.findIndex((d) => d === payload?.[0]?.payload)
        return labelOf(s, index < 0 ? data.findIndex((d) => d.name === s) : index)
      }}
    />
  )
  const axes = !mini && (
    <>
      <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" />
      <XAxis dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted)' }} tickFormatter={tickOf} stroke="var(--line)" />
      <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11, fill: 'var(--muted)' }} stroke="var(--line)" />
    </>
  )

  let content: ReactNode
  if (kind === 'line') {
    const dots = data.length <= LINE_DOTS_MAX_POINTS
    content = (
      <LineChart data={data} accessibilityLayer={false} margin={mini ? { top: 4, right: 4, bottom: 4, left: 4 } : undefined}>
        {axes}
        {mini && <XAxis dataKey="name" hide />}
        {tooltip}
        <Line
          type="monotone"
          dataKey="value"
          name={seriesName}
          stroke={CHART_COLORS[0]}
          strokeWidth={2}
          dot={dots ? { r: mini ? 2.5 : 4, fill: CHART_COLORS[0], strokeWidth: 0 } : false}
          activeDot={{ r: mini ? 3.5 : 5 }}
          isAnimationActive={!mini}
        />
      </LineChart>
    )
  } else if (kind === 'donut' || kind === 'pie') {
    content = (
      <PieChart accessibilityLayer={false}>
        <Pie
          data={data}
          dataKey="value"
          nameKey="name"
          innerRadius={kind === 'donut' ? (mini ? '55%' : 55) : 0}
          outerRadius={mini ? '100%' : 90}
          paddingAngle={data.length > 1 ? 2 : 0}
          // borde del color del panel entre rebanadas (tema claro y oscuro); una rebanada sola, sin la raya del corte
          stroke={data.length > 1 ? 'var(--panel)' : 'none'}
          isAnimationActive={!mini}
        >
          {data.map((d, i) => (
            <Cell key={i} fill={colorOf(d, i)} />
          ))}
          {kind === 'donut' && !mini && <Label value={donutCenter(total, isMoney)} position="center" fill="var(--text)" fontSize={16} fontWeight={700} />}
        </Pie>
        {tooltip}
        {!mini && <Legend wrapperStyle={{ fontSize: 11 }} />}
      </PieChart>
    )
  } else {
    content = (
      <BarChart data={data} accessibilityLayer={false} margin={mini ? { top: 2, right: 0, bottom: 0, left: 0 } : undefined} barCategoryGap={mini ? '18%' : undefined}>
        {axes}
        {mini && <XAxis dataKey="name" hide />}
        {tooltip}
        <Bar dataKey="value" name={seriesName} fill={CHART_COLORS[0]} radius={mini ? [2, 2, 0, 0] : [4, 4, 0, 0]} minPointSize={mini ? 2 : undefined} isAnimationActive={!mini}>
          {perPointColor && data.map((d, i) => <Cell key={i} fill={colorOf(d, i)} />)}
        </Bar>
      </BarChart>
    )
  }

  const values = data.map((d) => `${isDayLabel(d.name) ? longDay(d.name, lang) : d.name}: ${valueFmt(d.value)}`).join('; ')
  const aria = name ? t('analytics.charts.ariaLabel', { name, values }) : values
  return (
    <div className={mini ? 'pulse-chartbox mini' : 'pulse-chartbox'} role="img" aria-label={aria} data-chart-kind={kind} data-points={data.length}>
      <ResponsiveContainer width="100%" height="100%">
        {content}
      </ResponsiveContainer>
    </div>
  )
}
