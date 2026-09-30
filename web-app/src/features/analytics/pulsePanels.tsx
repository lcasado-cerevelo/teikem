// Lote F8a (P2) — registro de paneles del Pulso en el frontend (espejo de `PulsePanels` del dominio, §2.3 del plan).
// La pantalla pinta EXACTAMENTE los paneles que devuelve `GET /api/v1/analytics/pulse`, en su `sortOrder`, omitiendo los
// ocultos y las claves que este registro no conoce (compatibilidad hacia adelante); uno que el registro conoce y el API no
// devuelve no se pinta (permiso o módulo). Ningún panel se monta a mano fuera de aquí.
// Un panel nuevo (F3, F5, 7C): su clave en `PULSE_PANEL_KEYS` (pulseLayout.ts), su entrada aquí y su título en i18n.
import type { ReactNode } from 'react'
import { ActivityPanel } from './ActivityPanel'
import { AttentionPanel } from './AttentionPanel'
import { ChartsGrid, IndicatorsRiver } from './PulseSections'
import type { ChartDatum, Indicator, PulseDto, PulsePanelKey } from './pulseLayout'
import { WarehouseDayBand } from './WarehouseDayBand'
import { WarehousePulsePanel } from './WarehousePulsePanel'

/** Lo que recibe el `render` de cada panel: el Pulso completo y los elementos visibles ya ordenados. */
export interface PulsePanelContext {
  pulse: PulseDto
  indicators: Indicator[]
  charts: ChartDatum[]
}

export interface PulsePanelEntry {
  key: PulsePanelKey
  /** Título del panel (clave i18n): lo usa el modo Organizar. */
  titleKey: string
  /** Contenido del panel; null = la sección no se pinta (p. ej. INDICATORS sin elementos visibles). */
  render: (ctx: PulsePanelContext) => ReactNode
  /** Qué elementos ordena el modo Organizar dentro del panel (solo INDICATORS y CHARTS). */
  items?: 'indicators' | 'charts'
  /** true si con este contexto el panel tiene algo que mostrar (los de F7A siempre; los de elementos, si hay alguno). */
  hasContent: (ctx: PulsePanelContext) => boolean
  /**
   * Lote 15 (D6): franja de números que puede quedar FIJA al desplazarse. Solo se fija si es la primera sección pintada
   * (justo debajo de la fecha) y no se está organizando; nunca un panel alto (Actividad reciente, gráficos).
   */
  pinnable?: boolean
}

const always = () => true

export const PULSE_PANELS: Record<PulsePanelKey, PulsePanelEntry> = {
  INDICATORS: {
    key: 'INDICATORS',
    titleKey: 'analytics.pulse.panels.INDICATORS',
    items: 'indicators',
    hasContent: (ctx) => ctx.indicators.length > 0,
    render: (ctx) => <IndicatorsRiver indicators={ctx.indicators} />,
  },
  CHARTS: {
    key: 'CHARTS',
    titleKey: 'analytics.pulse.panels.CHARTS',
    items: 'charts',
    hasContent: (ctx) => ctx.charts.length > 0,
    render: (ctx) => <ChartsGrid charts={ctx.charts} />,
  },
  WAREHOUSE: {
    key: 'WAREHOUSE',
    titleKey: 'analytics.pulse.warehouse.title',
    hasContent: always,
    render: () => <WarehousePulsePanel />,
  },
  ACTIVITY: {
    key: 'ACTIVITY',
    titleKey: 'analytics.activity.title',
    hasContent: always,
    render: () => <ActivityPanel />,
  },
  // Lote 14 (D6): "Necesita tu atención" (pulse.attention; orden 5 del dominio). Siempre se pinta: sin pendientes dice
  // "Todo en orden".
  ATTENTION: {
    key: 'ATTENTION',
    titleKey: 'analytics.pulse.panels.ATTENTION',
    hasContent: always,
    render: () => <AttentionPanel />,
  },
  // Lote 15 (P4, D1–D6): franja "Almacén hoy" (pulse.warehouse + inventory.view + WMS_LOTSERIAL; orden −10 del dominio, la
  // fila siguiente a la fecha). Siempre se pinta (sin movimiento, ceros y barritas vacías) y es fijable.
  WAREHOUSE_DAY: {
    key: 'WAREHOUSE_DAY',
    titleKey: 'analytics.pulse.panels.WAREHOUSE_DAY',
    hasContent: always,
    pinnable: true,
    render: () => <WarehouseDayBand />,
  },
}

/** Clave del panel que queda fijo bajo la fecha (D6): la primera sección pintada si es fijable y no se organiza; si no, null. */
export function pinnedPanelKey(sections: readonly { key: PulsePanelKey }[], organizing: boolean): PulsePanelKey | null {
  const first = sections[0]
  return !organizing && first && PULSE_PANELS[first.key].pinnable ? first.key : null
}
