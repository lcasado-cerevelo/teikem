// P3 — Pulso del día (inicio, solo lectura): GET /api/v1/analytics/pulse.
// Tarjetas de indicadores (un solo número) y gráficos (Recharts) marcados por cada usuario para Pulso.
// El servidor calcula el valor con el rango de fecha de cada tarjeta; el botón "Rango" de la tarjeta cambia MI rango
// (preferencia por usuario, PUT .../my-date-range con solo analytics.view) y Pulso se recalcula.
// Lote F6: debajo de lo que calcula el API, un panel 'Almacén' (solo con inventory.view y el módulo WMS_LOTSERIAL) con
// tarjetas calculadas en cliente: saldo en mano y disponible, recibos abiertos, tareas pendientes por tipo y conteos
// abiertos. Son saldo actual: sin selector de rango.
import { useMemo, useState, type CSSProperties, type ReactNode } from 'react'
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
import { PULSE_TASK_TYPES, usePulse, useWarehousePulse, type PulseItemKind } from './api'
import { chartKind, customRangeDays, CUSTOM_RANGE, formatValue, formatYmd } from './format'
import { RangeModal } from './RangeModal'

type Indicator = components['schemas']['IndicatorValueDto']
type ChartDatum = components['schemas']['ChartDataDto']

const CHART_COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#a855f7', '#84cc16', '#ec4899']
const CARD_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16 }
const CHART_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 16 }
// Tarjetas del panel 'Almacén': una columna a 360 px (min(100%, …) evita que la columna mínima desborde el panel).
const TILE_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 200px), 1fr))', gap: 12 }
const TILE: CSSProperties = { border: '1px solid var(--line)', borderRadius: 'var(--radius-sm)', padding: '12px 14px', minWidth: 0 }
const TILE_LABEL: CSSProperties = { fontSize: 12.5, color: 'var(--muted)' }
const TILE_VALUE: CSSProperties = { fontSize: 26, fontWeight: 800, lineHeight: 1.2 }

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

/** Tarjeta simple del panel 'Almacén' (etiqueta y número). */
function WarehouseTile({ label, value, children }: { label: string; value: string; children?: ReactNode }) {
  return (
    <div role="group" aria-label={label} style={TILE}>
      <div style={TILE_LABEL}>{label}</div>
      <div style={TILE_VALUE}>{value}</div>
      {children}
    </div>
  )
}

/** Número de una tarjeta de almacén: '…' mientras carga, '—' si la consulta falló (p. ej. 403). */
function tileValue(value: number | null | undefined, loading: boolean): string {
  return loading ? '…' : formatValue(value, false)
}

/** Panel 'Almacén' de Pulso: se monta solo con inventory.view y WMS_LOTSERIAL (lo decide Pulse). */
function WarehousePulsePanel() {
  const t = useT()
  const { balances, openReceipts, pendingTasks, tasksByType, openCounts } = useWarehousePulse(true)
  const { data: taskTypes = [] } = useLookups('WarehouseTaskType', { includeDisabled: true })
  const typeLabel = (code: string) => taskTypes.find((o) => o.code === code)?.label ?? code

  return (
    <Panel title={t('analytics.pulse.warehouse.title')} subtitle={t('analytics.pulse.warehouse.subtitle')}>
      <div style={TILE_GRID}>
        <WarehouseTile label={t('analytics.pulse.warehouse.onHand')} value={tileValue(balances.data?.totalOnHand, balances.isLoading)} />
        <WarehouseTile label={t('analytics.pulse.warehouse.available')} value={tileValue(balances.data?.totalAvailable, balances.isLoading)} />
        <WarehouseTile label={t('analytics.pulse.warehouse.openReceipts')} value={tileValue(openReceipts.data?.total, openReceipts.isLoading)} />
        <WarehouseTile label={t('analytics.pulse.warehouse.pendingTasks')} value={tileValue(pendingTasks.data?.total, pendingTasks.isLoading)}>
          <ul style={{ listStyle: 'none', margin: '8px 0 0', padding: 0, fontSize: 13 }}>
            {PULSE_TASK_TYPES.map((type, i) => {
              const q = tasksByType[i]
              return (
                <li key={type} style={{ display: 'flex', justifyContent: 'space-between', gap: 8, minWidth: 0 }}>
                  <span style={{ color: 'var(--muted)', overflowWrap: 'anywhere' }}>{typeLabel(type)}</span>
                  <strong>{tileValue(q?.data?.total, q?.isLoading ?? false)}</strong>
                </li>
              )
            })}
          </ul>
        </WarehouseTile>
        <WarehouseTile label={t('analytics.pulse.warehouse.openCounts')} value={tileValue(openCounts.data?.length, openCounts.isLoading)} />
      </div>
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
  const warehouseOn = useModule(ModuleKeys.WmsLotSerial)
  const canViewInventory = useCan('inventory.view')
  const warehouse = warehouseOn && canViewInventory ? (
    <section style={{ marginTop: 20 }}>
      <WarehousePulsePanel />
    </section>
  ) : null

  // Sin datos del API (bienvenida, carga, error o vacío): el mensaje y, debajo, el panel de almacén si aplica.
  const alone = (content: ReactNode) =>
    warehouse ? (
      <div className="wrap">
        {content}
        {warehouse}
      </div>
    ) : (
      content
    )

  if (!canView) {
    const body = moduleOn ? t('analytics.pulse.welcomeBody') : t('analytics.pulse.moduleOffBody')
    return alone(<EmptyState title={t('analytics.pulse.welcomeTitle', { name })} body={body} />)
  }

  if (isLoading) return alone(<Spinner block label={t('common.loading')} />)

  if (error) {
    return alone(<EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />)
  }

  const indicators = data?.indicators ?? []
  const charts = data?.charts ?? []

  if (indicators.length === 0 && charts.length === 0) {
    return alone(<EmptyState title={t('analytics.pulse.emptyTitle')} body={t('analytics.pulse.emptyBody')} />)
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

      {warehouse}
    </div>
  )
}
