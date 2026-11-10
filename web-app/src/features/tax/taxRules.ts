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

/** Lote 237 — alcance de la exención: todo el impuesto, o solo algunos componentes de la tasa (p. ej. el municipal). */
export type ExemptionScope = 'ALL' | 'COMPONENTS'

export interface ExemptionDraft {
  clientId: number | null
  type: string
  certificate: string
  from: string
  to: string
  notes: string
  scope: ExemptionScope
  /** Códigos de componente exentos (solo cuentan con scope = COMPONENTS). */
  components: string[]
}

export type ExemptionErrorKey = 'client' | 'type' | 'certificate' | 'from' | 'dates' | 'components'

export function exemptionError(d: ExemptionDraft): ExemptionErrorKey | null {
  if (d.clientId === null) return 'client'
  if (!d.type.trim()) return 'type'
  if (!d.certificate.trim()) return 'certificate'
  if (!d.from) return 'from'
  if (d.to && d.to < d.from) return 'dates'
  if (d.scope === 'COMPONENTS' && d.components.length === 0) return 'components'
  return null
}

export function scopeOf(value: string | null | undefined): ExemptionScope {
  return value === 'COMPONENTS' ? 'COMPONENTS' : 'ALL'
}

/** Lo que se manda al API: con «todo el impuesto» no van componentes. */
export function scopeBody(d: Pick<ExemptionDraft, 'scope' | 'components'>): { scope: ExemptionScope; componentCodes: string[] } {
  return { scope: d.scope, componentCodes: d.scope === 'COMPONENTS' ? [...d.components].sort() : [] }
}

export interface ComponentOption {
  code: string
  name: string
  /** null = el componente no está en la tasa por omisión vigente (viene de la exención guardada). */
  percent: number | null
}

interface RateLike {
  code?: string | null
  status?: string | null
  components?: readonly { code?: string | null; name?: string | null; percent?: number }[] | null
}

/**
 * Las casillas del formulario de exención: los componentes de la tasa por omisión VIGENTE (si la compañía no ha escogido tasa por omisión, los de
 * todas las tasas vigentes). Los códigos que la exención ya tenía y no están ahí se agregan al final (para no perderlos al editar).
 */
export function componentOptions(rates: readonly RateLike[] | null | undefined, defaultRateCode: string | null | undefined, selected: readonly string[] = []): ComponentOption[] {
  const active = (rates ?? []).filter((r) => r.status === 'ACTIVE')
  const source = defaultRateCode ? active.filter((r) => r.code === defaultRateCode) : active
  const options: ComponentOption[] = []
  for (const r of source)
    for (const c of r.components ?? []) {
      const code = c.code ?? ''
      if (code && !options.some((o) => o.code === code)) options.push({ code, name: c.name ?? code, percent: c.percent ?? 0 })
    }
  for (const code of selected) if (!options.some((o) => o.code === code)) options.push({ code, name: code, percent: null })
  return options
}

/** Nombres de los componentes exentos de una exención parcial («Municipal, Estatal»); el código si la tasa vigente no lo tiene. */
export function componentNames(codes: readonly string[] | null | undefined, options: readonly ComponentOption[]): string {
  return (codes ?? []).map((code) => options.find((o) => o.code === code)?.name ?? code).join(', ')
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
