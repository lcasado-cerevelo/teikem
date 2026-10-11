// Lote 13 (plan de cambios, lote 3) — lógica pura del panel "Recolección" de Recolección y empaque (`CollectPanel.tsx`).
// La rejilla de líneas siempre deja una fila vacía al final; al grabar se compacta (se ignoran las filas vacías), el cuerpo
// del POST /api/v1/pick-batches lleva solo las líneas con datos y los errores del servidor (`lines[k].campo`) se devuelven a
// la fila de la pantalla con `indexMap`. Las reglas por línea son las de `lineRules.ts` (réplica de PickBatchRules): el
// servidor vuelve a validar todo. `fefoCandidates` replica `PickBatchRules.Eligible` para sugerir la posición.
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import { firstOtherOwner, parseSerials, pickDuplicateAcrossLines, pickLineIssues, remapProblemFields, type LineIssue } from './lineRules'
import { binStockOptions, type BinStock } from './movementForms'

type Schemas = components['schemas']
type BalanceDto = Schemas['BalanceDto']
export type PickBatchCreateRequest = Schemas['PickBatchCreateRequest']

/** Tope de líneas de una recolección (PickBatchRules.MaxLines). */
export const MAX_PICK_LINES = 100
/** Tope de la nota libre de un despacho manual (PickBatchRules.ManualNoteMax). */
export const MANUAL_NOTE_MAX = 500
/** Dueño "propio" (producto sin cliente) en la regla de un solo dueño. */
export const OWN = 'OWN'

export interface CollectLine {
  productPublicId: string | null
  sku: string
  trackingTypeCode: string
  /** '' = sin producto; OWN = propio; si no, publicId del cliente dueño. */
  owner: string
  /** Disponible del producto en el almacén al elegirlo (pista "N disp."); null = sin dato. */
  available: number | null
  quantity: number | null
  /** Id de la posición como texto ('' = el servidor elige por FEFO). */
  binId: string
  /** Id del lote como texto ('' = FEFO). */
  lotId: string
  /** Series escaneadas: una por renglón o separadas por coma. */
  serialNumbers: string
}

export interface CollectFormValues {
  warehousePublicId: string | null
  lines: CollectLine[]
  /** Solo del despacho manual: código del motivo (catálogo ManualIssueReason; '' = sin elegir). */
  reasonCode?: string
  /** Solo del despacho manual: nota libre (máx. 500). */
  note?: string
}

/** Línea tal como la entrega el formulario o zod (sin `strictNullChecks`, los campos anulables salen opcionales). */
export type CollectLineLike = Partial<CollectLine>

export const EMPTY_COLLECT_LINE: CollectLine = {
  productPublicId: null,
  sku: '',
  trackingTypeCode: '',
  owner: '',
  available: null,
  quantity: null,
  binId: '',
  lotId: '',
  serialNumbers: '',
}

/** Fila sin nada capturado (la fila vacía automática): no se envía ni se valida. */
export function isBlankLine(line: CollectLineLike): boolean {
  return !line.productPublicId && line.quantity == null && !line.binId && !line.lotId && parseSerials(line.serialNumbers).length === 0
}

/** ¿Hay que agregar una fila vacía al final? (la última tiene datos y no se llegó al tope). */
export function needsTrailingBlank(lines: readonly CollectLineLike[], max = MAX_PICK_LINES): boolean {
  if (lines.length >= max) return false
  const last = lines[lines.length - 1]
  return !last || !isBlankLine(last)
}

/**
 * Líneas con datos, en orden, y `indexMap[k]` = fila de la pantalla de la k-ésima línea enviada (para devolver a su fila
 * los errores `lines[k]` del servidor).
 */
export function compactPickLines<T extends CollectLineLike>(lines: readonly T[]): { lines: T[]; indexMap: number[] } {
  const kept: T[] = []
  const indexMap: number[] = []
  lines.forEach((l, i) => {
    if (isBlankLine(l)) return
    kept.push(l)
    indexMap.push(i)
  })
  return { lines: kept, indexMap }
}

/** Cuerpo de `POST /api/v1/pick-batches` (solo las líneas con datos) + el mapa de índices. */
export function buildCollectBody(values: { warehousePublicId?: string | null; lines: readonly CollectLineLike[] }): { body: PickBatchCreateRequest; indexMap: number[] } {
  const { lines, indexMap } = compactPickLines(values.lines)
  return {
    body: {
      warehousePublicId: values.warehousePublicId ?? null,
      lines: lines.map((l) => {
        const serials = parseSerials(l.serialNumbers)
        return {
          productPublicId: l.productPublicId ?? null,
          quantity: l.quantity ?? null,
          binId: l.binId ? Number(l.binId) : null,
          lotId: l.lotId ? Number(l.lotId) : null,
          serialNumbers: serials.length > 0 ? serials : null,
        }
      }),
    },
    indexMap,
  }
}

const SERVER_LINE = /^(?:\$\.)?lines\[(\d+)\](?:\.(\w+))?$/i

/**
 * Errores del servidor sobre las líneas enviadas → nombres de campo de la rejilla: `lines[k].quantity` →
 * `lines.<fila>.quantity`; `lines[k]` sin campo (existencia insuficiente, línea nula) → la cantidad de esa fila. `lines`
 * (un solo dueño) y los demás quedan igual (sin campo: van al aviso de arriba del formulario).
 */
export function remapCollectErrors(err: unknown, indexMap: readonly number[]): unknown {
  return remapProblemFields(err, (field) => {
    const m = SERVER_LINE.exec(field)
    if (!m) return null
    const row = indexMap[Number(m[1])]
    if (row === undefined) return null
    const name = m[2] ? m[2].charAt(0).toLowerCase() + m[2].slice(1) : 'quantity'
    return `lines.${row}.${name}`
  })
}

/** Filtro de dueño para el selector de producto de la fila `index`: el de la primera OTRA fila con producto. */
export function ownerFilterFor(lines: readonly (CollectLineLike | undefined)[], index: number): { ownOnly?: boolean; ownerClientPublicId?: string } {
  const other = lines.find((l, i) => i !== index && Boolean(l?.owner))
  if (!other) return {}
  return other.owner === OWN ? { ownOnly: true } : { ownerClientPublicId: other.owner ?? undefined }
}

// ---------------------------------------------------------------------------------------------------------------------
// Validación (zod) con los mensajes exactos del manual 06 §7
// ---------------------------------------------------------------------------------------------------------------------

type Translate = (key: string, params?: Record<string, string | number>) => string

const lineSchema = z.object({
  productPublicId: z.string().nullable(),
  sku: z.string(),
  trackingTypeCode: z.string(),
  owner: z.string(),
  available: z.number().nullable(),
  quantity: z.number().nullable(),
  binId: z.string(),
  lotId: z.string(),
  serialNumbers: z.string(),
})

/**
 * Esquema del panel: almacén obligatorio; al menos una línea con datos (el error va bajo el producto de la primera fila) y
 * como máximo 100; por línea con datos, producto obligatorio y `pickLineIssues`; series repetidas entre líneas y un solo
 * dueño. Las filas vacías no se validan.
 */
export function collectSchema(t: Translate, issueText: (issue: LineIssue) => string, opts: { manual?: boolean } = {}) {
  return z
    .object({ warehousePublicId: z.string().nullable(), lines: z.array(lineSchema), reasonCode: z.string().optional(), note: z.string().optional() })
    .superRefine((v, ctx) => {
      // Despacho manual (2026-10-11): motivo obligatorio y nota de máx. 500 (mensajes del servidor, PickBatchRules)
      if (opts.manual) {
        if (!v.reasonCode?.trim()) ctx.addIssue({ code: 'custom', path: ['reasonCode'], message: t('warehouse.manualIssues.errors.reasonRequired') })
        if ((v.note ?? '').trim().length > MANUAL_NOTE_MAX) ctx.addIssue({ code: 'custom', path: ['note'], message: t('warehouse.manualIssues.errors.noteTooLong') })
      }
      if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
      const { lines, indexMap } = compactPickLines(v.lines)
      if (lines.length === 0) {
        ctx.addIssue({ code: 'custom', path: ['lines', 0, 'productPublicId'], message: t('warehouse.lineRules.pickLinesRequired') })
        return
      }
      if (lines.length > MAX_PICK_LINES)
        ctx.addIssue({ code: 'custom', path: ['lines', indexMap[MAX_PICK_LINES], 'productPublicId'], message: t('warehouse.lineRules.pickTooManyLines') })
      const serialsByLine = lines.map((l) => parseSerials(l.serialNumbers))
      lines.forEach((l, k) => {
        const row = indexMap[k]
        if (!l.productPublicId) {
          ctx.addIssue({ code: 'custom', path: ['lines', row, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
          return
        }
        for (const issue of pickLineIssues({ sku: l.sku ?? '', trackingTypeCode: l.trackingTypeCode, quantity: l.quantity, hasLot: Boolean(l.lotId), serials: serialsByLine[k] })) {
          ctx.addIssue({ code: 'custom', path: ['lines', row, issue.field], message: issueText(issue) })
        }
      })
      const dup = pickDuplicateAcrossLines(serialsByLine)
      if (dup)
        ctx.addIssue({
          code: 'custom',
          path: ['lines', indexMap[dup[0]], 'serialNumbers'],
          message: issueText({ field: 'serialNumbers', code: 'pickSerialDuplicated', params: { serial: dup[1] } }),
        })
      const other = firstOtherOwner(lines.map((l) => (!l.owner ? undefined : l.owner === OWN ? null : l.owner)))
      if (other != null) ctx.addIssue({ code: 'custom', path: ['lines', indexMap[other], 'productPublicId'], message: t(opts.manual ? 'warehouse.manualIssues.errors.singleOwner' : 'warehouse.lineRules.singleOwner') })
    })
}

// ---------------------------------------------------------------------------------------------------------------------
// FEFO sugerido (réplica de PickBatchRules.Eligible sobre los saldos del API)
// ---------------------------------------------------------------------------------------------------------------------

/** Rango de zona para el desempate FEFO: PICKING 0, RESERVE 1, REFRIGERATED 2, STAGING 3, otra o sin tipo 4. */
export function zoneRank(zoneTypeCode: string | null | undefined): number {
  switch ((zoneTypeCode ?? '').toUpperCase()) {
    case 'PICKING':
      return 0
    case 'RESERVE':
      return 1
    case 'REFRIGERATED':
      return 2
    case 'STAGING':
      return 3
    default:
      return 4
  }
}

/** ¿Se recolecta de este tipo de zona? QUARANTINE, CROSSDOCK y RENTAL (equipos en renta, Lote 27) nunca: espejo de PickBatchRules.IsPickableZone. */
export function isPickableZone(zoneTypeCode: string | null | undefined): boolean {
  const z = (zoneTypeCode ?? '').toUpperCase()
  return z !== 'QUARANTINE' && z !== 'CROSSDOCK' && z !== 'RENTAL'
}

/** Comparación ordinal (como StringComparer.Ordinal), sin reglas del idioma. */
function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0
}

/**
 * Saldos elegibles en orden FEFO: con posición, zona recolectable y disponible > 0 (lo reservado no se recolecta);
 * vencimiento ascendente (sin fecha al final), zona, código de posición, id de posición y lote. Con `lotId`, solo ese lote.
 */
export function fefoCandidates(balances: readonly BalanceDto[], lotId?: number | null): BalanceDto[] {
  return balances
    .filter((b) => b.binId != null && isPickableZone(b.zoneTypeCode) && (b.qtyAvailable ?? 0) > 0)
    .filter((b) => lotId == null || b.lotId === lotId)
    .slice()
    .sort((a, b) => {
      const ea = a.expiryDate ?? null
      const eb = b.expiryDate ?? null
      if ((ea === null) !== (eb === null)) return ea === null ? 1 : -1
      if (ea !== null && eb !== null && ea !== eb) return ordinal(ea, eb)
      const z = zoneRank(a.zoneTypeCode) - zoneRank(b.zoneTypeCode)
      if (z !== 0) return z
      const c = ordinal(a.binCode ?? '', b.binCode ?? '')
      if (c !== 0) return c
      const i = (a.binId ?? 0) - (b.binId ?? 0)
      if (i !== 0) return i
      return (a.lotId ?? 0) - (b.lotId ?? 0)
    })
}

/** Ids de posición en orden FEFO, sin repetir (para `suggestedBinIds` del selector de posición). */
export function fefoBinSuggestions(balances: readonly BalanceDto[], lotId?: number | null): number[] {
  const seen = new Set<number>()
  const out: number[] = []
  for (const b of fefoCandidates(balances, lotId)) {
    const id = b.binId as number
    if (seen.has(id)) continue
    seen.add(id)
    out.push(id)
  }
  return out
}

/**
 * Posiciones que ofrece el selector "Posición" de una línea: solo donde el producto (y el lote, si se eligió) tiene
 * disponible recolectable, una por posición con su disponible, en el orden FEFO del servidor (la primera es la sugerida).
 */
export function fefoBinOptions(balances: readonly BalanceDto[], lotId?: number | null): BinStock[] {
  return binStockOptions(fefoCandidates(balances, lotId))
}

/** Disponible recolectable (suma de los saldos elegibles). */
export function fefoAvailable(balances: readonly BalanceDto[], lotId?: number | null): number {
  return fefoCandidates(balances, lotId).reduce((s, b) => s + (b.qtyAvailable ?? 0), 0)
}

// ---------------------------------------------------------------------------------------------------------------------
// Plan de salida (pedido del dueño 2026-10-07; en la app ya existía): dada la cantidad de un producto, de dónde sacarla siguiendo el orden FEFO del
// servidor (20 de A-01 y 30 de B-03), descontando lo que otras líneas del mismo despacho ya toman de esas posiciones.
// ---------------------------------------------------------------------------------------------------------------------
export interface PlanPart {
  binId: number
  binCode: string
  lotId: number | null
  lotNumber: string | null
  qty: number
}

/** Lo que otra línea del despacho ya toma de una posición (y lote); lotId null = sin lote elegido. */
export interface TakenQty {
  binId: number
  lotId: number | null
  qty: number
}

export function planCollect(
  balances: readonly BalanceDto[],
  qty: number,
  opts: { lotId?: number | null; taken?: readonly TakenQty[] } = {},
): { parts: PlanPart[]; short: number } {
  const parts: PlanPart[] = []
  let left = Math.max(0, qty)
  const taken = (opts.taken ?? []).map((t) => ({ ...t }))
  for (const b of fefoCandidates(balances, opts.lotId ?? null)) {
    if (left <= 0) break
    let avail = b.qtyAvailable ?? 0
    // lo que otras líneas ya toman de esta posición (de este lote, o de cualquiera si no tenían lote elegido)
    for (const t of taken) {
      if (t.qty <= 0 || t.binId !== b.binId || (t.lotId != null && t.lotId !== (b.lotId ?? null))) continue
      const used = Math.min(t.qty, avail)
      t.qty -= used
      avail -= used
    }
    if (avail <= 0) continue
    const take = Math.min(avail, left)
    parts.push({ binId: b.binId as number, binCode: b.binCode ?? '', lotId: b.lotId ?? null, lotNumber: b.lotNumber ?? null, qty: take })
    left = Math.round((left - take) * 1000) / 1000
  }
  return { parts, short: left }
}
