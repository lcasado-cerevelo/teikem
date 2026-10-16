// Lote 68 (kits) — lógica pura del editor de componentes de un kit (sin React). El servidor es quien manda en las reglas de fondo (otro kit, otro dueño, rastreo,
// existencia…); aquí solo lo que se puede decir antes de enviar: al menos un componente, cantidades mayores que 0 y sin repetir.
import type { components } from '../../kernel/api/schema'

export type KitDto = components['schemas']['KitDto']
export type KitSetRequest = components['schemas']['KitSetRequest']

export interface KitRow {
  key: string
  productPublicId: string
  sku: string
  name: string
  /** Texto escrito por la persona (acepta coma decimal). */
  quantity: string
}

export type KitDraftError = 'need' | 'product' | 'quantity' | 'duplicate'

export function parseQuantity(text: string): number | null {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : null
}

export function newKitRow(key: string): KitRow {
  return { key, productPublicId: '', sku: '', name: '', quantity: '1' }
}

/** Filas con los componentes del kit actual (para editar). */
export function rowsFromKit(kit: KitDto | undefined, keyOf: (i: number) => string): KitRow[] {
  const rows = (kit?.components ?? []).map((c, i) => ({
    key: keyOf(i),
    productPublicId: c.productPublicId ?? '',
    sku: c.sku ?? '',
    name: c.name ?? '',
    quantity: String(c.quantity ?? 1),
  }))
  return rows.length > 0 ? rows : [newKitRow(keyOf(0))]
}

/** Primer problema que se puede ver sin preguntar al servidor; null = listo para enviar. */
export function kitDraftError(rows: readonly KitRow[]): KitDraftError | null {
  const filled = rows.filter((r) => r.productPublicId !== '')
  if (filled.length === 0) return rows.length === 0 ? 'need' : 'product'
  if (rows.some((r) => r.productPublicId === '')) return 'product'
  if (filled.some((r) => parseQuantity(r.quantity) === null)) return 'quantity'
  if (new Set(filled.map((r) => r.productPublicId)).size !== filled.length) return 'duplicate'
  return null
}

export function toKitRequest(rows: readonly KitRow[], adjustable: boolean): KitSetRequest {
  return {
    isKit: true,
    adjustable,
    components: rows.filter((r) => r.productPublicId !== '').map((r) => ({ productPublicId: r.productPublicId, quantity: parseQuantity(r.quantity) ?? 0 })),
  }
}

/** Mayor «se pueden armar» entre los almacenes (para el resumen); 0 sin almacenes. */
export function bestCanBuild(kit: KitDto | undefined): number {
  return Math.max(0, ...(kit?.availability ?? []).map((a) => a.canBuild ?? 0))
}
