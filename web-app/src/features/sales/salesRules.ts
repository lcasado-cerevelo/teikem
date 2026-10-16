// Reglas puras de la pantalla de órdenes de venta (Lote 60): cantidades y precios escritos a mano, totales del borrador y su validación.

/** «2,5» o «2.5» → 2.5. Vacío o no numérico → null. */
export function parseNumber(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (clean === '' || !/^\d+(\.\d+)?$/.test(clean)) return null
  return Number(clean)
}

export function decimalsOf(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

export interface LineDraft {
  /** Clave estable de la fila en pantalla (no se envía). */
  key: number
  productPublicId: string | null
  sku: string
  name: string
  /** Precio de venta del producto (el que se usa si no se escribe otro). */
  listPrice: number | null
  quantity: string
  /** Vacío = el precio de lista. */
  price: string
  /** Lote 69: la línea es de un kit (se pide en piezas enteras; sus componentes se ven debajo). */
  isKit: boolean
  /** Lote 69: piezas ajustadas al pedir (solo kits ajustables); null = las del catálogo / las ya guardadas en la orden. */
  components: KitPiece[] | null
}

export interface KitPiece {
  productPublicId: string
  sku: string
  name: string
  /** Cantidad por kit escrita a mano. */
  quantity: string
}

export interface OrderDraft {
  clientPublicId: string | null
  warehousePublicId: string | null
  requiredDate: string
  notes: string
  lines: LineDraft[]
}

export type DraftErrorKey = 'client' | 'lines' | 'product' | 'quantity' | 'quantityDecimals' | 'kitWhole' | 'kitPiece' | 'price' | 'priceDecimals' | 'noPrice' | 'repeated' | 'notes'

/** Primer problema del borrador (la clave se traduce en la pantalla) o null. El servidor repite estas reglas. */
export function draftError(d: OrderDraft): DraftErrorKey | null {
  if (!d.clientPublicId) return 'client'
  if (d.lines.length === 0) return 'lines'
  const seen = new Set<string>()
  for (const l of d.lines) {
    if (!l.productPublicId) return 'product'
    if (seen.has(l.productPublicId)) return 'repeated'
    seen.add(l.productPublicId)
    const q = parseNumber(l.quantity)
    if (q === null || q <= 0) return 'quantity'
    if (decimalsOf(q) > 3) return 'quantityDecimals'
    if (l.isKit && !Number.isInteger(q)) return 'kitWhole'
    if (l.isKit && l.components && l.components.some((c) => { const n = parseNumber(c.quantity); return n === null || n <= 0 })) return 'kitPiece'
    if (l.price.trim() !== '') {
      const p = parseNumber(l.price)
      if (p === null) return 'price'
      if (decimalsOf(p) > 4) return 'priceDecimals'
    } else if (l.listPrice === null) return 'noPrice'
  }
  if (d.notes.length > 500) return 'notes'
  return null
}

/** Precio que se usará en la línea: el escrito o el de lista. */
export function effectivePrice(l: LineDraft): number | null {
  if (l.price.trim() !== '') return parseNumber(l.price)
  return l.listPrice
}

export function lineAmount(l: LineDraft): number {
  const q = parseNumber(l.quantity)
  const p = effectivePrice(l)
  return q === null || p === null ? 0 : Math.round(q * p * 10_000) / 10_000
}

/** Subtotal del borrador (sin impuesto: lo calcula el servidor al guardar). */
export function draftSubtotal(d: OrderDraft): number {
  return Math.round(d.lines.reduce((acc, l) => acc + lineAmount(l), 0) * 10_000) / 10_000
}

/** Cuerpo para crear o reemplazar: el precio solo viaja si se escribió uno distinto del de lista. */
export function toRequest(d: OrderDraft) {
  return {
    warehousePublicId: d.warehousePublicId ?? undefined,
    clientPublicId: d.clientPublicId ?? undefined,
    requiredDate: d.requiredDate || undefined,
    clearRequiredDate: d.requiredDate ? undefined : true,
    notes: d.notes,
    lines: d.lines.map((l) => {
      const price = l.price.trim() === '' ? null : parseNumber(l.price)
      return {
        productPublicId: l.productPublicId,
        quantity: parseNumber(l.quantity) ?? undefined,
        unitPrice: price === null || price === l.listPrice ? undefined : price,
        components: l.isKit && l.components ? l.components.map((c) => ({ productPublicId: c.productPublicId, quantity: parseNumber(c.quantity) ?? 0 })) : undefined,
      }
    }),
  }
}

export const ORDER_STATUSES = ['DRAFT', 'REQUESTED', 'PARTIAL', 'SHIPPED', 'CANCELLED'] as const
