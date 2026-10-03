// Calendario laboral de la compañía (Ajustes → Calendario): días laborables (`Tenant.WorkDaysMask`: bit 1 << día, domingo
// = 1 … sábado = 64, como `TenantService.IsWorkDayAsync`) y feriados (exactos o "cada año": mismo mes y día). Lógica pura.

export interface HolidayLike {
  date?: string
  isRecurring?: boolean
}

/** Días de la semana 0 (domingo) … 6 (sábado). */
export const WEEK_DAYS = [0, 1, 2, 3, 4, 5, 6] as const

/** Bit de un día en la máscara (domingo = 1, lunes = 2 … sábado = 64). */
export function dayBit(day: number): number {
  return 1 << day
}

/** Días laborables de una máscara, en orden de domingo a sábado. */
export function workDaysFromMask(mask: number): number[] {
  return WEEK_DAYS.filter((d) => (mask & dayBit(d)) !== 0)
}

/** Máscara de una lista de días. */
export function maskFromWorkDays(days: readonly number[]): number {
  return days.reduce((m, d) => m | dayBit(d), 0)
}

/** Prende o apaga un día; nunca deja la semana sin días laborables (`null` = no se puede: es el último). */
export function toggleWorkDay(mask: number, day: number): number | null {
  const next = mask ^ dayBit(day)
  return (next & 127) === 0 ? null : next & 127
}

/** Orden de los días en pantalla según el primer día de la semana de la compañía (0 domingo, 1 lunes). */
export function weekOrder(weekStartDay: number): number[] {
  return weekStartDay === 1 ? [1, 2, 3, 4, 5, 6, 0] : [0, 1, 2, 3, 4, 5, 6]
}

const day = (iso: string) => iso.slice(0, 10)

/** ¿Es feriado? La fecha exacta, o un feriado "cada año" con el mismo mes y día. */
export function isHoliday(iso: string, holidays: readonly HolidayLike[]): boolean {
  const d = day(iso)
  const md = d.slice(5)
  return holidays.some((h) => {
    const hd = day(h.date ?? '')
    return hd === d || (h.isRecurring === true && hd.slice(5) === md)
  })
}

/** Día de la semana (0-6) de un 'YYYY-MM-DD' (sin corrimiento de zona). */
export function weekDayOf(iso: string): number {
  const [y, m, d] = day(iso).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay()
}

/** ¿Día hábil? Día laborable de la máscara y que no sea feriado. */
export function isWorkDay(iso: string, mask: number, holidays: readonly HolidayLike[]): boolean {
  return (mask & dayBit(weekDayOf(iso))) !== 0 && !isHoliday(iso, holidays)
}

/** Día siguiente 'YYYY-MM-DD' (+n). */
export function addDays(iso: string, n: number): string {
  const [y, m, d] = day(iso).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10)
}

/** Próximo día hábil DESPUÉS de `fromIso` (hasta un año y unos días); null si no hay (máscara vacía o todo feriado). */
export function nextWorkDay(fromIso: string, mask: number, holidays: readonly HolidayLike[]): string | null {
  for (let i = 1; i <= 370; i++) {
    const iso = addDays(fromIso, i)
    if (isWorkDay(iso, mask, holidays)) return iso
  }
  return null
}
