// Rangos de fecha predefinidos del portal (2026-10-23, pedido de Luis: «lo mismo que tenemos en la aplicación web del personal»): el mismo juego «pasado» de
// docs/filtro-fecha-diseno.md (Todas, Hoy, Ayer, Últimos 7 días, Esta semana, Semana pasada, Últimos 30 días, Este mes, Mes pasado, Este año, Personalizado).
// Todo en días AAAA-MM-DD de la zona de la persona; la semana empieza el domingo (como el calendario de Puerto Rico y el valor por omisión de la web del personal).
// Sin React: se prueba en dateRanges.test.ts.

export interface DayRange {
  /** AAAA-MM-DD o '' (abierto). */
  from: string
  to: string
}

export type RangePreset = 'all' | 'today' | 'yesterday' | 'last7' | 'thisWeek' | 'lastWeek' | 'last30' | 'thisMonth' | 'lastMonth' | 'thisYear' | 'custom'

/** Orden en que se ofrecen (Personalizado al final). */
export const RANGE_PRESETS: readonly RangePreset[] = ['all', 'today', 'yesterday', 'last7', 'thisWeek', 'lastWeek', 'last30', 'thisMonth', 'lastMonth', 'thisYear', 'custom']

export const WEEK_START = 0   // domingo

const pad = (n: number) => String(n).padStart(2, '0')
export const dayText = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n)
const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate())

/** Del primer al último día de la semana que contiene `d` (empieza el domingo). */
function weekOf(d: Date): DayRange {
  const day = startOfDay(d)
  const back = (day.getDay() - WEEK_START + 7) % 7
  const first = addDays(day, -back)
  return { from: dayText(first), to: dayText(addDays(first, 6)) }
}

function monthOf(year: number, month: number): DayRange {
  return { from: dayText(new Date(year, month, 1)), to: dayText(new Date(year, month + 1, 0)) }
}

/** Las fechas concretas de un rango predefinido («custom» y «all» no fijan fechas: vacío). */
export function rangeOf(preset: RangePreset, now = new Date()): DayRange {
  const today = startOfDay(now)
  switch (preset) {
    case 'today': return { from: dayText(today), to: dayText(today) }
    case 'yesterday': { const y = dayText(addDays(today, -1)); return { from: y, to: y } }
    case 'last7': return { from: dayText(addDays(today, -6)), to: dayText(today) }
    case 'thisWeek': return weekOf(today)
    case 'lastWeek': return weekOf(addDays(today, -7))
    case 'last30': return { from: dayText(addDays(today, -29)), to: dayText(today) }
    case 'thisMonth': return monthOf(today.getFullYear(), today.getMonth())
    case 'lastMonth': return monthOf(today.getFullYear(), today.getMonth() - 1)
    case 'thisYear': return { from: `${today.getFullYear()}-01-01`, to: `${today.getFullYear()}-12-31` }
    default: return { from: '', to: '' }
  }
}

/** Qué rango predefinido describe estas fechas: vacías → «all»; iguales a uno (con la fecha de hoy) → ese; si no → «custom». */
export function presetOf(value: DayRange, now = new Date()): RangePreset {
  if (!value.from && !value.to) return 'all'
  for (const p of RANGE_PRESETS) {
    if (p === 'all' || p === 'custom') continue
    const r = rangeOf(p, now)
    if (r.from === value.from && r.to === value.to) return p
  }
  return 'custom'
}

export const isDayText = (v: string) => /^\d{4}-\d{2}-\d{2}$/.test(v) && !Number.isNaN(Date.parse(`${v}T00:00:00Z`))

/** Inicio del día (UTC) para `fromUtc`; vacío o mal escrito → sin filtro. */
export const dayStartUtc = (day: string) => (isDayText(day) ? `${day}T00:00:00Z` : undefined)

/** Fin del día INCLUIDO: el servidor compara `toUtc` exclusivo, así que se manda el inicio del día siguiente. */
export function dayEndExclusiveUtc(day: string): string | undefined {
  if (!isDayText(day)) return undefined
  const d = new Date(`${day}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString()
}

/** Texto corto del rango para avisos («del 01/10 al 15/10» lo arma la pantalla con su formato); aquí solo si hay algo que filtrar. */
export const hasRange = (v: DayRange) => !!(v.from || v.to)
