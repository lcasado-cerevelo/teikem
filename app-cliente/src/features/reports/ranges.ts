// Rangos de fechas de los informes (AAAA-MM-DD, en la zona de la persona).
const pad = (n: number) => String(n).padStart(2, '0')
export const dayText = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
export const todayDay = (now = new Date()) => dayText(now)

export type PresetId = 'thisMonth' | 'lastMonth' | 'last30' | 'thisYear'
export const PRESETS: readonly PresetId[] = ['thisMonth', 'lastMonth', 'last30', 'thisYear']

/** Del primer al último día del mes de `now`. */
export function monthRange(now = new Date()): { from: string; to: string } {
  return { from: dayText(new Date(now.getFullYear(), now.getMonth(), 1)), to: dayText(new Date(now.getFullYear(), now.getMonth() + 1, 0)) }
}

export function rangeOf(id: PresetId, now = new Date()): { from: string; to: string } {
  if (id === 'thisMonth') return monthRange(now)
  if (id === 'lastMonth') return monthRange(new Date(now.getFullYear(), now.getMonth() - 1, 1))
  if (id === 'last30') {
    const from = new Date(now)
    from.setDate(from.getDate() - 29)
    return { from: dayText(from), to: dayText(now) }
  }
  return { from: `${now.getFullYear()}-01-01`, to: `${now.getFullYear()}-12-31` }
}
