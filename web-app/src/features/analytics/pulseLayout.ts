// Lote F8a (P2) — lógica pura del Pulso por paneles: qué paneles y elementos se pintan (orden y ocultos que ya resolvió el
// servidor) y el estado editable del modo Organizar con el cuerpo del `PUT /api/v1/analytics/pulse/layout?scope=…`.
// Sin React: se prueba sola y la comparten `Pulse.tsx`, `pulsePanels.tsx` y `PulseOrganizer.tsx`.
import type { components } from '../../kernel/api/schema'

export type PulseDto = components['schemas']['PulseDto']
export type PulsePanelDto = components['schemas']['PulsePanelDto']
export type PulseLayoutRequest = components['schemas']['PulseLayoutRequest']
export type Indicator = components['schemas']['IndicatorValueDto']
export type ChartDatum = components['schemas']['ChartDataDto']

/** Alcance de un orden guardado: el mío (sin permiso) o el de la compañía (`pulse.organize_company`). */
export type PulseScope = 'mine' | 'company'

/** Paneles que este frontend sabe pintar (registro `PulsePanels` del dominio). Uno nuevo = una clave aquí y en `pulsePanels.tsx`.
 *  Lote 15: WAREHOUSE_DAY (franja "Almacén hoy", orden −10 del dominio: la fila siguiente a la fecha). */
export const PULSE_PANEL_KEYS = ['INDICATORS', 'CHARTS', 'WAREHOUSE', 'ACTIVITY', 'ATTENTION', 'WAREHOUSE_DAY'] as const
export type PulsePanelKey = (typeof PULSE_PANEL_KEYS)[number]

export function isKnownPanel(key: string | null | undefined): key is PulsePanelKey {
  return (PULSE_PANEL_KEYS as readonly string[]).includes(key ?? '')
}

const byName = new Intl.Collator('es', { sensitivity: 'base', numeric: true })

/** Paneles en el orden efectivo (`sortOrder`, luego clave, como el servidor). No filtra. */
export function sortPanels(panels: readonly PulsePanelDto[] | null | undefined): PulsePanelDto[] {
  return [...(panels ?? [])].sort(
    (a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || ((a.key ?? '') < (b.key ?? '') ? -1 : (a.key ?? '') > (b.key ?? '') ? 1 : 0),
  )
}

/** Indicadores o gráficos en el orden efectivo (`sortOrder`, luego nombre sin mayúsculas, como el servidor). No filtra. */
export function sortItems<T extends { sortOrder?: number; name?: string | null }>(items: readonly T[] | null | undefined): T[] {
  return [...(items ?? [])].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || byName.compare(a.name ?? '', b.name ?? ''))
}

/** Paneles que la pantalla pinta: los que devolvió el API, visibles, que este frontend conoce, en su orden. */
export function shownPanels(panels: readonly PulsePanelDto[] | null | undefined): (PulsePanelDto & { key: PulsePanelKey })[] {
  return sortPanels(panels).filter((p): p is PulsePanelDto & { key: PulsePanelKey } => p.isVisible !== false && isKnownPanel(p.key))
}

/** Elementos visibles de un panel INDICATORS/CHARTS, en su orden. */
export function shownItems<T extends { sortOrder?: number; name?: string | null; isVisible?: boolean }>(items: readonly T[] | null | undefined): T[] {
  return sortItems(items).filter((i) => i.isVisible !== false)
}

// ---------------------------------------------------------------------------------------------------------------------
// Modo Organizar: copia local editable. Los paneles que este frontend no conoce no se ofrecen ni se envían (el servidor no
// toca lo que no viene en el cuerpo).
// ---------------------------------------------------------------------------------------------------------------------

export interface OrganizerPanel {
  key: PulsePanelKey
  isVisible: boolean
}

export interface OrganizerItem {
  id: number
  name: string
  isMoney: boolean
  businessModule: string | null
  isVisible: boolean
}

export interface OrganizerState {
  panels: OrganizerPanel[]
  indicators: OrganizerItem[]
  charts: OrganizerItem[]
}

/** Lista que se reordena: los paneles o los elementos de uno de los dos paneles con elementos. */
export type OrganizerList = 'panels' | 'indicators' | 'charts'

function toItem(i: Indicator | ChartDatum): OrganizerItem | null {
  if (i.id == null) return null
  return { id: i.id, name: i.name ?? '', isMoney: i.isMoney ?? false, businessModule: i.businessModule ?? null, isVisible: i.isVisible !== false }
}

/**
 * Estado inicial del modo Organizar a partir del Pulso cargado (incluye los ocultos, en su sitio). Los indicadores van
 * agrupados por línea (Lote 15, D8: Operación, Almacén, Contabilidad) y, dentro de cada una, en su orden: así los ve la
 * pantalla y así se ordenan (solo dentro de su línea, ver `sameLine`).
 */
export function initOrganizer(pulse: PulseDto): OrganizerState {
  return {
    panels: sortPanels(pulse.panels)
      .filter((p) => isKnownPanel(p.key))
      .map((p) => ({ key: p.key as PulsePanelKey, isVisible: p.isVisible !== false })),
    indicators: indicatorLines(sortItems(pulse.indicators).flatMap((i) => toItem(i) ?? [])).flatMap((l) => l.items),
    charts: sortItems(pulse.charts).flatMap((c) => toItem(c) ?? []),
  }
}

/** Copia de `list` con el elemento de `from` en la posición `to` (fuera de rango = sin cambios). */
export function moveInList<T>(list: readonly T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= list.length || to >= list.length) return [...list]
  const next = [...list]
  const [moved] = next.splice(from, 1)
  next.splice(to, 0, moved)
  return next
}

/**
 * true si las posiciones `a` y `b` de la lista se pueden intercambiar: siempre en paneles y gráficos; en indicadores, solo
 * dentro de la misma línea de módulo (Lote 15, D8: "Organizar ordena dentro de su fila").
 */
export function sameLine(state: OrganizerState, list: OrganizerList, a: number, b: number): boolean {
  if (list !== 'indicators') return true
  const x = state.indicators[a]
  const y = state.indicators[b]
  return x != null && y != null && moduleGroup(x.businessModule) === moduleGroup(y.businessModule)
}

/** Mueve un panel o elemento dentro de su lista (un indicador, solo dentro de su línea; si no, sin cambios). */
export function moveEntry(state: OrganizerState, list: OrganizerList, from: number, to: number): OrganizerState {
  if (list === 'panels') return { ...state, panels: moveInList(state.panels, from, to) }
  if (!sameLine(state, list, from, to)) return state
  return { ...state, [list]: moveInList(state[list], from, to) }
}

/** Oculta o vuelve a mostrar un panel o elemento (se queda en su sitio). */
export function toggleEntry(state: OrganizerState, list: OrganizerList, index: number): OrganizerState {
  if (list === 'panels') return { ...state, panels: state.panels.map((p, i) => (i === index ? { ...p, isVisible: !p.isVisible } : p)) }
  return { ...state, [list]: state[list].map((it, i) => (i === index ? { ...it, isVisible: !it.isVisible } : it)) }
}

/** Separación entre posiciones al guardar: orden = índice × 10 (deja hueco para insertar sin renumerar). */
export const SORT_STEP = 10

/** Cuerpo del PUT: TODOS los paneles y elementos, con el orden de la pantalla (índice × 10) y su visibilidad. */
export function buildLayoutRequest(state: OrganizerState): PulseLayoutRequest {
  return {
    panels: state.panels.map((p, i) => ({ key: p.key, sortOrder: i * SORT_STEP, isVisible: p.isVisible })),
    items: [
      ...state.indicators.map((it, i) => ({ kind: 'indicator', id: it.id, sortOrder: i * SORT_STEP, isVisible: it.isVisible })),
      ...state.charts.map((it, i) => ({ kind: 'chart', id: it.id, sortOrder: i * SORT_STEP, isVisible: it.isVisible })),
    ],
  }
}

/** Grupo del menú (y su ícono) que corresponde al módulo de negocio de un indicador o gráfico (`BusinessModule`). */
export type ModuleGroup = 'ops' | 'warehouse' | 'money'

export function moduleGroup(businessModule: string | null | undefined): ModuleGroup {
  switch ((businessModule ?? '').toUpperCase()) {
    case 'WAREHOUSE':
      return 'warehouse'
    case 'ACCOUNTING':
      return 'money'
    default:
      return 'ops'
  }
}

/** Orden de las líneas de indicadores: el del menú (Lote 15, D8). */
export const INDICATOR_LINE_ORDER: readonly ModuleGroup[] = ['ops', 'warehouse', 'money']

export interface IndicatorLine<T> {
  group: ModuleGroup
  items: T[]
}

/**
 * "Tus indicadores" en una línea por módulo (Lote 15, D8): en el orden del menú (Operación, Almacén, Contabilidad), solo las
 * líneas con algún elemento, y dentro de cada una el orden recibido (el de siempre). Sin módulo o con uno desconocido = Operación.
 */
export function indicatorLines<T extends { businessModule?: string | null }>(items: readonly T[]): IndicatorLine<T>[] {
  return INDICATOR_LINE_ORDER.map((group) => ({ group, items: items.filter((i) => moduleGroup(i.businessModule) === group) })).filter(
    (l) => l.items.length > 0,
  )
}
