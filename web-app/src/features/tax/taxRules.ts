// Reglas puras de la pantalla de impuestos (Lote 56): porcentajes escritos a mano, suma de componentes y formularios de tasa y exención.

/** «10,5» o «10.5» → 10.5. Vacío o no numérico → null. Hasta 4 decimales (como el servidor). */
export function parsePercent(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (clean === '' || !/^\d+(\.\d+)?$/.test(clean)) return null
  return Math.round(Number(clean) * 10_000) / 10_000
}

export interface ComponentDraft {
  /** Clave estable de la fila en pantalla (no se envía). */
  key: number
  code: string
  name: string
  percent: string
}

/** Suma de los porcentajes válidos, sin errores de coma flotante (a 4 decimales). */
export function sumPercent(rows: readonly { percent: string }[]): number {
  const cents = rows.reduce((acc, r) => acc + Math.round((parsePercent(r.percent) ?? 0) * 10_000), 0)
  return cents / 10_000
}

export interface RateDraft {
  code: string
  name: string
  from: string
  to: string
  components: ComponentDraft[]
}

export type RateErrorKey = 'code' | 'name' | 'from' | 'dates' | 'components' | 'componentFields' | 'percent' | 'total'

/** Primer problema del formulario de tasa (la clave se traduce en la pantalla) o null si está bien. El servidor repite estas reglas. */
export function rateError(d: RateDraft): RateErrorKey | null {
  if (!d.code.trim()) return 'code'
  if (!d.name.trim()) return 'name'
  if (!d.from) return 'from'
  if (d.to && d.to < d.from) return 'dates'
  if (d.components.length === 0) return 'components'
  if (d.components.some((c) => !c.code.trim() || !c.name.trim())) return 'componentFields'
  if (d.components.some((c) => parsePercent(c.percent) === null || (parsePercent(c.percent) ?? 0) > 100)) return 'percent'
  if (sumPercent(d.components) > 100) return 'total'
  return null
}

export interface ExemptionDraft {
  clientId: number | null
  type: string
  certificate: string
  from: string
  to: string
  notes: string
}

export type ExemptionErrorKey = 'client' | 'type' | 'certificate' | 'from' | 'dates'

export function exemptionError(d: ExemptionDraft): ExemptionErrorKey | null {
  if (d.clientId === null) return 'client'
  if (!d.type.trim()) return 'type'
  if (!d.certificate.trim()) return 'certificate'
  if (!d.from) return 'from'
  if (d.to && d.to < d.from) return 'dates'
  return null
}

/** «Estatal 10.5 % + Municipal 1 %» para la tabla. */
export function componentsText(components: readonly { name?: string | null; percent?: number }[] | null | undefined): string {
  return (components ?? []).map((c) => `${c.name ?? ''} ${c.percent ?? 0} %`).join(' + ')
}

/** Día siguiente (AAAA-MM-DD) para proponer el inicio de la tasa nueva al cerrar la vieja. */
export function nextDay(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d + 1))
  return date.toISOString().slice(0, 10)
}

export const CATEGORIES = ['TAXABLE', 'EXEMPT', 'ZERO_RATE'] as const
export type TaxCategory = (typeof CATEGORIES)[number]
