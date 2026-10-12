// Ajuste múltiple AJM-###### (2026-10-12; docs/ajuste-multiple-diseno.md): lógica pura de la pantalla, sin React ni red.
// - Un bloque por producto con sus posiciones (cantidad del sistema al abrir) y, por posición, «Hay ahora» como texto.
// - «Hay ahora» vacío = esa línea no se toca; igual al sistema = tampoco genera movimiento; solo las diferencias cuentan.
// - La petición manda TODAS las líneas con «Hay ahora» (también las iguales): el servidor contrasta cada `systemQty` con el saldo
//   bloqueado y responde 409 `stale_system_qty` con `lines[i]` si algo cambió; `rowErrors` devuelve esos mensajes por posición.
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']

export type MultiAdjustmentRequest = Schemas['MultiAdjustmentRequest']
export type MultiAdjustmentProductDto = Schemas['MultiAdjustmentProductDto']

/** Máximo de la nota del documento (igual al del ajuste normal). */
export const MULTI_NOTES_MAX = 300

export interface PositionRow {
  /** `binId:lotId` — identifica la posición dentro del bloque. */
  key: string
  binId: number
  binCode: string
  zoneCode: string
  lotId: number | null
  lotNumber: string | null
  /** Lo que tenía el sistema al abrir (o al actualizar). */
  systemQty: number
  /** «Hay ahora» tal como se escribió ('' = no se toca). */
  counted: string
  /** Posición agregada a mano (el sistema no tenía existencia ahí). */
  added: boolean
}

export interface ProductBlock {
  productPublicId: string
  sku: string
  name: string
  trackingTypeCode: string
  rows: PositionRow[]
}

export const rowKey = (binId: number, lotId: number | null | undefined): string => `${binId}:${lotId ?? 0}`

/** Bloque nuevo desde la respuesta de `GET /inventory/multi-adjustments/stock`. */
export function blockFromStock(p: MultiAdjustmentProductDto): ProductBlock {
  return {
    productPublicId: p.productPublicId ?? '',
    sku: p.sku ?? '',
    name: p.name ?? '',
    trackingTypeCode: p.trackingTypeCode ?? 'NONE',
    rows: (p.positions ?? []).map((x) => ({
      key: rowKey(x.binId ?? 0, x.lotId),
      binId: x.binId ?? 0,
      binCode: x.binCode ?? '',
      zoneCode: x.zoneCode ?? '',
      lotId: x.lotId ?? null,
      lotNumber: x.lotNumber ?? null,
      systemQty: x.qtyOnHand ?? 0,
      counted: '',
      added: false,
    })),
  }
}

/** Los productos con serie no entran (se ajustan con el ajuste normal). */
export const isSerial = (b: Pick<ProductBlock, 'trackingTypeCode'>): boolean => b.trackingTypeCode === 'SERIAL'

/** '' → null (no se toca); número ≥ 0 (acepta coma decimal); cualquier otra cosa → 'invalid'. */
export function parseCounted(text: string): number | null | 'invalid' {
  const s = text.trim().replace(',', '.')
  if (s === '') return null
  if (!/^\d+(\.\d{1,3})?$/.test(s)) return 'invalid'
  const n = Number(s)
  return Number.isFinite(n) ? n : 'invalid'
}

/** Diferencia con signo (contado − sistema); null si la línea no se toca o está mal escrita. */
export function rowDifference(row: Pick<PositionRow, 'counted' | 'systemQty'>): number | null {
  const n = parseCounted(row.counted)
  if (n === null || n === 'invalid') return null
  return Math.round((n - row.systemQty) * 1000) / 1000
}

export interface MultiSummary {
  /** Líneas con diferencia (cada una será un movimiento). */
  lines: number
  products: number
  up: number
  down: number
  /** Posiciones con «Hay ahora» mal escrito. */
  invalid: number
}

export function summarize(blocks: readonly ProductBlock[]): MultiSummary {
  const s: MultiSummary = { lines: 0, products: 0, up: 0, down: 0, invalid: 0 }
  for (const b of blocks) {
    let any = false
    for (const r of b.rows) {
      if (parseCounted(r.counted) === 'invalid') s.invalid++
      const d = rowDifference(r)
      if (d === null || d === 0) continue
      any = true
      s.lines++
      if (d > 0) s.up += d
      else s.down += -d
    }
    if (any) s.products++
  }
  s.up = Math.round(s.up * 1000) / 1000
  s.down = Math.round(s.down * 1000) / 1000
  return s
}

/** Mensaje de por qué «Aplicar» no se puede; null si se puede. */
export function applyBlocker(blocks: readonly ProductBlock[], notes: string, warehouse: string | null): 'warehouse' | 'invalid' | 'nothing' | 'notes' | null {
  if (!warehouse) return 'warehouse'
  const s = summarize(blocks)
  if (s.invalid > 0) return 'invalid'
  if (s.lines === 0) return 'nothing'
  if (notes.trim() === '') return 'notes'
  return null
}

export interface BuiltRequest {
  body: MultiAdjustmentRequest
  /** índice de línea del API → dónde está (producto + posición), para pintar los errores `lines[i]`. */
  index: { productPublicId: string; key: string }[]
}

/** Arma la petición con todas las posiciones que tienen «Hay ahora» (el servidor decide cuáles generan movimiento). */
export function buildRequest(warehousePublicId: string, notes: string, blocks: readonly ProductBlock[]): BuiltRequest {
  const lines: NonNullable<MultiAdjustmentRequest['lines']> = []
  const index: BuiltRequest['index'] = []
  for (const b of blocks) {
    if (isSerial(b)) continue
    for (const r of b.rows) {
      const n = parseCounted(r.counted)
      if (n === null || n === 'invalid') continue
      lines.push({ productPublicId: b.productPublicId, binId: r.binId, lotId: r.lotId, systemQty: r.systemQty, countedQty: n })
      index.push({ productPublicId: b.productPublicId, key: r.key })
    }
  }
  return { body: { warehousePublicId, notes: notes.trim(), lines }, index }
}

/** Clave de una posición para los errores: `producto|binId:lotId`. */
export const errorKey = (productPublicId: string, key: string): string => `${productPublicId}|${key}`

/** Errores `lines[i]` / `lines[i].campo` del API → mensaje por posición (el primero de cada una). */
export function rowErrors(errors: Record<string, string[]> | undefined, index: BuiltRequest['index']): Record<string, string> {
  const out: Record<string, string> = {}
  for (const [field, msgs] of Object.entries(errors ?? {})) {
    const m = /^lines\[(\d+)\]/.exec(field)
    if (!m) continue
    const at = index[Number(m[1])]
    if (at && msgs[0]) out[errorKey(at.productPublicId, at.key)] ??= msgs[0]
  }
  return out
}

/** Errores que no son de una línea (nota, almacén, «lines»): para mostrarlos arriba. */
export function generalErrors(errors: Record<string, string[]> | undefined): string[] {
  return Object.entries(errors ?? {})
    .filter(([field]) => !/^lines\[/.test(field))
    .flatMap(([, msgs]) => msgs)
}

/**
 * Pone al día «En sistema» con lo que hay ahora (tras un 409): conserva lo escrito en «Hay ahora» y las posiciones agregadas a mano;
 * una posición que ya no tiene existencia queda con 0 y una que apareció se agrega al final del bloque.
 */
export function refreshBlocks(blocks: readonly ProductBlock[], fresh: readonly MultiAdjustmentProductDto[]): ProductBlock[] {
  return blocks.map((b) => {
    const f = fresh.find((x) => x.productPublicId === b.productPublicId)
    if (!f) return b
    const latest = blockFromStock(f).rows
    const rows = b.rows.map((r) => {
      const now = latest.find((x) => x.key === r.key)
      return { ...r, systemQty: now?.systemQty ?? 0 }
    })
    for (const n of latest) if (!rows.some((r) => r.key === n.key)) rows.push(n)
    return { ...b, rows }
  })
}

/** Agrega al bloque una posición sin existencia (se encontró mercancía en un lugar vacío); no duplica. */
export function addPosition(
  block: ProductBlock,
  pos: { binId: number; binCode: string; zoneCode: string; lotId?: number | null; lotNumber?: string | null },
): ProductBlock {
  const key = rowKey(pos.binId, pos.lotId ?? null)
  if (block.rows.some((r) => r.key === key)) return block
  return {
    ...block,
    rows: [
      ...block.rows,
      { key, binId: pos.binId, binCode: pos.binCode, zoneCode: pos.zoneCode, lotId: pos.lotId ?? null, lotNumber: pos.lotNumber ?? null, systemQty: 0, counted: '', added: true },
    ],
  }
}

/**
 * Posiciones agregadas a mano como un conjunto (desplegable de selección múltiple): las marcadas que el bloque aún no tiene se agregan sin existencia
 * (el label es «Código · Zona · Almacén»); las agregadas que se desmarcan se quitan; las que traen existencia del sistema nunca se quitan; conserva lo escrito.
 */
export function withAddedPositions(block: ProductBlock, items: readonly { id: number; label: string }[]): ProductBlock {
  const wanted = new Set(items.map((i) => i.id))
  let next: ProductBlock = { ...block, rows: block.rows.filter((r) => !r.added || wanted.has(r.binId)) }
  for (const i of items) {
    const [code, zone] = i.label.split(' · ')
    next = addPosition(next, { binId: i.id, binCode: code, zoneCode: zone ?? '' })
  }
  return next
}

/** ¿Hay algo escrito que se perdería al salir? */
export const isDirty = (blocks: readonly ProductBlock[], notes: string): boolean => notes.trim() !== '' || blocks.some((b) => b.rows.some((r) => r.counted.trim() !== ''))
