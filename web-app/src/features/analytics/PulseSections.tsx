// Lote F8a (P2) — secciones INDICATORS y CHARTS del Pulso, a la maqueta (teikem-mockups.html, `pulseIndicatorsHtml` y
// `pulseChartsHtml`): "Tus indicadores" como río (`.streamlabel` + `.river` + `.node flow|money`: azul cantidad, naranja
// dinero) y "Tus gráficos" en grilla. Cada tarjeta conserva el botón "Rango" de P3 (mi rango de fecha, `analytics.view`).
// Reciben solo los elementos visibles y ya ordenados (`shownItems`); sin elementos no pintan nada.
// Lote 15: los indicadores van en una línea por módulo (D8, `indicatorLines`: Operación, Almacén, Contabilidad), cada una con
// su etiqueta h3 (nunca h2: los recorridos buscan la sección con h2 "Almacén" del panel Almacén); los gráficos se dibujan
// SIEMPRE con `ChartVisual` y la grilla pone como mucho 2 por fila (una columna en celular).
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { IconBox, IconCash, IconChart, IconLayers } from '../../app/icons'
import { Can, ModuleKeys, useCan, useModule } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Panel } from '../../kernel/ui/Panel'
import type { PulseItemKind } from './api'
import { ChartVisual } from './ChartVisual'
import { customRangeDays, CUSTOM_RANGE, formatValue, formatYmd } from './format'
import { IconSignal } from './pulseIcons'
import { indicatorLines, moduleGroup, type ChartDatum, type Indicator, type ModuleGroup } from './pulseLayout'
import { RangeModal } from './RangeModal'
import './pulse.css'

/** Destino de un clic en un nodo del río. */
export const INDICATORS_ROUTE = '/analytics/indicators'

/**
 * Columnas de "Tus gráficos" (Lote 15, hallazgo 17 y D13): nunca más de 2 por fila (cada columna mide al menos la mitad
 * menos el hueco de 16 px) y una sola por debajo de 776 px (380 × 2 + 16) o a 360 px. No aplica a Análisis → Gráficos.
 */
export const PULSE_CHART_COLUMNS = 'repeat(auto-fill, minmax(min(100%, max(380px, calc(50% - 8px))), 1fr))'

const MODULE_ICON: Record<ModuleGroup, () => ReactNode> = { ops: IconBox, warehouse: IconLayers, money: IconCash }
/** Tono de la etiqueta de cada línea: azul Operación, violeta Almacén, naranja Contabilidad. */
const LINE_TONE: Record<ModuleGroup, 'flow' | 'wh' | 'money'> = { ops: 'flow', warehouse: 'wh', money: 'money' }

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

export interface StreamLabelProps {
  icon: ReactNode
  children: ReactNode
  id?: string
  /** Nivel del encabezado: 2 (sección, por defecto) o 3 (línea dentro de una sección). */
  level?: 2 | 3
  /** Color del ícono (líneas de indicadores): azul, violeta o naranja; sin tono, gris. */
  tone?: 'flow' | 'wh' | 'money'
}

/** Etiqueta de sección de la maqueta: ícono, texto en mayúsculas espaciadas y línea que se desvanece. */
export function StreamLabel({ icon, children, id, level = 2, tone }: StreamLabelProps) {
  const Tag = level === 3 ? 'h3' : 'h2'
  const className = ['streamlabel', level === 3 ? 'line' : '', tone ?? ''].filter(Boolean).join(' ')
  return (
    <Tag className={className} id={id}>
      {icon}
      <span>{children}</span>
      <span className="ln" aria-hidden="true" />
    </Tag>
  )
}

/** Nodo del río: módulo (ícono) y nombre, valor y, debajo, el rango (el módulo ya lo dice la línea). Todo el nodo lleva a Indicadores. */
function IndicatorNode({ indicator, canOpen }: { indicator: Indicator; canOpen: boolean }) {
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
      {caption && <div className="sub">{caption}</div>}
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

/**
 * Sección INDICATORS: "Tus indicadores" (h2) con un río por línea de módulo (Lote 15, D8): cada línea lleva su etiqueta h3
 * ("Operación", "Almacén", "Contabilidad") y sus indicadores en el orden de siempre. Sin indicadores visibles no se pinta.
 */
/** Máximo de tarjetas por fila en cada línea de indicadores (pedido de Luis, 2026-10-01): la sexta pasa a otra fila. */
export const INDICATORS_PER_ROW = 5

/** Parte una lista en filas de `size` (la última puede quedar más corta). */
export function chunkRows<T>(items: readonly T[], size = INDICATORS_PER_ROW): T[][] {
  const rows: T[][] = []
  for (let i = 0; i < items.length; i += size) rows.push(items.slice(i, i + size))
  return rows
}

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
      {indicatorLines(indicators).map((line) => {
        const LineIcon = MODULE_ICON[line.group]
        return (
          <div key={line.group} className="pulse-line" data-line={line.group}>
            <StreamLabel icon={<LineIcon />} id={`pulse-line-${line.group}`} level={3} tone={LINE_TONE[line.group]}>
              {t(`nav.groups.${line.group}`)}
            </StreamLabel>
            {/* hasta 5 por fila; cada fila es su propio río (la línea punteada no cruza de una fila a otra) */}
            {chunkRows(line.items).map((row, r) => (
              <div key={r} className="river river-5">
                {row.map((ind, i) => (
                  <IndicatorNodeWithPipe key={ind.id} indicator={ind} first={i === 0} canOpen={canOpen} />
                ))}
              </div>
            ))}
          </div>
        )
      })}
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

/** Tarjeta de gráfico: siempre el gráfico (`ChartVisual`, Lote 15), con el aviso del Pulso si no hay puntos. */
function ChartCard({ chart }: { chart: ChartDatum }) {
  const t = useT()
  const caption = useRangeCaption(chart.dateRangeMode, chart.fromUtc, chart.toUtc)
  const ModuleIcon = MODULE_ICON[moduleGroup(chart.businessModule)]
  return (
    <Panel className="pulse-chart" icon={<ModuleIcon />} title={chart.name} subtitle={caption ?? undefined} actions={<RangeButton kind="chart" item={chart} />}>
      <ChartVisual chartType={chart.chartType} points={chart.points} isMoney={chart.isMoney ?? false} name={chart.name} emptyText={t('analytics.pulse.chartEmpty')} />
    </Panel>
  )
}

/** Sección CHARTS: "Tus gráficos" en grilla de 2 por fila como mucho (una columna en celular). Sin gráficos visibles no se pinta. */
export function ChartsGrid({ charts }: { charts: ChartDatum[] }) {
  const t = useT()
  if (charts.length === 0) return null
  return (
    <section aria-labelledby="pulse-charts">
      <StreamLabel icon={<IconSignal />} id="pulse-charts">
        {t('analytics.pulse.panels.CHARTS')}
      </StreamLabel>
      <div className="pulse-charts" style={{ gridTemplateColumns: PULSE_CHART_COLUMNS }}>
        {charts.map((chart) => (
          <ChartCard key={chart.id} chart={chart} />
        ))}
      </div>
    </section>
  )
}
