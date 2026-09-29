// Dibujo de un gráfico (Lote F8a P3, pantalla Gráficos): mismo criterio que la tarjeta de gráficos de Pulso
// (`PulseSections.tsx`) — Recharts para barra/línea/dona, y con pocos puntos una lista "etiqueta · valor" en vez de
// un gráfico que dice poco. Componente propio (no se exporta de `PulseSections.tsx`) para no acoplar esa pantalla,
// ya cubierta por las pruebas de P2, a esta pieza.
import { useMemo, type ReactNode } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, Label, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useT } from '../../kernel/i18n/useT'
import { chartKind, formatValue } from './format'

const CHART_COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#a855f7', '#84cc16', '#ec4899']
/** Con esta cantidad de puntos o menos, un gráfico dice poco: se muestra como lista "etiqueta · valor". */
export const CHART_LIST_MAX_POINTS = 3

export interface ChartVisualPoint {
  label?: string | null
  value?: number | null
}

export interface ChartVisualProps {
  chartType: string | null | undefined
  points: readonly ChartVisualPoint[] | null | undefined
  isMoney: boolean
  /** Etiqueta accesible de la lista de puntos (nombre del gráfico). */
  name?: string | null
}

/** Barra, línea o dona (con el total al centro) sobre `points`; con ≤ 3 puntos, lista; sin puntos, aviso. */
export function ChartVisual({ chartType, points, isMoney, name }: ChartVisualProps) {
  const t = useT()
  const data = useMemo(() => (points ?? []).map((p) => ({ name: String(p.label ?? ''), value: p.value ?? 0 })), [points])
  const kind = chartKind(chartType)
  const valueFmt = (v: number) => formatValue(v, isMoney)
  const total = data.reduce((sum, d) => sum + d.value, 0)

  if (data.length === 0) return <p className="pulse-muted">{t('analytics.charts.noData')}</p>

  if (data.length <= CHART_LIST_MAX_POINTS) {
    return (
      <ul className="pulse-pts" aria-label={name ?? undefined}>
        {data.map((d, i) => (
          <li key={i}>
            <span className="lbl">{d.name}</span>
            <span aria-hidden="true"> · </span>
            <b>{valueFmt(d.value)}</b>
          </li>
        ))}
      </ul>
    )
  }

  let content: ReactNode
  if (kind === 'line') {
    content = (
      <LineChart data={data}>
        <CartesianGrid strokeDasharray="3 3" />
        <XAxis dataKey="name" tick={{ fontSize: 11 }} />
        <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11 }} />
        <Tooltip formatter={(v) => valueFmt(Number(v))} />
        <Line type="monotone" dataKey="value" stroke={CHART_COLORS[0]} strokeWidth={2} dot={false} />
      </LineChart>
    )
  } else if (kind === 'donut' || kind === 'pie') {
    content = (
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
    )
  } else {
    content = (
      <BarChart data={data}>
        <CartesianGrid strokeDasharray="3 3" />
        <XAxis dataKey="name" tick={{ fontSize: 11 }} />
        <YAxis tickFormatter={valueFmt} width={64} tick={{ fontSize: 11 }} />
        <Tooltip formatter={(v) => valueFmt(Number(v))} />
        <Bar dataKey="value" fill={CHART_COLORS[0]} radius={[4, 4, 0, 0]} />
      </BarChart>
    )
  }

  return (
    <div className="pulse-chartbox">
      <ResponsiveContainer width="100%" height="100%">
        {content}
      </ResponsiveContainer>
    </div>
  )
}
