// Lote 8A-app — reglas puras de la captura de Recibir (docs/mobile/app-almacen-plan.md §2, pantalla 3). Sin base de
// datos ni API: solo construye y valida lo que se ve en pantalla, para poder probarlo sin montar nada.
// Lote 16 (recibo directo a posición): en un almacén "Directo a posición" cada línea lleva la posición destino escaneada
// (targetBinCode) y el recibo manda su modo; un recibo sin modo (abierto con una app anterior) entra "con acomodo" (D9-A).
import { parseDateInput } from '../../kernel/format/format'
import type { FormatSettings } from '../../kernel/format/settings'
import { getFormatSettings } from '../../kernel/format/store'
import { chunkAt, distSummary, maxBins } from '../putaway/putawayLogic'
import type { TrackingType } from './localLookup'

/** Modos de recepción del almacén (LookupCode ReceivingMode). */
export type ReceivingMode = 'PUTAWAY' | 'DIRECT'

/** Normaliza lo que llega del registro/heartbeat: cualquier otro valor (o nada) = sin modo conocido. */
export function parseReceivingMode(value: string | null | undefined): ReceivingMode | null {
  const v = value?.trim().toUpperCase()
  return v === 'PUTAWAY' || v === 'DIRECT' ? v : null
}

export interface DraftLine {
  productPublicId: string
  sku: string
  productName: string
  trackingTypeCode: TrackingType
  receivedQty: number
  lotNumber: string | null
  expiryDate: string | null
  serialNumbers: string[] | null
  /** Lote 16: posición destino (solo en recibo directo); null con acomodo. */
  targetBinCode: string | null
  /** 2026-10-08: de lo recibido, las unidades dañadas (0 = ninguna), su causa del catálogo, el comentario (causa Otra), la posición donde se dejan
   *  (null = que decida el servidor) o si se desechan de una vez. */
  damagedQty: number
  damageCause: string | null
  damageNote: string | null
  damageBinCode: string | null
  damageDiscard: boolean
  /** A dónde va si se le da salida (catálogo DamageFinalDestination); null = sin dato. */
  damageDestination: string | null
}

export interface LineDraft {
  productPublicId: string
  sku: string
  productName: string
  trackingTypeCode: TrackingType
  qtyText: string
  lot: string
  expiry: string
  serials: string[]
  /** Lote 16: posición destino ya validada (validateTargetBin); null mientras no se escanee. */
  targetBinCode: string | null
  /** 2026-10-08: «Vinieron unidades dañadas»: cuántas, causa del catálogo y, si la causa es Otra, el comentario. */
  damaged: boolean
  damagedQtyText: string
  damageCause: string
  damageNote: string
}

/** Causas del daño (catálogo DamageCause); «Otra» pide el comentario. */
export const DAMAGE_CAUSE_OTHER = 'OTHER'

export function newLineDraft(product: { publicId: string; sku: string; name: string; trackingTypeCode: TrackingType }): LineDraft {
  return {
    productPublicId: product.publicId,
    sku: product.sku,
    productName: product.name,
    trackingTypeCode: product.trackingTypeCode,
    qtyText: '1',
    lot: '',
    expiry: '',
    serials: [],
    targetBinCode: null,
    damaged: false,
    damagedQtyText: '1',
    damageCause: '',
    damageNote: '',
  }
}

function parseQty(text: string): number {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : 0
}

/** Cantidad del borrador tal como quedaría en la línea (series = cuántas; si no, la escrita; 0 si no es válida). */
export function draftQuantity(draft: LineDraft): number {
  return requiresSerials(draft) ? draft.serials.length : parseQty(draft.qtyText)
}

/** Unidades dañadas declaradas (0 si no se marcó o la cantidad no es válida). Los productos con serie no declaran daño aquí. */
export function draftDamagedQty(draft: LineDraft): number {
  return draft.damaged && !requiresSerials(draft) ? parseQty(draft.damagedQtyText) : 0
}

export type DamageBlock = 'qty' | 'cause' | 'note' | null

/** Qué le falta al daño declarado (null = completo o no se marcó): cantidad mayor que 0 y no mayor que lo recibido, causa y, si es Otra, el comentario. */
export function damageBlock(draft: LineDraft): DamageBlock {
  if (!draft.damaged || requiresSerials(draft)) return null
  const damaged = parseQty(draft.damagedQtyText)
  if (damaged <= 0 || damaged > parseQty(draft.qtyText)) return 'qty'
  if (!draft.damageCause) return 'cause'
  if (draft.damageCause === DAMAGE_CAUSE_OTHER && !draft.damageNote.trim()) return 'note'
  return null
}

/** El producto necesita lote o series antes de poder agregar la línea. */
export function requiresLot(draft: Pick<LineDraft, 'trackingTypeCode'>): boolean {
  return draft.trackingTypeCode === 'LOT'
}

export function requiresSerials(draft: Pick<LineDraft, 'trackingTypeCode'>): boolean {
  return draft.trackingTypeCode === 'SERIAL'
}

/**
 * Vencimiento escrito en el borrador → 'YYYY-MM-DD' para el API (región y formatos: se escribe en el orden de fecha de la
 * compañía, p. ej. 01/31/2027 en Puerto Rico, o en ISO 2027-01-31). '' = sin vencimiento (es opcional); null = no es una
 * fecha válida (no deja agregar la línea: el servidor rechazaría el recibo entero y en la cola ya no se corrige).
 */
export function draftExpiry(draft: Pick<LineDraft, 'expiry'>, s: FormatSettings = getFormatSettings()): string | null {
  const text = draft.expiry.trim()
  return text ? parseDateInput(text, s) : ''
}

/** ¿Está completa la captura de cantidad/lote/series? (sin mirar la posición destino). */
function hasQuantityData(draft: LineDraft): boolean {
  if (damageBlock(draft) !== null) return false
  if (requiresSerials(draft)) return draft.serials.length > 0
  if (requiresLot(draft)) return draft.lot.trim().length > 0 && parseQty(draft.qtyText) > 0 && draftExpiry(draft) !== null
  return parseQty(draft.qtyText) > 0
}

/** ¿Se puede agregar la línea tal como está? En recibo directo (`direct`) además hace falta la posición destino. */
export function canAddLine(draft: LineDraft, direct = false): boolean {
  if (!hasQuantityData(draft)) return false
  return !direct || !!draft.targetBinCode
}

/** Agrega un número de serie a la lista (sin repetir vacíos ni el mismo dos veces). */
export function addSerial(draft: LineDraft, serial: string): LineDraft {
  const value = serial.trim()
  if (!value || draft.serials.includes(value)) return draft
  return { ...draft, serials: [...draft.serials, value] }
}

export function removeSerial(draft: LineDraft, serial: string): LineDraft {
  return { ...draft, serials: draft.serials.filter((s) => s !== serial) }
}

/** Construye la línea final a partir del borrador (asume canAddLine(draft) === true). */
export function buildLine(draft: LineDraft): DraftLine {
  const targetBinCode = draft.targetBinCode?.trim() || null
  if (requiresSerials(draft)) {
    return {
      productPublicId: draft.productPublicId,
      sku: draft.sku,
      productName: draft.productName,
      trackingTypeCode: draft.trackingTypeCode,
      receivedQty: draft.serials.length,
      lotNumber: null,
      expiryDate: null,
      serialNumbers: draft.serials,
      targetBinCode,
      ...NO_DAMAGE,
    }
  }
  return {
    productPublicId: draft.productPublicId,
    sku: draft.sku,
    productName: draft.productName,
    trackingTypeCode: draft.trackingTypeCode,
    receivedQty: parseQty(draft.qtyText),
    lotNumber: requiresLot(draft) ? draft.lot.trim() : null,
    expiryDate: requiresLot(draft) ? draftExpiry(draft) || null : null,
    serialNumbers: null,
    targetBinCode,
    ...NO_DAMAGE,
  }
}

const NO_DAMAGE = { damagedQty: 0, damageCause: null, damageNote: null, damageBinCode: null, damageDiscard: false, damageDestination: null } as const

/** Dónde quedan las unidades dañadas: una posición escaneada o se desechan de una vez. */
export type DamagePlacement = { binCode: string; discard: false } | { binCode: null; discard: true; destination: string | null }

/**
 * Reparte lo dañado del borrador entre las líneas que salen de él (una por posición en un reparto): se asigna en orden, sin pasar de lo que trae cada
 * línea (el servidor no admite más dañadas que recibidas por línea). Devuelve las líneas con el daño puesto y lo que quedó sin asignar (cuando el
 * reparto no cubrió toda la cantidad, lo dañado que falta sigue en la captura).
 */
export function attachDamage(draft: LineDraft, lines: readonly DraftLine[], placement: DamagePlacement): { lines: DraftLine[]; leftover: number } {
  let remaining = draftDamagedQty(draft)
  const out = lines.map((l) => {
    const take = Math.min(remaining, l.receivedQty)
    if (take <= 0) return l
    remaining = Math.round((remaining - take) * 1000) / 1000
    return {
      ...l,
      damagedQty: take,
      damageCause: draft.damageCause,
      damageNote: draft.damageCause === DAMAGE_CAUSE_OTHER ? draft.damageNote.trim() : null,
      damageBinCode: placement.binCode,
      damageDiscard: placement.discard,
      damageDestination: placement.discard ? placement.destination : null,
    }
  })
  return { lines: out, leftover: remaining }
}

/** Posición escaneada para dejar lo dañado: cualquier posición activa del almacén (cuarentena, recepción, guardado…). */
export function validateDamageBin(code: string, localBins: readonly LocalBin[]): TargetBinCheck {
  const wanted = code.trim().toUpperCase()
  const bin = wanted ? localBins.find((b) => b.code.trim().toUpperCase() === wanted) : undefined
  if (!bin) return { ok: false, reason: 'notFound' }
  if (!bin.isActive) return { ok: false, reason: 'inactive' }
  return { ok: true, code: bin.code }
}

// ------------------------------------------------------------------ Lote 16: posición destino

/** Posición del catálogo local (tabla `bin`, bajada de GET /sync/bins) con el tipo de su zona. */
export interface LocalBin {
  code: string
  zoneTypeCode: string | null
  isActive: boolean
}

/** Tipos de zona que NO pueden ser destino (D5-A): recepción y cruce de muelle. La cuarentena sí puede. */
export const NON_TARGET_ZONE_TYPES: readonly string[] = ['STAGING', 'CROSSDOCK']

export type TargetBinCheck =
  | { ok: true; code: string }
  | { ok: false; reason: 'notFound' | 'inactive' | 'notStorage' }

/**
 * Valida la posición destino escaneada contra las posiciones locales del almacén del recibo (las que coinciden con el
 * código; se compara sin distinguir mayúsculas, como el servidor). Existe → activa (posición, zona y almacén) → zona de
 * guardado (no STAGING ni CROSSDOCK). Devuelve el código tal como está en el catálogo.
 */
export function validateTargetBin(code: string, localBins: readonly LocalBin[]): TargetBinCheck {
  const wanted = code.trim().toUpperCase()
  const bin = wanted ? localBins.find((b) => b.code.trim().toUpperCase() === wanted) : undefined
  if (!bin) return { ok: false, reason: 'notFound' }
  if (!bin.isActive) return { ok: false, reason: 'inactive' }
  if (bin.zoneTypeCode && NON_TARGET_ZONE_TYPES.includes(bin.zoneTypeCode.toUpperCase())) return { ok: false, reason: 'notStorage' }
  return { ok: true, code: bin.code }
}

/**
 * Reparto en recibo directo (tarea 24c): una línea por posición con la misma cantidad (la última posición no recibe de más: solo caben
 * posiciones llenas, como en Acomodar). Devuelve las líneas y lo que queda sin ubicar de la captura (los sueltos).
 */
export function splitDraftLines(draft: LineDraft, perBin: number, codes: readonly string[]): { lines: DraftLine[]; left: number } {
  const total = draftQuantity(draft)
  const lines = codes.map((code, i) => buildLine({ ...draft, qtyText: String(chunkAt(total, perBin, i)), targetBinCode: code }))
  return { lines, left: distSummary(total, perBin, codes.length).left }
}

export type AddSplitResult = { ok: true; codes: string[] } | { ok: false; reason: 'duplicate' | 'full' }

/** Suma una posición al reparto del recibo: no repetida y dentro de las posiciones llenas que caben. */
export function addSplitBin(codes: readonly string[], code: string, total: number, perBin: number): AddSplitResult {
  if (codes.some((c) => c.toUpperCase() === code.toUpperCase())) return { ok: false, reason: 'duplicate' }
  if (codes.length >= maxBins(total, perBin)) return { ok: false, reason: 'full' }
  return { ok: true, codes: [...codes, code] }
}

/** Líneas de un recibo directo que todavía no tienen posición destino (no debería pasar: el paso la exige). */
export function linesMissingTarget(lines: readonly DraftLine[]): number {
  return lines.filter((l) => !l.targetBinCode).length
}

/** Línea del documento (aviso u orden de compra) tal como la tiene el aparato: producto y lote esperado. */
export interface DocLine {
  productPublicId: string
  lotNumber: string | null
}

export interface TargetConflict {
  /** Índice de la línea capturada que choca. */
  index: number
  sku: string
  /** Posición que ya tiene la línea del documento con la que se sumaría. */
  bin: string
}

const sameLot = (a: string | null, b: string | null) => (a ?? '').trim().toUpperCase() === (b ?? '').trim().toUpperCase()

/**
 * H11: en un recibo con aviso u orden de compra el servidor aplica cada línea capturada sobre una línea libre del
 * documento del mismo producto (primero la del mismo lote, luego una sin lote) y, si ya no queda libre, la SUMA a la
 * ya aplicada del mismo producto (y lote); si esa ya tiene otra posición destino rechaza el recibo completo (400,
 * ReceivingModeRules.SameProductOtherTarget). Esta función repite ese reparto con las líneas del documento que tiene el
 * aparato para avisar ANTES de mandarlo (un envío rechazado en la cola ya no se edita). Producto que no está en el
 * documento = línea extra: nunca choca.
 */
export function findTargetConflict(docLines: readonly DocLine[], lines: readonly DraftLine[]): TargetConflict | null {
  const applied = new Map<number, { lot: string | null; target: string | null }>()
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    const free = (j: number) => !applied.has(j) && docLines[j].productPublicId === line.productPublicId
    const indexes = docLines.map((_, j) => j)
    const match = line.lotNumber
      ? (indexes.find((j) => free(j) && sameLot(docLines[j].lotNumber, line.lotNumber)) ??
        indexes.find((j) => free(j) && !docLines[j].lotNumber))
      : indexes.find(free)
    if (match !== undefined) {
      applied.set(match, { lot: line.lotNumber ?? docLines[match].lotNumber, target: line.targetBinCode })
      continue
    }
    const same = indexes.find(
      (j) =>
        applied.has(j) &&
        docLines[j].productPublicId === line.productPublicId &&
        (!line.lotNumber || sameLot(applied.get(j)!.lot, line.lotNumber)),
    )
    if (same === undefined) continue
    const current = applied.get(same)!
    if (line.targetBinCode && current.target && line.targetBinCode.toUpperCase() !== current.target.toUpperCase()) {
      return { index: i, sku: line.sku, bin: current.target }
    }
    current.target ??= line.targetBinCode
  }
  return null
}

// ------------------------------------------------------------------ cuerpo del envío

export interface ReceiptDoc {
  purchaseOrderPublicId?: string | null
  asnId?: number | null
}

/**
 * Cuerpo de POST /api/v1/receipts en una sola llamada (confirm:true), decisión 2 de docs/lote8A-decisiones.md. Lote 16:
 * `receivingMode` es el modo con que se abrió el recibo en el aparato (null = recibo de antes de actualizar: el servidor
 * lo trata "con acomodo") y cada línea lleva su `targetBinCode` (null con acomodo).
 */
export function buildReceiptBody(
  warehousePublicId: string,
  doc: ReceiptDoc | null,
  lines: DraftLine[],
  receivingMode: ReceivingMode | null = null,
) {
  return {
    warehousePublicId,
    purchaseOrderPublicId: doc?.purchaseOrderPublicId ?? null,
    asnId: doc?.asnId ?? null,
    confirm: true,
    receivingMode,
    lines: lines.map((l) => ({
      productPublicId: l.productPublicId,
      receivedQty: l.receivedQty,
      lot: l.lotNumber ? { number: l.lotNumber, expiryDate: l.expiryDate || null } : null,
      serialNumbers: l.serialNumbers,
      targetBinCode: receivingMode === 'DIRECT' ? l.targetBinCode : null,
      damagedQty: l.damagedQty > 0 ? l.damagedQty : null,
      damageCause: l.damagedQty > 0 ? l.damageCause : null,
      damageNote: l.damagedQty > 0 ? l.damageNote : null,
      damageBinCode: l.damagedQty > 0 && !l.damageDiscard ? l.damageBinCode : null,
      damageDiscard: l.damagedQty > 0 && l.damageDiscard ? true : null,
      damageFinalDestination: l.damagedQty > 0 && l.damageDiscard ? l.damageDestination : null,
    })),
  }
}
