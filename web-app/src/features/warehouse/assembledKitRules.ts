// Lote 232 — kit ARMADO (producto con inventario propio): reglas puras de la pantalla, sin React. Espejo de `AssembledKitRules` del servidor
// (`src/Teikem.Domain/Wms/AssembledKit.cs`; mismos vectores que `AssembledKitTests.Pure_rules_…` en `assembledKitRules.test.ts`): cuántos kits
// cubre una cantidad, «se pueden armar N» (en UN armado: un solo lote por pieza; juntando lotes: en varios), el rastreo que deciden las piezas,
// el vencimiento del lote del kit, la cantidad de kits (entera, 1 a 100,000) y el reparto del regreso de las piezas al desarmar. Además: lo que
// la pantalla puede revisar de las piezas antes de enviar (sin kits como pieza, sin serie, mismo dueño, activas, máximo 50), los cuerpos que
// viajan al servidor (alta con «es un kit armado», armar, desarmar) y los resúmenes que se muestran antes de confirmar. El servidor repite todo.
import type { components } from '../../kernel/api/schema'
import { kitPiecesError, pieceQuantity, type KitPiece, type KitPiecesProblem } from './kitPieces'

type Schemas = components['schemas']
export type AssembledKitDto = Schemas['AssembledKitDto']
export type KitAssemblyDto = Schemas['KitAssemblyDto']
export type KitAssemblyLineDto = Schemas['KitAssemblyLineDto']
export type StockExitOptionDto = Schemas['StockExitOptionDto']

/** Un kit armado lleva de 1 a 50 piezas. */
export const MAX_KIT_PIECES = 50
/** Se arman o desarman como máximo 100,000 kits a la vez. */
export const MAX_KITS_PER_OPERATION = 100_000
/** Zonas donde no se arman ni desarman kits (posición destino y regreso). */
export const KIT_EXCLUDED_ZONE_TYPES = ['QUARANTINE', 'CROSSDOCK', 'RENTAL'] as const

/** Kits completos que cubre una cantidad disponible (hacia abajo; 0 si no hay o la cantidad por kit no es positiva). */
export function kitsCoveredBy(available: number, qtyPerKit: number): number {
  if (!(qtyPerKit > 0) || !(available > 0)) return 0
  // redondeo a 6 decimales antes del piso: 0.3 / 0.1 no debe dar 2.9999…
  return Math.floor(Math.round((available / qtyPerKit) * 1e6) / 1e6)
}

/** Existencia de una pieza en un almacén: cantidad por kit, si va por lote, lo disponible por lote y el total. */
export interface PieceStock {
  qtyPerKit: number
  lotTracked: boolean
  /** Disponible por lote (id del lote → cantidad); vacío si la pieza no va por lote. */
  byLot: Record<string, number>
  total: number
}

/** Cuántos kits se pueden armar EN UN SOLO ARMADO: por pieza, lo que cubre su mejor lote (o, sin lote, todo); el menor entre las piezas. */
export function canAssembleInOne(pieces: readonly PieceStock[]): number {
  if (pieces.length === 0) return 0
  return Math.min(
    ...pieces.map((p) => {
      if (!p.lotTracked) return kitsCoveredBy(p.total, p.qtyPerKit)
      const lots = Object.values(p.byLot)
      return lots.length === 0 ? 0 : Math.max(...lots.map((a) => kitsCoveredBy(a, p.qtyPerKit)))
    }),
  )
}

/** Cuántos kits alcanzan juntando todos los lotes (en varios armados): por pieza lo que cubre su total; el menor. */
export function canAssembleTotal(pieces: readonly PieceStock[]): number {
  if (pieces.length === 0) return 0
  return Math.min(...pieces.map((p) => kitsCoveredBy(p.total, p.qtyPerKit)))
}

/** Rastreo del kit armado según sus piezas: LOT si alguna va por lote; NONE si ninguna. */
export function decideTracking(trackings: readonly (string | null | undefined)[]): 'LOT' | 'NONE' {
  return trackings.some((t) => (t ?? '').toUpperCase() === 'LOT') ? 'LOT' : 'NONE'
}

/** Vencimiento del lote del kit: el más cercano de los lotes de sus piezas ('YYYY-MM-DD'); null si ninguno vence. */
export function kitExpiry(expiries: readonly (string | null | undefined)[]): string | null {
  let min: string | null = null
  for (const e of expiries) if (e && (min === null || e < min)) min = e
  return min
}

export type KitQuantityProblem = 'kitQtyRequired' | 'kitQtyInvalid' | 'kitQtyTooLarge'

/** Cantidad de kits: obligatoria, entera, > 0 y hasta el tope (mensajes del servidor en `warehouse.products.assembled.err.*`). */
export function kitQuantityProblem(quantity: number | null | undefined): KitQuantityProblem | null {
  if (quantity == null || Number.isNaN(quantity)) return 'kitQtyRequired'
  if (quantity <= 0 || !Number.isInteger(quantity)) return 'kitQtyInvalid'
  return quantity > MAX_KITS_PER_OPERATION ? 'kitQtyTooLarge' : null
}

/**
 * Reparte el regreso de una pieza entre las líneas del armado (en su orden) sin pasar lo que cada línea sacó menos lo que ya volvió.
 * Devuelve [índice de línea, cantidad]; lo que no cabe queda sin repartir.
 */
export function splitReturn(quantity: number, lines: readonly { taken: number; returned: number }[]): [number, number][] {
  const out: [number, number][] = []
  let left = quantity
  for (let i = 0; i < lines.length && left > 0; i++) {
    const room = lines[i].taken - lines[i].returned
    if (room <= 0) continue
    const take = Math.min(room, left)
    out.push([i, take])
    left = round6(left - take)
  }
  return out
}

const round6 = (n: number) => Math.round(n * 1e6) / 1e6

// ---------------------------------------------------------------- piezas (antes de enviar)

/** Lo que la pantalla sabe de una pieza escogida (de su ficha `GET /products/{id}`). */
export interface PieceInfo {
  sku: string
  trackingTypeCode?: string | null
  isKit?: boolean
  isAssembledKit?: boolean
  isActive?: boolean
  ownerClientPublicId?: string | null
}

export type AssembledPiecesProblem =
  | { key: KitPiecesProblem }
  | { key: 'tooMany' }
  | { key: 'pieceIsKit' | 'pieceIsSerial' | 'pieceOtherOwner' | 'pieceInactive'; sku: string }

/**
 * Primer problema de la composición de un kit armado, con lo que se sabe de cada pieza (`infos`: publicId → ficha; una pieza sin ficha
 * todavía no se revisa, la revisa el servidor). `owner` = dueño del kit (null = propio).
 */
export function assembledPiecesProblem(
  pieces: readonly KitPiece[],
  infos: ReadonlyMap<string, PieceInfo>,
  owner: string | null | undefined,
): AssembledPiecesProblem | null {
  const basic = kitPiecesError(pieces)
  if (basic) return { key: basic }
  const chosen = pieces.filter((p) => p.productPublicId)
  if (chosen.length > MAX_KIT_PIECES) return { key: 'tooMany' }
  for (const p of chosen) {
    const info = infos.get(p.productPublicId!)
    if (!info) continue
    const sku = info.sku || p.sku
    if (info.isKit || info.isAssembledKit) return { key: 'pieceIsKit', sku }
    if ((info.trackingTypeCode ?? '').toUpperCase() === 'SERIAL') return { key: 'pieceIsSerial', sku }
    if (info.isActive === false) return { key: 'pieceInactive', sku }
    if ((info.ownerClientPublicId ?? null) !== (owner || null)) return { key: 'pieceOtherOwner', sku }
  }
  return null
}

/** Las piezas como las pide el servidor (`AssembledKitPieceInput[]`): solo las que tienen producto. */
export function assembledPiecesInput(pieces: readonly KitPiece[]): { productPublicId: string; quantity: number }[] {
  return pieces.filter((p) => p.productPublicId).map((p) => ({ productPublicId: p.productPublicId!, quantity: pieceQuantity(p.quantity) ?? 0 }))
}

/** Firma comparable de una composición (producto y cantidad, en orden): para saber si cambió. */
export function piecesSignature(pieces: readonly KitPiece[]): string {
  return assembledPiecesInput(pieces)
    .map((p) => `${p.productPublicId}×${p.quantity}`)
    .join('|')
}

/** Piezas del editor a partir de la composición del servidor (`AssembledKitPieceDto[]`). */
export function piecesFromAssembled(pieces: AssembledKitDto['pieces'], key: (i: number) => string): KitPiece[] {
  return [...(pieces ?? [])]
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    .map((p, i) => ({
      key: key(i),
      productPublicId: p.productPublicId ?? null,
      sku: p.sku ?? '',
      name: p.name ?? '',
      quantity: String(p.quantity ?? 1),
      notSubstitutable: false,
      salePrice: null,
    }))
}

/** Lo que agrega el alta de un kit armado al `ProductCreateRequest` (el rastreo lo deciden las piezas y el costo sale de ellas al armar). */
export function assembledKitCreateFields(v: {
  pieces: readonly KitPiece[]
  assembleQuantity: number | null
  assembleWarehousePublicId: string | null
  assembleToBinId: string
}) {
  const assembling = v.assembleQuantity != null
  return {
    isAssembledKit: true,
    kitPieces: assembledPiecesInput(v.pieces),
    trackingType: null,
    purchaseCost: null,
    assembleQuantity: assembling ? v.assembleQuantity : null,
    assembleWarehousePublicId: assembling ? v.assembleWarehousePublicId || null : null,
    assembleToBinId: assembling && v.assembleToBinId ? Number(v.assembleToBinId) : null,
  }
}

// ---------------------------------------------------------------- existencia de las piezas (se pueden armar N)

/** Existencia por pieza en un almacén a partir de las opciones de salida (`GET /inventory/exit-options`, ya filtradas por almacén). */
export function pieceStocksFromExitOptions(
  pieces: readonly { productPublicId: string; quantity: number; lotTracked: boolean }[],
  rows: readonly StockExitOptionDto[],
): PieceStock[] {
  return pieces.map((p) => {
    const mine = rows.filter((r) => r.productPublicId === p.productPublicId && (r.available ?? 0) > 0)
    const byLot: Record<string, number> = {}
    if (p.lotTracked) for (const r of mine) if (r.lotId != null) byLot[String(r.lotId)] = round6((byLot[String(r.lotId)] ?? 0) + (r.available ?? 0))
    return { qtyPerKit: p.quantity, lotTracked: p.lotTracked, byLot, total: round6(mine.reduce((s, r) => s + (r.available ?? 0), 0)) }
  })
}

// ---------------------------------------------------------------- armar

/** De dónde sale una pieza: '' = la sugerida por la rotación del servidor; si no, posición y lote escogidos (`binId:lotId`). */
export const AUTO_CHOICE = ''
export const choiceKey = (binId: number | null | undefined, lotId: number | null | undefined) => `${binId ?? ''}:${lotId ?? ''}`
export function parseChoiceKey(key: string): { binId: number | null; lotId: number | null } | null {
  if (!key) return null
  const [b, l] = key.split(':')
  return { binId: b ? Number(b) : null, lotId: l ? Number(l) : null }
}

/** Opciones de salida de UNA pieza en el orden de la rotación (rank 1 = la que sale primero: la sugerida). */
export function exitOptionsOf(rows: readonly StockExitOptionDto[], productPublicId: string): StockExitOptionDto[] {
  return rows.filter((r) => r.productPublicId === productPublicId && (r.available ?? 0) > 0).sort((a, b) => (a.rank ?? 0) - (b.rank ?? 0))
}

/** Cuerpo de `POST …/assembled-kit/assemblies`: solo las piezas con salida escogida viajan en `pieces` (las demás, por la rotación). */
export function assembleRequest(v: {
  quantity: number | null
  toBinId: string
  warehousePublicId: string | null
  notes: string
  choices: Readonly<Record<string, string>>
}): Schemas['KitAssembleRequest'] {
  const pieces = Object.entries(v.choices)
    .map(([productPublicId, key]) => ({ productPublicId, chosen: parseChoiceKey(key) }))
    .filter((c) => c.chosen !== null)
    .map((c) => ({ productPublicId: c.productPublicId, lotId: c.chosen!.lotId, binId: c.chosen!.binId }))
  return {
    quantity: v.quantity,
    toBinId: v.toBinId ? Number(v.toBinId) : null,
    warehousePublicId: v.warehousePublicId || null,
    notes: v.notes.trim() || null,
    ...(pieces.length > 0 ? { pieces } : {}),
  }
}

export interface AssembleSummaryRow {
  productPublicId: string
  sku: string
  name: string
  qtyPerKit: number
  /** Lo que sale en total: cantidad por kit × kits. */
  total: number
  /** Posición y lote de donde sale (la escogida o la sugerida); null si no hay sugerencia. */
  binCode: string | null
  lotNumber: string | null
  /** true = la escogió el servidor por la rotación (sugerida); false = la escogió el usuario. */
  suggested: boolean
}

/** Resumen de un armado antes de confirmar: por pieza cuánto sale y de dónde. */
export function assembleSummary(
  pieces: readonly { productPublicId?: string; sku?: string | null; name?: string | null; quantity?: number }[],
  kits: number,
  rows: readonly StockExitOptionDto[],
  choices: Readonly<Record<string, string>>,
): AssembleSummaryRow[] {
  return pieces.map((p) => {
    const id = p.productPublicId ?? ''
    const options = exitOptionsOf(rows, id)
    const chosen = parseChoiceKey(choices[id] ?? AUTO_CHOICE)
    const at = chosen ? options.find((o) => (o.binId ?? null) === chosen.binId && (o.lotId ?? null) === chosen.lotId) : options[0]
    return {
      productPublicId: id,
      sku: p.sku ?? '',
      name: p.name ?? '',
      qtyPerKit: p.quantity ?? 0,
      total: round6((p.quantity ?? 0) * kits),
      binCode: at?.binCode ?? null,
      lotNumber: at?.lotNumber ?? null,
      suggested: chosen === null,
    }
  })
}

/** Todos los mensajes de un error del servidor (el título y, sin repetir, los de cada pieza): «con ese lote alcanzan N» va tal cual. */
export function problemMessages(err: { title?: string; message?: string; errors?: Record<string, string[]> } | null | undefined): string[] {
  if (!err) return []
  const all = [err.title ?? err.message ?? '', ...Object.values(err.errors ?? {}).flat()].map((m) => m.trim()).filter(Boolean)
  return [...new Set(all)]
}

// ---------------------------------------------------------------- desarmar

export interface ReturnRow {
  productPublicId: string
  sku: string
  name: string
  lotNumber: string | null
  /** Posición a la que vuelve (la de donde salió, salvo que se indique otra). */
  binCode: string
  quantity: number
}

/** Lo que vuelve de cada pieza al desarmar `kits` de un armado: a su mismo lote, repartido entre las líneas como el servidor. */
export function disassembleReturns(assembly: Pick<KitAssemblyDto, 'lines'>, kits: number, toBinCode?: string | null): ReturnRow[] {
  const lines = assembly.lines ?? []
  const groups = new Map<string, KitAssemblyLineDto[]>()
  for (const l of lines) {
    const id = l.productPublicId ?? ''
    groups.set(id, [...(groups.get(id) ?? []), l])
  }
  const out: ReturnRow[] = []
  for (const [id, group] of groups) {
    const back = round6((group[0].qtyPerKit ?? 0) * kits)
    for (const [i, qty] of splitReturn(back, group.map((l) => ({ taken: l.quantity ?? 0, returned: l.returnedQty ?? 0 })))) {
      const l = group[i]
      out.push({ productPublicId: id, sku: l.sku ?? '', name: l.name ?? '', lotNumber: l.lotNumber ?? null, binCode: toBinCode || l.fromBinCode || '', quantity: qty })
    }
  }
  return out
}

/** Cuerpo de `POST …/assemblies/{id}/disassemble`: sin almacén (o el del armado) ni posición, las piezas vuelven a donde salieron. */
export function disassembleRequest(v: {
  quantity: number | null
  warehousePublicId: string | null
  assemblyWarehousePublicId: string | null | undefined
  toBinId: string
  notes: string
}): Schemas['KitDisassembleRequest'] {
  const otherWarehouse = Boolean(v.warehousePublicId) && v.warehousePublicId !== v.assemblyWarehousePublicId
  return {
    quantity: v.quantity,
    warehousePublicId: otherWarehouse ? v.warehousePublicId : null,
    toBinId: v.toBinId ? Number(v.toBinId) : null,
    notes: v.notes.trim() || null,
  }
}

/** ¿Se puede desarmar algo de este armado? (lo que queda; si están reservados lo dice el servidor). */
export const canDisassemble = (a: Pick<KitAssemblyDto, 'remaining'>) => (a.remaining ?? 0) > 0
