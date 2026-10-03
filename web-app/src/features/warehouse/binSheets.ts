// Lote F15 — hojas de posición en Ubicaciones (servidor: Lote 23, manual 06 §1.5). Lógica pura de la pantalla y el flujo
// de impresión, sin React (se prueba con dependencias falsas):
// - Estado de la hoja de cada posición (`sheetStatus` del listado): Sin hoja impresa (NEVER_PRINTED), Desactualizada
//   (STALE), Al día (CURRENT) o "—" (EMPTY: sin productos, nada que imprimir). Es un aviso acumulado: la pantalla pinta la
//   insignia y el contador; nunca abre ventanas por cada movimiento (decisión del dueño).
// - Qué imprimir (`BinSheetsScope`): las posiciones del filtro actual de la tabla, las marcadas con sus casillas, o solo
//   las desactualizadas o sin hoja (los mismos filtros + `sheetStatus` STALE y NEVER_PRINTED).
// - Flujo (`printBinSheets`): lee las hojas del servidor por tandas de ≤ 200 (`GET .../bin-sheets`, cada tanda con su
//   `generatedAtUtc`), con tope de `BIN_SHEETS_MAX_BINS` posiciones (si se pasa, avisa y pide acotar sin generar nada);
//   ordena por código (orden natural), arma el PDF (`kernel/ui/binSheetPdf`) y SOLO si la descarga terminó bien marca
//   impresas (`POST .../mark-printed`) las posiciones que salieron en el PDF, con el `generatedAtUtc` MÁS ANTIGUO de las
//   tandas leídas (si la lista de productos de una posición cambió después de leerla, queda Desactualizada). Con el tope
//   de 500 es UNA sola llamada (todo o nada); el código igual parte en tandas de 500 por si el tope cambia. Cancelar
//   durante la lectura, o un error al leer o al generar, no marca nada.
import { parseApiDate } from '../../kernel/api/dates'
import { chunk, planBinSheets, type BinSheetBin, type BinSheetDetail, type BinSheetSpec } from '../../kernel/ui/binSheetPdf'
import type { BinSheetDto, BinSheetPageDto, BinSheetQuery, BinSheetStateDto } from './api'
import { naturalCompare } from './barcodeReports'
import type { BinListQuery } from './locations'

type Translate = (key: string, params?: Record<string, string | number>) => string

const S = 'warehouse.binSheets'

/** Estados de la hoja (códigos de `BinSheetStatuses` del dominio), en el orden del filtro. */
export const SHEET_STATUSES = ['NEVER_PRINTED', 'STALE', 'CURRENT', 'EMPTY'] as const
export type SheetStatus = (typeof SHEET_STATUSES)[number]
/** Los que piden imprimir (los que cuenta `staleCount`). */
export const NEEDS_PRINTING: readonly SheetStatus[] = ['STALE', 'NEVER_PRINTED']
/** Tono de la insignia: sin hoja en alerta, desactualizada en rojo, al día en verde (EMPTY va sin insignia: "—"). */
export const SHEET_STATUS_TONE: Record<Exclude<SheetStatus, 'EMPTY'>, 'warn' | 'fail' | 'disp'> = {
  NEVER_PRINTED: 'warn',
  STALE: 'fail',
  CURRENT: 'disp',
}

/** Permiso de las hojas de posición (leer, imprimir y marcar impresas: el de la pantalla y el del servidor). */
export const BIN_SHEETS_PERMISSION = 'inventory.view'

/** Hojas por lectura (el máximo del servidor), posiciones por "marcar impresas" (el del servidor) y tope por impresión. */
export const BIN_SHEETS_PAGE_SIZE = 200
export const BIN_SHEETS_MARK_MAX = 500
export const BIN_SHEETS_MAX_BINS = 500

/** Estado de la hoja de una posición; null si el servidor no lo manda (versión anterior) o es desconocido. */
export function sheetStatusOf(bin: { sheetStatus?: string | null }): SheetStatus | null {
  const s = (bin.sheetStatus ?? '').toUpperCase()
  return (SHEET_STATUSES as readonly string[]).includes(s) ? (s as SheetStatus) : null
}

/** Texto del estado de la hoja ('' sin estado): para ordenar, exportar y la tarjeta. */
export function sheetStatusText(bin: { sheetStatus?: string | null }, t: Translate): string {
  const s = sheetStatusOf(bin)
  return s ? t(`${S}.status.${s}`) : ''
}

/** true = la posición pide imprimir su hoja (Sin hoja impresa o Desactualizada). */
export function needsPrinting(bin: { sheetStatus?: string | null }): boolean {
  const s = sheetStatusOf(bin)
  return s !== null && NEEDS_PRINTING.includes(s)
}

/** Qué imprimir. */
export type BinSheetsScope = 'filter' | 'selected' | 'stale'

/** La consulta de la tabla sin el filtro "Hoja" (el contador de desactualizadas no depende de él). */
export function withoutSheetStatus<Q extends { sheetStatus?: string[] }>(query: Q): Q {
  const rest = { ...query }
  delete rest.sheetStatus
  return rest
}

/** Las desactualizadas o sin hoja con los demás filtros de la tabla. */
export function staleBinsQuery(query: BinListQuery): BinListQuery {
  return { ...withoutSheetStatus(query), sheetStatus: [...NEEDS_PRINTING] }
}

/**
 * Consultas de `GET .../bin-sheets` para un alcance (sin `skip`/`take`): el filtro de la tabla tal cual; las
 * desactualizadas (`staleBinsQuery`); o las marcadas, en tandas de 200 ids (`binIds`, sin repetir y en orden: la
 * dirección no crece de más). `query` null = filtros imposibles (nada que leer).
 */
export function binSheetsSources(scope: BinSheetsScope, query: BinListQuery | null, selectedIds: readonly number[]): BinSheetQuery[] {
  if (scope === 'selected') {
    const ids = [...new Set(selectedIds)].sort((a, b) => a - b)
    return chunk(ids, BIN_SHEETS_PAGE_SIZE).map((binIds) => ({ binIds }))
  }
  if (!query) return []
  return [scope === 'stale' ? staleBinsQuery(query) : { ...query }]
}

/** Detalle del encabezado: zona, pasillo, rack, nivel y posición (los que existen) y "Inactiva". */
export function sheetDetails(s: Pick<BinSheetDto, 'zoneCode' | 'aisle' | 'rack' | 'level' | 'position' | 'isActive'>, t: Translate): BinSheetDetail[] {
  const parts: [string, string | null | undefined][] = [
    ['zone', s.zoneCode],
    ['aisle', s.aisle],
    ['rack', s.rack],
    ['level', s.level],
    ['position', s.position],
  ]
  const out = parts.filter(([, v]) => v != null && v.trim() !== '').map(([k, v]) => ({ label: t(`${S}.details.${k}`), value: (v ?? '').trim() }))
  if (s.isActive === false) out.push({ label: t(`${S}.details.inactive`), value: '' })
  return out
}

/** Una hoja del servidor → la posición del PDF (productos en el orden del servidor: por SKU). */
export function toSheetBin(s: BinSheetDto, t: Translate, printedAt?: Date): BinSheetBin {
  return {
    code: s.code ?? '',
    key: s.binId,
    details: sheetDetails(s, t),
    printedAt,
    products: (s.products ?? []).map((p) => ({ sku: p.sku ?? '', name: p.name ?? '', barcode: p.barcode ?? null })),
  }
}

/** El instante más antiguo (texto del API tal cual); null si no hay ninguno válido. */
export function earliestUtc(values: readonly (string | null | undefined)[]): string | null {
  let best: { raw: string; ms: number } | null = null
  for (const raw of values) {
    if (!raw) continue
    const ms = parseApiDate(raw).getTime()
    if (Number.isNaN(ms)) continue
    if (!best || ms < best.ms) best = { raw, ms }
  }
  return best?.raw ?? null
}

export type BinSheetsProgress = { phase: 'read'; done: number; total: number } | { phase: 'render'; sheets: number } | { phase: 'mark'; bins: number }

export interface BinSheetsFlowDeps {
  /** Una tanda (`skip`/`take` ya puestos). */
  fetchPage: (query: BinSheetQuery) => Promise<BinSheetPageDto>
  /** Arma y descarga el PDF; si lanza, no se marca nada. */
  download: (spec: BinSheetSpec) => Promise<unknown>
  markPrinted: (binIds: number[], generatedAtUtc: string | null) => Promise<BinSheetStateDto[]>
  /** Cancelar (solo se atiende mientras se lee y antes de marcar). */
  signal?: AbortSignal
  onProgress?: (p: BinSheetsProgress) => void
  /** Hora de respaldo si el servidor no mandó `generatedAtUtc` (pruebas). */
  now?: () => Date
}

export interface BinSheetsFlowArgs {
  sources: readonly BinSheetQuery[]
  includeEmpty: boolean
  /** Título, compañía, almacén e idioma del PDF. */
  spec: Pick<BinSheetSpec, 'title' | 'company' | 'warehouse' | 'locale'>
  t: Translate
  lang: string
  /** Tope de posiciones (por defecto `BIN_SHEETS_MAX_BINS`). */
  max?: number
}

export type BinSheetsResult =
  | { status: 'tooMany'; total: number; max: number }
  | { status: 'nothing'; bins: number; omittedEmpty: number }
  | { status: 'cancelled' }
  | { status: 'printed'; sheets: number; bins: number; omittedEmpty: number; withoutCode: number; states: BinSheetStateDto[] }
  | { status: 'markFailed'; sheets: number; bins: number; error: unknown }

/** Lee, arma el PDF y marca impresas (ver el encabezado del archivo). Un error al leer o al generar se propaga (nada marcado). */
export async function printBinSheets(deps: BinSheetsFlowDeps, args: BinSheetsFlowArgs): Promise<BinSheetsResult> {
  const max = args.max ?? BIN_SHEETS_MAX_BINS
  const aborted = () => deps.signal?.aborted === true
  const read = new Map<number, { sheet: BinSheetDto; generatedAtUtc: string | null }>()
  let total = 0
  for (const source of args.sources) {
    let skip = 0
    let sourceTotal: number | null = null
    while (sourceTotal === null || skip < sourceTotal) {
      if (aborted()) return { status: 'cancelled' }
      let page: BinSheetPageDto
      try {
        page = await deps.fetchPage({ ...source, skip, take: BIN_SHEETS_PAGE_SIZE })
      } catch (err) {
        if (aborted()) return { status: 'cancelled' }
        throw err
      }
      if (sourceTotal === null) {
        sourceTotal = page.total ?? 0
        total += sourceTotal
        // más del tope: no se genera nada (ni se sigue leyendo); la pantalla pide acotar
        if (total > max) return { status: 'tooMany', total, max }
      }
      const items = page.items ?? []
      for (const sheet of items) {
        if (sheet.binId != null && !read.has(sheet.binId)) read.set(sheet.binId, { sheet, generatedAtUtc: page.generatedAtUtc ?? null })
      }
      deps.onProgress?.({ phase: 'read', done: read.size, total })
      if (items.length === 0) break
      skip += items.length
    }
  }
  if (aborted()) return { status: 'cancelled' }

  const cmp = naturalCompare(args.lang)
  const entries = [...read.values()].sort((a, b) => cmp(a.sheet.code ?? '', b.sheet.code ?? ''))
  const bins = entries.map((e) => toSheetBin(e.sheet, args.t, e.generatedAtUtc ? parseApiDate(e.generatedAtUtc) : undefined))
  const plan = planBinSheets(bins, { includeEmpty: args.includeEmpty })
  if (plan.pages.length === 0) return { status: 'nothing', bins: bins.length, omittedEmpty: plan.omittedEmpty.length }

  const printed = plan.printedBins.map((i) => entries[i])
  const generatedAtUtc = earliestUtc(printed.map((e) => e.generatedAtUtc))
  const printedAt = generatedAtUtc ? parseApiDate(generatedAtUtc) : (deps.now?.() ?? new Date())
  deps.onProgress?.({ phase: 'render', sheets: plan.pages.length })
  await deps.download({ ...args.spec, printedAt, bins, includeEmpty: args.includeEmpty })
  if (aborted()) return { status: 'cancelled' }

  const ids = printed.map((e) => e.sheet.binId as number)
  deps.onProgress?.({ phase: 'mark', bins: ids.length })
  const states: BinSheetStateDto[] = []
  try {
    for (const part of chunk(ids, BIN_SHEETS_MARK_MAX)) states.push(...(await deps.markPrinted(part, generatedAtUtc)))
  } catch (error) {
    return { status: 'markFailed', sheets: plan.pages.length, bins: ids.length, error }
  }
  return { status: 'printed', sheets: plan.pages.length, bins: ids.length, omittedEmpty: plan.omittedEmpty.length, withoutCode: plan.withoutCode, states }
}

/** Texto del aviso final de una impresión correcta (hojas, posiciones, vacías omitidas, productos sin código). */
export function printedSummary(r: Extract<BinSheetsResult, { status: 'printed' }>, t: Translate, fmt: (n: number) => string = String): string {
  const parts = [t(`${S}.result.printed`, { sheets: fmt(r.sheets), bins: fmt(r.bins) })]
  if (r.omittedEmpty > 0) parts.push(t(`${S}.result.omittedEmpty`, { count: fmt(r.omittedEmpty) }))
  if (r.withoutCode > 0) parts.push(t(`${S}.result.withoutCode`, { count: fmt(r.withoutCode) }))
  return parts.join(' ')
}
