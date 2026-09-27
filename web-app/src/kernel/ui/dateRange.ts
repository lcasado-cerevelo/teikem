export interface DateRange {
  /** 'YYYY-MM-DD' o '' */
  from: string
  /** 'YYYY-MM-DD' o '' */
  to: string
}

export const EMPTY_RANGE: DateRange = { from: '', to: '' }

/** true si la fecha ISO (o 'YYYY-MM-DD') cae en el rango [from, to] (extremos vacíos = abiertos). */
export function inDateRange(value: string | null | undefined, range: DateRange): boolean {
  if (!range.from && !range.to) return true
  if (!value) return false
  const day = value.slice(0, 10)
  return (!range.from || day >= range.from) && (!range.to || day <= range.to)
}
