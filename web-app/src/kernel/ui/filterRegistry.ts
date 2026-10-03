// Registro de filtros aplicados (pedido del dueño del producto, 2026-09-30): los controles de filtro del kit (`SelectFilter`,
// `SearchSelect`, `DateRangeFilter`, `QBox`, y los filtros de almacén que usan `useRegisterFilter`) anotan aquí su etiqueta
// y su valor LEGIBLE mientras están montados; la exportación de las tablas del mismo ámbito (`FilterScope`) lo lee al
// exportar y pone la oración "Filtros: Almacén ALM-DEPOT · Estatus Recibiendo · Creado del 01/09/2026 al 30/09/2026" en el
// PDF y en el Excel. Este módulo es puro (sin React): el registro, cómo se escribe cada valor y la oración.
import { formatDate } from '../format/format'

/** Un filtro con valor: `value` '' = solo la etiqueta (interruptor encendido: "Solo manuales"). */
export interface AppliedFilter {
  label: string
  value: string
}

/** Lo que hay en un ámbito al exportar: cuántos controles de filtro están montados y cuáles tienen valor (en orden). */
export interface FilterSnapshot {
  controls: number
  applied: AppliedFilter[]
}

export interface FilterRegistration {
  label: string
  /** Texto legible del valor; null = sin filtro (vacío o "Todos"); '' = solo la etiqueta. */
  value: string | null
  /** Elemento del control: ordena la oración como la barra (orden del documento). */
  element?: Element | null
}

export interface FilterRegistry {
  set(id: string, entry: FilterRegistration): void
  remove(id: string): void
  snapshot(): FilterSnapshot
}

/** Registro de un ámbito. El orden de la oración es el del documento (con elemento) y, sin elemento, el de alta. */
export function createFilterRegistry(): FilterRegistry {
  const entries = new Map<string, FilterRegistration & { seq: number }>()
  let seq = 0
  return {
    set(id, entry) {
      const prev = entries.get(id)
      entries.set(id, { ...entry, seq: prev ? prev.seq : seq++ })
    },
    remove(id) {
      entries.delete(id)
    },
    snapshot() {
      const all = [...entries.values()]
      all.sort((a, b) => {
        const ea = a.element?.isConnected ? a.element : null
        const eb = b.element?.isConnected ? b.element : null
        if (ea && eb && ea !== eb) {
          const pos = ea.compareDocumentPosition(eb)
          if (pos & 4 /* DOCUMENT_POSITION_FOLLOWING */) return -1
          if (pos & 2 /* DOCUMENT_POSITION_PRECEDING */) return 1
        }
        if (ea && !eb) return -1
        if (!ea && eb) return 1
        return a.seq - b.seq
      })
      return {
        controls: all.length,
        applied: all.filter((e) => e.value !== null).map((e) => ({ label: e.label, value: e.value ?? '' })),
      }
    },
  }
}

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Máximo de valores que se nombran de una selección múltiple; el resto va como "y N más". */
export const FILTER_LIST_MAX = 4

/**
 * Un valor con partes "Código · Nombre" (almacenes, clientes, posiciones) se escribe "Código (Nombre)": el " · " separa
 * los filtros en la oración y no debe aparecer dentro de un valor. "A-01 · PISO · ALM-01" → "A-01 (PISO, ALM-01)".
 */
export function filterItemText(value: string): string {
  const parts = value
    .split(' · ')
    .map((p) => p.trim())
    .filter(Boolean)
  if (parts.length <= 1) return parts[0] ?? ''
  return `${parts[0]} (${parts.slice(1).join(', ')})`
}

/** Valores de una selección (una o varias): "A, B, C" o "A, B, C, D y 3 más" (cada uno con `filterItemText`); vacío =
 *  null (sin filtro). */
export function joinFilterValues(values: readonly string[], t: Translate): string | null {
  const clean = values.map((v) => filterItemText(v.trim())).filter(Boolean)
  if (clean.length === 0) return null
  if (clean.length <= FILTER_LIST_MAX) return clean.join(', ')
  return t('ui.filters.applied.more', { list: clean.slice(0, FILTER_LIST_MAX).join(', '), count: clean.length - FILTER_LIST_MAX })
}

/**
 * Día 'YYYY-MM-DD' con el formato corto de la COMPAÑÍA (Región y formatos: orden y separador; Puerto Rico 09/01/2026). El
 * idioma ya no decide el orden; `lang` se conserva por compatibilidad.
 */
export function formatFilterDate(day: string, _lang: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(day) ? formatDate(day) : day
}

/** Rango de fechas: "del 09/01/2026 al 09/30/2026" (formato de la compañía), "desde 01/09/2026", "hasta 30/09/2026"; sin fechas = null. */
export function dateRangeFilterText(range: { from: string; to: string }, lang: string, t: Translate): string | null {
  const from = range.from ? formatFilterDate(range.from, lang) : ''
  const to = range.to ? formatFilterDate(range.to, lang) : ''
  if (from && to) return t('ui.filters.applied.between', { from, to })
  if (from) return t('ui.filters.applied.since', { from })
  if (to) return t('ui.filters.applied.until', { to })
  return null
}

/** Texto libre: "abc" entre comillas; vacío = null. */
export function textFilterValue(text: string | null | undefined): string | null {
  const s = (text ?? '').trim()
  return s ? `"${s}"` : null
}

/**
 * Oración de filtros de una exportación: null sin ámbito o sin controles de filtro (tabla sin barra o dentro de un modal:
 * no se pone la línea); "Sin filtros" si hay barra pero nada elegido; si no, "Filtros: Etiqueta valor · Etiqueta valor".
 */
export function filtersSentence(snapshot: FilterSnapshot | null | undefined, t: Translate): string | null {
  if (!snapshot || snapshot.controls === 0) return null
  if (snapshot.applied.length === 0) return t('ui.filters.applied.none')
  const parts = snapshot.applied.map((f) => (f.value ? `${f.label} ${f.value}` : f.label))
  return `${t('ui.filters.applied.prefix')} ${parts.join(' · ')}`
}
