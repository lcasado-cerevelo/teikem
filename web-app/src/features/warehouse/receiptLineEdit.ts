// Lote 13 — Recibo: lógica pura de la rejilla de líneas del detalle (`ReceiptLinesEditor`). Cada fila guarda lo que se
// teclea como texto (`expected`, `received`) junto con lo guardado (`original`), para saber qué mandar al API y qué falta
// por guardar. Reglas:
// - Con documento (aviso u orden de compra) lo esperado es de solo lectura y solo se captura lo recibido (decisión 6).
// - Sin documento (ciego o devolución): producto, esperado y recibido; siempre una fila vacía al final (hasta 200).
// - Copia recibido → esperado SOLO DE INTERFAZ mientras se teclea: `mirror` arranca en true si el esperado estaba vacío o
//   en 0 y se recalcula al teclear el esperado (`onExpectedInput`).
// - `linePayload` arma el POST (fila nueva) o el PUT con solo lo que cambió; `confirmBlockers` dice por qué no se puede
//   confirmar. Mensajes: claves i18n (`warehouse.lineRules.*` / `warehouse.receipts.*`) o el texto exacto del servidor.
// Pruebas en receiptLineEdit.test.ts.
import type { components } from '../../kernel/api/schema'
import { decimalsOf, QTY_LIMIT } from './lineRules'

type Schemas = components['schemas']
type LineDto = Schemas['ReceiptLineDto']

/** Máximo de líneas de un recibo (ReceiptRules.MaxLines). */
export const MAX_RECEIPT_LINES = 200

export type RowField = 'product' | 'expected' | 'received' | 'row'
/** Errores de una fila por campo: texto ya traducido (o el del servidor). */
export type RowErrors = Partial<Record<RowField, string>>

export interface LineRow {
  /** Clave local estable (no cambia al guardarse la fila: el foco no se pierde). */
  key: string
  /** Id de la línea guardada; null = fila nueva sin guardar. */
  lineId: number | null
  asnLineId: number | null
  productPublicId: string | null
  sku: string
  productName: string
  trackingTypeCode: string
  /** Lo tecleado ('' = vacío). */
  expected: string
  received: string
  /** Copiar lo recibido al esperado mientras se teclea. */
  mirror: boolean
  lotNumber: string | null
  serialCount: number
  allocatedToCrossDock: number
  /** Lo guardado en el servidor (null = fila nueva). */
  original: { productPublicId: string | null; expected: number | null; received: number | null } | null
  errors: RowErrors
  saving: boolean
}

// ---------------------------------------------------------------------------------------------------------------------
// Texto ↔ número
// ---------------------------------------------------------------------------------------------------------------------

/** Texto de una cantidad: '' = null; coma o punto decimal; texto que no es número = NaN. */
export function parseQtyText(text: string): number | null {
  const s = text.trim().replace(',', '.')
  if (!s) return null
  const n = Number(s)
  return Number.isFinite(n) ? n : Number.NaN
}

export function qtyText(n: number | null | undefined): string {
  return n == null ? '' : String(n)
}

function blankOrZero(text: string): boolean {
  const n = parseQtyText(text)
  return n === null || n === 0
}

// ---------------------------------------------------------------------------------------------------------------------
// Filas
// ---------------------------------------------------------------------------------------------------------------------

export function emptyRow(key: string): LineRow {
  return {
    key,
    lineId: null,
    asnLineId: null,
    productPublicId: null,
    sku: '',
    productName: '',
    trackingTypeCode: '',
    expected: '',
    received: '',
    mirror: true,
    lotNumber: null,
    serialCount: 0,
    allocatedToCrossDock: 0,
    original: null,
    errors: {},
    saving: false,
  }
}

/** Fila a partir de una línea guardada. */
export function rowFromLine(line: LineDto, key = `line-${line.id ?? 0}`): LineRow {
  const expected = qtyText(line.expectedQty)
  return {
    key,
    lineId: line.id ?? null,
    asnLineId: line.asnLineId ?? null,
    productPublicId: line.productPublicId ?? null,
    sku: line.sku ?? '',
    productName: line.productName ?? '',
    trackingTypeCode: line.trackingTypeCode ?? 'NONE',
    expected,
    received: qtyText(line.receivedQty ?? 0),
    mirror: blankOrZero(expected),
    lotNumber: line.lotNumber ?? null,
    serialCount: line.serialNumbers?.length ?? 0,
    allocatedToCrossDock: line.allocatedToCrossDock ?? 0,
    original: { productPublicId: line.productPublicId ?? null, expected: line.expectedQty ?? null, received: line.receivedQty ?? 0 },
    errors: {},
    saving: false,
  }
}

/** Fila nueva sin nada capturado (la del final de un recibo sin documento). */
export function isEmptyRow(row: LineRow): boolean {
  return row.lineId === null && !row.productPublicId && row.expected.trim() === '' && row.received.trim() === ''
}

/** Siempre una fila vacía al final (sin pasar de 200 filas). */
export function ensureTrailingEmpty(rows: readonly LineRow[], newKey: () => string): LineRow[] {
  const out = [...rows]
  const last = out[out.length - 1]
  if ((!last || !isEmptyRow(last)) && out.length < MAX_RECEIPT_LINES) out.push(emptyRow(newKey()))
  return out
}

export interface RowsMode {
  /** Sin documento (ciego o devolución): producto y esperado editables, fila vacía al final. */
  manual: boolean
  /** Abierto y con warehouse.receive. */
  editable: boolean
}

/** Filas iniciales de un recibo: sus líneas (por id) y, si se capturan a mano, una fila vacía al final. */
export function rowsFromDetail(lines: readonly LineDto[] | null | undefined, mode: RowsMode, newKey: () => string): LineRow[] {
  const rows = [...(lines ?? [])].sort((a, b) => (a.id ?? 0) - (b.id ?? 0)).map((l) => rowFromLine(l))
  return mode.manual && mode.editable ? ensureTrailingEmpty(rows, newKey) : rows
}

// ---------------------------------------------------------------------------------------------------------------------
// Captura
// ---------------------------------------------------------------------------------------------------------------------

function withoutErrors(errors: RowErrors, ...fields: RowField[]): RowErrors {
  const next = { ...errors }
  for (const f of fields) delete next[f]
  return next
}

/** Se teclea lo recibido: si la fila copia (`mirror`) y el recibo no tiene documento (`manual`), el esperado sigue a lo
 *  recibido mientras se teclea. */
export function onReceivedInput(row: LineRow, text: string, manual = true): LineRow {
  const copy = manual && row.mirror
  return {
    ...row,
    received: text,
    expected: copy ? text : row.expected,
    errors: withoutErrors(row.errors, 'received', 'row', ...(copy ? (['expected'] as RowField[]) : [])),
  }
}

/** Se teclea el esperado: vuelve a copiar solo si queda vacío o en 0. */
export function onExpectedInput(row: LineRow, text: string): LineRow {
  return { ...row, expected: text, mirror: blankOrZero(text), errors: withoutErrors(row.errors, 'expected', 'row') }
}

/** Se elige (o se quita) el producto de la fila. */
export function onProductPicked(
  row: LineRow,
  product: { publicId?: string | null; sku?: string | null; name?: string | null; trackingTypeCode?: string | null } | null,
): LineRow {
  return {
    ...row,
    productPublicId: product?.publicId ?? null,
    sku: product?.sku ?? '',
    productName: product?.name ?? '',
    trackingTypeCode: product?.trackingTypeCode ?? '',
    errors: withoutErrors(row.errors, 'product', 'row'),
  }
}

function sameQty(text: string, saved: number | null): boolean {
  const n = parseQtyText(text)
  if (Number.isNaN(n)) return false
  return n === saved
}

/** ¿La fila tiene algo tecleado que no está guardado? (la fila vacía del final no cuenta; con documento el esperado no se
 *  edita y no cuenta). */
export function isDirty(row: LineRow, manual = true): boolean {
  if (row.original === null) return !isEmptyRow(row)
  return (
    row.productPublicId !== row.original.productPublicId ||
    (manual && !sameQty(row.expected, row.original.expected)) ||
    !sameQty(row.received, row.original.received)
  )
}

/** Diferencia de la fila (recibido − esperado) como en el servidor: sin esperado, un ciego espera lo recibido y una línea
 *  extra de un recibo con documento espera 0. null = sin recibido que comparar. */
export function rowVariance(row: LineRow, manual: boolean): number | null {
  const rec = parseQtyText(row.received)
  if (rec === null || Number.isNaN(rec)) return null
  const exp = parseQtyText(row.expected)
  const expected = exp === null || Number.isNaN(exp) ? (manual ? rec : 0) : exp
  return Math.round((rec - expected) * 1000) / 1000
}

// ---------------------------------------------------------------------------------------------------------------------
// Guardado
// ---------------------------------------------------------------------------------------------------------------------

/** Error de captura: clave i18n y parámetros. */
export interface RowIssue {
  key: string
  params?: Record<string, string | number>
}

function qtyIssue(n: number | null, kind: 'received' | 'expected'): RowIssue | null {
  if (n === null) return kind === 'received' ? { key: 'warehouse.lineRules.receivedQtyRequired' } : null
  if (Number.isNaN(n)) return { key: 'warehouse.receipts.lines.invalidNumber' }
  if (n < 0) return { key: kind === 'received' ? 'warehouse.lineRules.receivedQtyNegative' : 'warehouse.receipts.errors.expectedQtyNegative' }
  if (n >= QTY_LIMIT) return { key: 'warehouse.lineRules.qtyTooLarge' }
  if (decimalsOf(n) > 3) return { key: 'warehouse.lineRules.qtyDecimals' }
  return null
}

export type LinePayload =
  | { kind: 'none' }
  | { kind: 'incomplete' }
  | { kind: 'invalid'; issues: Partial<Record<RowField, RowIssue>> }
  | { kind: 'add'; body: Schemas['ReceiptLineRequest'] }
  | { kind: 'update'; lineId: number; body: Schemas['ReceiptLineUpdateRequest'] }

/**
 * Qué mandar al API por una fila. Nueva: POST cuando tiene producto y recibido (si falta alguno, `incomplete`: se espera).
 * Guardada: PUT solo con lo que cambió (`expectedQty`/`clearExpected` y `productPublicId` solo sin documento). Sin
 * cambios: `none`. Con lo tecleado inválido: `invalid` (no se manda).
 */
export function linePayload(row: LineRow, manual: boolean): LinePayload {
  const received = parseQtyText(row.received)
  const expected = parseQtyText(row.expected)

  if (row.lineId === null || row.original === null) {
    if (isEmptyRow(row)) return { kind: 'none' }
    if (!row.productPublicId || received === null) return { kind: 'incomplete' }
    const issues: Partial<Record<RowField, RowIssue>> = {}
    const ri = qtyIssue(received, 'received')
    if (ri) issues.received = ri
    const ei = manual ? qtyIssue(expected, 'expected') : null
    if (ei) issues.expected = ei
    if (Object.keys(issues).length > 0) return { kind: 'invalid', issues }
    const body: Schemas['ReceiptLineRequest'] = { productPublicId: row.productPublicId, receivedQty: received }
    if (manual && expected !== null) body.expectedQty = expected
    return { kind: 'add', body }
  }

  const issues: Partial<Record<RowField, RowIssue>> = {}
  if (!row.productPublicId) issues.product = { key: 'warehouse.receipts.errors.productRequired' }
  const ri = qtyIssue(received, 'received')
  if (ri) issues.received = ri
  const ei = manual ? qtyIssue(expected, 'expected') : null
  if (ei) issues.expected = ei
  if (Object.keys(issues).length > 0) return { kind: 'invalid', issues }

  const body: Schemas['ReceiptLineUpdateRequest'] = {}
  if (received !== row.original.received) body.receivedQty = received
  if (manual && expected !== row.original.expected) {
    if (expected === null) body.clearExpected = true
    else body.expectedQty = expected
  }
  if (manual && row.asnLineId === null && row.productPublicId !== row.original.productPublicId) body.productPublicId = row.productPublicId
  return Object.keys(body).length === 0 ? { kind: 'none' } : { kind: 'update', lineId: row.lineId, body }
}

/** Línea nueva que devolvió el POST: la de id más alto que aún no está en la rejilla. */
export function newLineFrom(lines: readonly LineDto[] | null | undefined, knownIds: ReadonlySet<number>): LineDto | null {
  let found: LineDto | null = null
  for (const l of lines ?? []) {
    if (l.id == null || knownIds.has(l.id)) continue
    if (!found || (found.id ?? 0) < l.id) found = l
  }
  return found
}

/**
 * Fila tras guardarse: si no se tecleó nada más mientras viajaba, queda como la devolvió el servidor (vuelve a decidir si
 * copia); si se siguió tecleando, se conserva lo tecleado y solo se actualiza lo guardado (quedará sin guardar).
 */
export function mergeSaved(current: LineRow, sent: LineRow, line: LineDto): LineRow {
  const fresh = rowFromLine(line, current.key)
  const untouched =
    current.expected === sent.expected && current.received === sent.received && current.productPublicId === sent.productPublicId
  if (untouched) return fresh
  return { ...current, lineId: fresh.lineId, asnLineId: fresh.asnLineId, original: fresh.original, saving: false, errors: {} }
}

/**
 * Pone al día la rejilla con el recibo del servidor (otra consulta, otra pantalla, la app): las filas sin cambios toman lo
 * guardado; las que tienen algo tecleado o se están guardando se conservan; las líneas que ya no existen se quitan; las
 * nuevas se agregan antes de las filas sin guardar (salvo mientras viaja un alta: `holdNew`, para no duplicarla).
 */
export function reconcileRows(
  rows: readonly LineRow[],
  lines: readonly LineDto[] | null | undefined,
  mode: RowsMode,
  newKey: () => string,
): LineRow[] {
  const byId = new Map<number, LineDto>()
  for (const l of lines ?? []) if (l.id != null) byId.set(l.id, l)
  const holdNew = rows.some((r) => r.lineId === null && r.saving)

  const saved: LineRow[] = []
  const pending: LineRow[] = []
  const seen = new Set<number>()
  for (const r of rows) {
    if (r.lineId === null) {
      pending.push(r)
      continue
    }
    const line = byId.get(r.lineId)
    if (!line) continue
    seen.add(r.lineId)
    if (r.saving || isDirty(r, mode.manual)) {
      saved.push({
        ...r,
        asnLineId: line.asnLineId ?? null,
        lotNumber: line.lotNumber ?? null,
        serialCount: line.serialNumbers?.length ?? 0,
        allocatedToCrossDock: line.allocatedToCrossDock ?? 0,
      })
    } else {
      saved.push({ ...rowFromLine(line, r.key), errors: r.errors })
    }
  }
  if (!holdNew) {
    const added = [...byId.values()].filter((l) => !seen.has(l.id ?? 0)).sort((a, b) => (a.id ?? 0) - (b.id ?? 0))
    for (const l of added) saved.push(rowFromLine(l))
  }
  const keep = mode.manual && mode.editable ? pending : pending.filter((r) => !isEmptyRow(r))
  const out = [...saved, ...(mode.editable ? keep : [])]
  return mode.manual && mode.editable ? ensureTrailingEmpty(out, newKey) : out
}

/** Errores del API de una fila (`line.receivedQty`, `expectedQty`, `productPublicId`, `line`…) por campo; sin campo, el título. */
export function rowErrorsFromProblem(problem: { title: string; errors: Record<string, string[]> }): RowErrors {
  const out: RowErrors = {}
  for (const [raw, msgs] of Object.entries(problem.errors)) {
    const msg = msgs[0]
    if (!msg) continue
    const k = raw.replace(/^\$\./, '').replace(/^line\./i, '').toLowerCase()
    const field: RowField =
      k === 'receivedqty' ? 'received' : k === 'expectedqty' ? 'expected' : k === 'productpublicid' ? 'product' : 'row'
    out[field] = out[field] ? `${out[field]} ${msg}` : msg
  }
  if (Object.keys(out).length === 0) out.row = problem.title
  return out
}

export type ConfirmBlocker = 'confirmed' | 'noLines' | 'saving' | 'unsaved' | 'errors'

/** Por qué no se puede confirmar todavía (null = se puede): confirmado, sin líneas guardadas, guardando, filas sin guardar
 *  o con error. */
export function confirmBlockers(rows: readonly LineRow[], isOpen: boolean, manual = true): ConfirmBlocker | null {
  if (!isOpen) return 'confirmed'
  if (rows.some((r) => r.saving)) return 'saving'
  if (rows.some((r) => isDirty(r, manual))) return 'unsaved'
  if (!rows.some((r) => r.lineId !== null)) return 'noLines'
  if (rows.some((r) => Object.keys(r.errors).length > 0)) return 'errors'
  return null
}
