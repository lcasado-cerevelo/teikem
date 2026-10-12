// Rangos predefinidos del filtro de fecha (2026-10-12; docs/filtro-fecha-diseno.md). Lógica pura, sin React: «hoy» llega de afuera (la fecha de
// hoy en la zona horaria de la compañía) y el primer día de la semana también (ajuste de la compañía), así que se prueba sin relojes.
// Todas las fechas son 'YYYY-MM-DD'. Los períodos de calendario (semana, mes, año) son COMPLETOS; «Últimos N días» termina hoy.
import { EMPTY_RANGE, type DateRange } from './dateRange'

export type DatePresetId =
  | 'all'
  | 'today'
  | 'yesterday'
  | 'tomorrow'
  | 'last7'
  | 'next7'
  | 'thisWeek'
  | 'lastWeek'
  | 'nextWeek'
  | 'last30'
  | 'thisMonth'
  | 'lastMonth'
  | 'nextMonth'
  | 'thisYear'
  | 'custom'

/** Juego de rangos: 'past' (movimientos y documentos creados), 'future' (fechas esperadas) o 'both' (se mira el pasado reciente y lo que viene). */
export type DatePresetMode = 'past' | 'future' | 'both'

/** Opciones de cada juego, en el orden en que salen en el desplegable. `all` y `custom` van siempre. */
export const DATE_PRESETS: Record<DatePresetMode, readonly DatePresetId[]> = {
  past: ['all', 'today', 'yesterday', 'last7', 'thisWeek', 'lastWeek', 'last30', 'thisMonth', 'lastMonth', 'thisYear', 'custom'],
  future: ['all', 'today', 'tomorrow', 'next7', 'thisWeek', 'nextWeek', 'thisMonth', 'nextMonth', 'custom'],
  both: ['all', 'yesterday', 'today', 'tomorrow', 'last7', 'next7', 'thisWeek', 'lastWeek', 'nextWeek', 'thisMonth', 'custom'],
}

export interface DatePresetContext {
  /** Hoy ('YYYY-MM-DD') en la zona horaria de la compañía. */
  today: string
  /** Primer día de la semana de la compañía: 0 = domingo, 1 = lunes. */
  weekStartDay: 0 | 1
}

function parse(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d))
}

function format(date: Date): string {
  return date.toISOString().slice(0, 10)
}

/** Suma (o resta) días a un 'YYYY-MM-DD' (calendario puro: sin husos ni horario de verano). */
export function addDaysIso(iso: string, days: number): string {
  const d = parse(iso)
  d.setUTCDate(d.getUTCDate() + days)
  return format(d)
}

/** Primer día de la semana (según `weekStartDay`) que contiene `iso`. */
function weekStart(iso: string, weekStartDay: 0 | 1): string {
  const dayOfWeek = parse(iso).getUTCDay() // 0 = domingo
  return addDaysIso(iso, -((dayOfWeek - weekStartDay + 7) % 7))
}

/** Primer día del mes que está `offset` meses de `iso`. */
function monthStart(iso: string, offset: number): string {
  const d = parse(iso)
  return format(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + offset, 1)))
}

/** Último día del mes que está `offset` meses de `iso`. */
function monthEnd(iso: string, offset: number): string {
  const d = parse(iso)
  return format(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + offset + 1, 0)))
}

/** Fechas de un rango predefinido. `all` = sin fechas; `custom` no tiene fechas propias (null). */
export function presetRange(id: DatePresetId, ctx: DatePresetContext): DateRange | null {
  const { today, weekStartDay } = ctx
  switch (id) {
    case 'all':
      return EMPTY_RANGE
    case 'custom':
      return null
    case 'today':
      return { from: today, to: today }
    case 'yesterday': {
      const d = addDaysIso(today, -1)
      return { from: d, to: d }
    }
    case 'tomorrow': {
      const d = addDaysIso(today, 1)
      return { from: d, to: d }
    }
    case 'last7':
      return { from: addDaysIso(today, -6), to: today }
    case 'next7':
      return { from: today, to: addDaysIso(today, 6) }
    case 'last30':
      return { from: addDaysIso(today, -29), to: today }
    case 'thisWeek': {
      const start = weekStart(today, weekStartDay)
      return { from: start, to: addDaysIso(start, 6) }
    }
    case 'lastWeek': {
      const start = addDaysIso(weekStart(today, weekStartDay), -7)
      return { from: start, to: addDaysIso(start, 6) }
    }
    case 'nextWeek': {
      const start = addDaysIso(weekStart(today, weekStartDay), 7)
      return { from: start, to: addDaysIso(start, 6) }
    }
    case 'thisMonth':
      return { from: monthStart(today, 0), to: monthEnd(today, 0) }
    case 'lastMonth':
      return { from: monthStart(today, -1), to: monthEnd(today, -1) }
    case 'nextMonth':
      return { from: monthStart(today, 1), to: monthEnd(today, 1) }
    case 'thisYear': {
      const y = parse(today).getUTCFullYear()
      return { from: `${y}-01-01`, to: `${y}-12-31` }
    }
  }
}

/**
 * Qué opción describe estas fechas: vacías → `all`; iguales a las de una opción del juego → esa (la primera de la lista si dos coinciden
 * el mismo día); cualquier otra cosa (incluida una fecha sola) → `custom`.
 */
export function matchPreset(range: DateRange, ctx: DatePresetContext, mode: DatePresetMode): DatePresetId {
  if (!range.from && !range.to) return 'all'
  for (const id of DATE_PRESETS[mode]) {
    if (id === 'all' || id === 'custom') continue
    const r = presetRange(id, ctx)
    if (r && r.from === range.from && r.to === range.to) return id
  }
  return 'custom'
}
