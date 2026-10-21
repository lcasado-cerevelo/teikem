// Lote 197 — los eventos de aviso agrupados por categoría para la lista desplegable de «Nueva regla». El servidor manda la categoría de cada evento (`category`); aquí se ordenan los grupos y,
// dentro de cada uno, los eventos por nombre. Un evento sin categoría cae en «Otros».
import type { components } from '../../kernel/api/schema'

type EventDto = components['schemas']['NotificationEventDto']

/** Orden de los grupos (el mismo del servidor). */
export const EVENT_CATEGORY_ORDER = ['WAREHOUSE', 'SALES', 'TRANSPORT', 'MAINTENANCE', 'BILLING', 'INTEGRATIONS', 'OTHER'] as const

export function groupEventsByCategory(events: readonly EventDto[]): Array<{ category: string; events: EventDto[] }> {
  const by = new Map<string, EventDto[]>()
  for (const e of events) {
    const c = (EVENT_CATEGORY_ORDER as readonly string[]).includes(e.category ?? '') ? (e.category as string) : 'OTHER'
    by.set(c, [...(by.get(c) ?? []), e])
  }
  return EVENT_CATEGORY_ORDER.filter((c) => by.has(c)).map((category) => ({ category, events: [...(by.get(category) ?? [])].sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '')) }))
}
