// Lote 235 — reglas puras de lo DISPONIBLE al escoger productos en el pedido del teléfono. COPIA de web-app/src/features/sales/stockRules.ts (deben seguir dando lo
// mismo; scripts/test-sales-order.mjs repite sus casos): el almacén de la orden (por omisión el PRINCIPAL cuando la compañía tiene varios), «Disponible: N» en ese
// almacén, lo que quedaría en BACKORDER si se pide de más (se permite: solo se MARCA) y la nota de otros almacenes con disponible. Los datos salen de
// GET /sales-orders/stock (una sola consulta; lo que no viene es 0). Sin React ni navegador.
// Lote 236: el disponible de un KIT que se arma al pedir (el kit de oferta) sale DESGLOSADO por pieza (`kitStock`): cuánto lleva cada kit, cuánto hay de cada pieza
// en el almacén de la orden, cuántos kits da y cuál es la que limita. Las piezas salen de `kits` (la misma respuesta) o, si la orden las cambió, de la orden.

export interface StockWarehouse {
  publicId?: string
  code?: string | null
  name?: string | null
  isMain?: boolean
}

export interface StockInput {
  warehouses?: readonly StockWarehouse[] | null
  products?: readonly { productPublicId?: string; stock?: readonly { warehousePublicId?: string; available?: number }[] | null }[] | null
  /** Lote 236: los kits que se arman al pedir, con sus piezas (un kit armado no viene aquí). */
  kits?: readonly { kitPublicId?: string; components?: readonly StockKitComponent[] | null }[] | null
}

export interface StockKitComponent {
  productPublicId?: string
  sku?: string | null
  name?: string | null
  qtyPerKit?: number
}

/** Una pieza del kit tal como va en la orden (la cantidad por kit puede venir escrita a mano). */
export interface KitPieceInput {
  productPublicId?: string | null
  sku?: string | null
  name?: string | null
  quantity?: string | number | null
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

export interface KitPieceStock {
  productPublicId: string
  sku: string
  name: string
  /** Cuántas lleva cada kit (0 si no es un número válido). */
  qtyPerKit: number
  /** Lo disponible de la pieza en el almacén. */
  available: number
  /** Kits completos que esta pieza sola deja armar. */
  kits: number
  /** Es la que limita (la que menos kits da; si empatan, todas las empatadas). */
  limiting: boolean
}

export interface KitStock {
  /** Kits completos que se pueden armar en el almacén (lo que da la pieza que limita; sin piezas = 0). */
  available: number
  pieces: KitPieceStock[]
}

/** ¿Es un kit que se arma al pedir (viene con piezas en `kits`)? Un kit armado o un producto normal, no. */
export function isBuiltOnOrderKit(stock: Stock, productPublicId: string | null | undefined): boolean {
  return !!productPublicId && (stock?.kits ?? []).some((k) => k.kitPublicId === productPublicId)
}

/**
 * El desglose del disponible de un kit que se arma al pedir en un almacén: por pieza, cuánto lleva el kit, cuánto hay y cuántos kits da; el disponible del kit
 * es lo que da la pieza que limita (misma cuenta que el servidor: `KitRules.CanBuild`). `pieces` = las piezas de la ORDEN si se cambiaron (sustitución o
 * cantidad ajustada); sin ellas, las del catálogo. null si el producto no es un kit que se arma al pedir (o no hay datos).
 */
export function kitStock(stock: Stock, kitPublicId: string | null | undefined, warehousePublicId: string | null | undefined, pieces?: readonly KitPieceInput[] | null): KitStock | null {
  if (!stock || !kitPublicId || !warehousePublicId) return null
  const kit = (stock.kits ?? []).find((k) => k.kitPublicId === kitPublicId)
  if (!kit) return null
  const source: readonly KitPieceInput[] = pieces && pieces.length > 0 ? pieces : (kit.components ?? []).map((c) => ({ productPublicId: c.productPublicId, sku: c.sku, name: c.name, quantity: c.qtyPerKit }))
  const rows = source.map((p) => {
    const qty = toNumber(p.quantity)
    const qtyPerKit = qty !== null && qty > 0 ? qty : 0
    const available = availableIn(stock, p.productPublicId, warehousePublicId)
    return { productPublicId: p.productPublicId ?? '', sku: p.sku ?? '', name: p.name ?? '', qtyPerKit, available, kits: qtyPerKit > 0 ? Math.floor(Math.round((available / qtyPerKit) * 1e6) / 1e6) : 0 }
  })
  const available = rows.length === 0 ? 0 : Math.min(...rows.map((r) => r.kits))
  return { available, pieces: rows.map((r) => ({ ...r, limiting: r.kits === available })) }
}

/** Las piezas de un kit en la orden: las que el vendedor cambió en pantalla o, si no, las de la orden guardada; null = las del catálogo. */
export function orderKitPieces(
  edited: readonly KitPieceInput[] | null | undefined,
  saved: { components?: readonly { productPublicId?: string; sku?: string | null; name?: string | null; qtyPerKit?: number }[] | null } | null | undefined,
): readonly KitPieceInput[] | null {
  if (edited && edited.length > 0) return edited
  const comps = saved?.components ?? []
  return comps.length > 0 ? comps.map((c) => ({ productPublicId: c.productPublicId, sku: c.sku, name: c.name, quantity: c.qtyPerKit ?? 1 })) : null
}

export interface LineStock {
  available: number
  backorder: number
  elsewhere: ElsewhereStock[]
  /** Lote 236: el desglose por pieza, solo si el producto es un kit que se arma al pedir. */
  kit?: KitStock
}

/**
 * Todo lo que el renglón de un producto dice del inventario; null mientras no se conozca (sin datos o sin almacén): la pantalla no pinta nada.
 * Para un kit que se arma al pedir, el disponible (aquí y en los otros almacenes) se cuenta con sus piezas — las de la orden si se pasan.
 */
export function lineStock(
  stock: Stock,
  productPublicId: string | null | undefined,
  warehousePublicId: string | null | undefined,
  quantity: string | number | null | undefined,
  pieces?: readonly KitPieceInput[] | null,
): LineStock | null {
  if (!stock || !productPublicId || !warehousePublicId) return null
  const kit = kitStock(stock, productPublicId, warehousePublicId, pieces)
  if (kit) {
    const elsewhere: ElsewhereStock[] = []
    for (const w of stockWarehouses(stock)) {
      if (!w.publicId || w.publicId === warehousePublicId) continue
      const there = kitStock(stock, productPublicId, w.publicId, pieces)?.available ?? 0
      if (there > 0) elsewhere.push({ warehousePublicId: w.publicId, name: w.name || w.code || '', available: there })
    }
    return { available: kit.available, backorder: backorderQty(quantity, kit.available), elsewhere, kit }
  }
  const available = availableIn(stock, productPublicId, warehousePublicId)
  return { available, backorder: backorderQty(quantity, available), elsewhere: availableElsewhere(stock, productPublicId, warehousePublicId) }
}

/** ¿Alguna línea pide más de lo disponible? (el pedido guiado manda entonces `allowBackorder` sin volver a preguntar: el vendedor ya vio el distintivo). */
export function anyBackorder(
  stock: Stock,
  warehousePublicId: string | null | undefined,
  lines: readonly { productPublicId?: string | null; quantity: string | number; components?: readonly KitPieceInput[] | null }[],
): boolean {
  if (!stock || !warehousePublicId) return false
  return lines.some((l) => (lineStock(stock, l.productPublicId, warehousePublicId, l.quantity, l.components)?.backorder ?? 0) > 0)
}

/** Cuántas piezas caben a la vista sin plegar el desglose («Ver piezas (N)»). */
export const KIT_PIECES_UNFOLDED = 3

/** Clave i18n (`sales.stock.*`) del principio del aviso según lo disponible: nada, uno o varios. */
export function shortHeadKey(available: number): 'shortNone' | 'shortOne' | 'shortMany' {
  return available <= 0 ? 'shortNone' : available === 1 ? 'shortOne' : 'shortMany'
}
