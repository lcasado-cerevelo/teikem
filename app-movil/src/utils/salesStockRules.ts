// Lote 235 — reglas puras de lo DISPONIBLE al escoger productos en el pedido del teléfono. COPIA de web-app/src/features/sales/stockRules.ts (deben seguir dando lo
// mismo; scripts/test-sales-order.mjs repite sus casos): el almacén de la orden (por omisión el PRINCIPAL cuando la compañía tiene varios), «Disponible: N» en ese
// almacén, lo que quedaría en BACKORDER si se pide de más (se permite: solo se MARCA) y la nota de otros almacenes con disponible. Los datos salen de
// GET /sales-orders/stock (una sola consulta; lo que no viene es 0). Sin React ni navegador.

export interface StockWarehouse {
  publicId?: string
  code?: string | null
  name?: string | null
  isMain?: boolean
}

export interface StockInput {
  warehouses?: readonly StockWarehouse[] | null
  products?: readonly { productPublicId?: string; stock?: readonly { warehousePublicId?: string; available?: number }[] | null }[] | null
}

type Stock = StockInput | null | undefined

/** Texto → número (coma o punto); vacío o inválido = null. */
function toNumber(v: string | number | null | undefined): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null
  const text = (v ?? '').trim().replace(',', '.')
  if (text === '' || !/^\d*\.?\d*$/.test(text)) return null
  const n = Number(text)
  return Number.isFinite(n) ? n : null
}

const round3 = (n: number) => Math.round(n * 1000) / 1000

export function stockWarehouses(stock: Stock): readonly StockWarehouse[] {
  return stock?.warehouses ?? []
}

/** El almacén principal: el marcado por el servidor o, por si acaso, el primero de la lista. Sin almacenes = null. */
export function mainWarehouseId(stock: Stock): string | null {
  const list = stockWarehouses(stock)
  return (list.find((w) => w.isMain) ?? list[0])?.publicId ?? null
}

/** ¿Se muestra el campo Almacén? Solo con MÁS de un almacén activo (con uno solo se usa ese y no se pregunta). */
export function showWarehouseField(stock: Stock): boolean {
  return stockWarehouses(stock).length > 1
}

/** El almacén de la orden: el escogido (aunque ya no esté activo: una orden guardada lo conserva) o, si no hay, el principal. */
export function orderWarehouseId(stock: Stock, chosen: string | null | undefined): string | null {
  return chosen || mainWarehouseId(stock)
}

export function warehouseLabel(stock: Stock, publicId: string | null | undefined): string {
  const w = stockWarehouses(stock).find((x) => x.publicId === publicId)
  return w?.name || w?.code || ''
}

/** Lo disponible para vender del producto en un almacén (0 si no viene). */
export function availableIn(stock: Stock, productPublicId: string | null | undefined, warehousePublicId: string | null | undefined): number {
  if (!productPublicId || !warehousePublicId) return 0
  const row = stock?.products?.find((p) => p.productPublicId === productPublicId)
  return Math.max(0, row?.stock?.find((s) => s.warehousePublicId === warehousePublicId)?.available ?? 0)
}

export interface ElsewhereStock {
  warehousePublicId: string
  name: string
  available: number
}

/** Los OTROS almacenes activos donde el producto tiene disponible (en el orden de la lista de almacenes). Con un solo almacén, nada. */
export function availableElsewhere(stock: Stock, productPublicId: string | null | undefined, warehousePublicId: string | null | undefined): ElsewhereStock[] {
  const out: ElsewhereStock[] = []
  for (const w of stockWarehouses(stock)) {
    if (!w.publicId || w.publicId === warehousePublicId) continue
    const available = availableIn(stock, productPublicId, w.publicId)
    if (available > 0) out.push({ warehousePublicId: w.publicId, name: w.name || w.code || '', available })
  }
  return out
}

/** Lo que quedaría en backorder: lo pedido que pasa de lo disponible (0 si alcanza o si la cantidad no es un número). */
export function backorderQty(quantity: string | number | null | undefined, available: number): number {
  const q = toNumber(quantity)
  if (q === null || q <= 0) return 0
  return round3(Math.max(0, q - Math.max(0, available)))
}

export interface LineStock {
  available: number
  backorder: number
  elsewhere: ElsewhereStock[]
}

/** Todo lo que el renglón de un producto dice del inventario; null mientras no se conozca (sin datos o sin almacén): la pantalla no pinta nada. */
export function lineStock(stock: Stock, productPublicId: string | null | undefined, warehousePublicId: string | null | undefined, quantity: string | number | null | undefined): LineStock | null {
  if (!stock || !productPublicId || !warehousePublicId) return null
  const available = availableIn(stock, productPublicId, warehousePublicId)
  return { available, backorder: backorderQty(quantity, available), elsewhere: availableElsewhere(stock, productPublicId, warehousePublicId) }
}

/** ¿Alguna línea pide más de lo disponible? (el pedido guiado manda entonces `allowBackorder` sin volver a preguntar: el vendedor ya vio el distintivo). */
export function anyBackorder(stock: Stock, warehousePublicId: string | null | undefined, lines: readonly { productPublicId?: string | null; quantity: string | number }[]): boolean {
  if (!stock || !warehousePublicId) return false
  return lines.some((l) => !!l.productPublicId && backorderQty(l.quantity, availableIn(stock, l.productPublicId, warehousePublicId)) > 0)
}

/** Clave i18n (`sales.stock.*`) del principio del aviso según lo disponible: nada, uno o varios. */
export function shortHeadKey(available: number): 'shortNone' | 'shortOne' | 'shortMany' {
  return available <= 0 ? 'shortNone' : available === 1 ? 'shortOne' : 'shortMany'
}
