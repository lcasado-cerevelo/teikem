// Lote F8a (P2) — registro de paneles del Pulso en el frontend (espejo de `PulsePanels` del dominio, §2.3 del plan).
// La pantalla pinta EXACTAMENTE los paneles que devuelve `GET /api/v1/analytics/pulse`, en su `sortOrder`, omitiendo los
// ocultos y las claves que este registro no conoce (compatibilidad hacia adelante); uno que el registro conoce y el API no
// devuelve no se pinta (permiso o módulo). Ningún panel se monta a mano fuera de aquí.
// Un panel nuevo (F3, F5, 7C): su clave en `PULSE_PANEL_KEYS` (pulseLayout.ts), su entrada aquí y su título en i18n.
import type { ReactNode } from 'react'
import { ActivityPanel } from './ActivityPanel'
import { ChartsGrid, IndicatorsRiver } from './PulseSections'
import type { ChartDatum, Indicator, PulseDto, PulsePanelKey } from './pulseLayout'
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
}
