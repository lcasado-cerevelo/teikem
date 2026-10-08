// Tarea 25: todas las lecturas del conteo que hace la app mandan forCounting=true, para que la compañía en «Nadie» (ver lo esperado) también deje a ciegas al supervisor.
// Lote 8A-app — Conteo empieza en línea: escanear la posición reclama el conteo en el servidor (POST /cycle-counts,
// docs/lote8A-app-decisiones.md, decisión heredada de que las posiciones son un recurso compartido); de ahí en
// adelante capturar lo encontrado y terminar van por la cola de salida (kernel/sync/outbox.ts), referenciando el id
// que ya se conoce, sin necesidad de reescribir nada.
// Lote A4 — "Contar por producto" (docs/lote21-decisiones.md, "Receta para la app"): abrir el conteo por producto y crear
// una posición provisional ("Otra posición") necesitan señal; capturar y terminar van por la cola de salida igual que antes.
import { api, ApiError, unwrap } from '../../kernel/api/client'
import { getDb } from '../../kernel/db/database'
import { enqueue } from '../../kernel/sync/outbox'
import { buildBatchItems, type BinOption, type CapturedEntry, type ExpectedLine, type ProductCountLine } from './countLogic'

export interface StartedCount {
  countId: number
  number: string
  isBlind: boolean
  expectedLines: ExpectedLine[]
  /** 2026-10-07: true si ya existía un conteo abierto de esa posición y se retomó (en vez de abrir otro). */
  resumed: boolean
  /** Lo ya contado en un conteo retomado (líneas con cantidad). */
  captured: Array<{ lineId: number; countedQty: number }>
  /** Estado de verificación (MATCH/RECOUNT/FINAL) de las líneas de un conteo retomado. */
  checks: Array<{ lineId: number; state: 'MATCH' | 'RECOUNT' | 'FINAL' }>
  /** Posición del conteo (la de sus líneas); null si no trae líneas. */
  binId: number | null
  binCode: string | null
}

type LineDto = {
  id?: number
  productPublicId?: string
  sku?: string | null
  productName?: string | null
  systemQty?: number | null
  binId?: number
  binCode?: string | null
  lotId?: number | null
  lotNumber?: string | null
  binIsProvisional?: boolean
  countedQty?: number | null
  checkState?: string | null
}

function mapExpectedLines(lines: LineDto[] | null | undefined): ExpectedLine[] {
  return (lines ?? []).map((l) => ({
    lineId: l.id ?? 0,
    productPublicId: l.productPublicId ?? '',
    sku: l.sku ?? '',
    productName: l.productName ?? '',
    systemQty: l.systemQty ?? null,
  }))
}

function mapProductLines(lines: LineDto[] | null | undefined): ProductCountLine[] {
  return (lines ?? []).map((l) => ({
    lineId: l.id ?? 0,
    productPublicId: l.productPublicId ?? '',
    sku: l.sku ?? '',
    productName: l.productName ?? '',
    systemQty: l.systemQty ?? null,
    binId: l.binId ?? 0,
    binCode: l.binCode ?? '',
    lotId: l.lotId ?? null,
    lotNumber: l.lotNumber ?? null,
    binIsProvisional: l.binIsProvisional ?? false,
  }))
}

type DetailDto = {
  count?: { id?: number; number?: string | null }
  isBlind?: boolean
  resumed?: boolean
  lines?: LineDto[] | null
}

function toStarted(detail: DetailDto): StartedCount {
  const lines = detail.lines ?? []
  return {
    countId: detail.count?.id ?? 0,
    number: detail.count?.number ?? '',
    isBlind: detail.isBlind ?? true,
    expectedLines: mapExpectedLines(lines),
    resumed: detail.resumed ?? false,
    captured: lines.filter((l) => l.countedQty != null).map((l) => ({ lineId: l.id ?? 0, countedQty: l.countedQty as number })),
    checks: lines
      .filter((l) => l.checkState === 'MATCH' || l.checkState === 'RECOUNT' || l.checkState === 'FINAL')
      .map((l) => ({ lineId: l.id ?? 0, state: l.checkState as 'MATCH' | 'RECOUNT' | 'FINAL' })),
    binId: lines[0]?.binId ?? null,
    binCode: lines[0]?.binCode ?? null,
  }
}

/** Abre el conteo de esa posición; si ya hay uno abierto ahí lo RETOMA (resumeOpen) con lo que ya se contó. Si lo tiene otro contador, el servidor
 *  responde 409 «Esa posición la está contando X (CC-…).». */
export async function startCountOnline(warehousePublicId: string, binId: number): Promise<StartedCount> {
  const detail = await unwrap(
    api.POST('/api/v1/cycle-counts', { params: { query: { forCounting: true } }, body: { warehousePublicId, binIds: [binId], assignToMe: true, resumeOpen: true } }),
  )
  return toStarted(detail)
}

/** Retoma un conteo abierto por su id (lista «Conteos abiertos»): sirve también para los duplicados de la misma posición. */
export async function loadOpenCount(countId: number): Promise<StartedCount> {
  const detail = await unwrap(api.GET('/api/v1/cycle-counts/{id}', { params: { path: { id: countId }, query: { forCounting: true } } }))
  return { ...toStarted(detail), resumed: true }
}

export interface StartedProductCount {
  countId: number
  isBlind: boolean
  lines: ProductCountLine[]
}

/** Crea el conteo POR PRODUCTO (productPublicIds, sin posiciones): una línea por cada posición y lote con existencia del
 *  producto en el almacén. Con `allowEmpty: true` (siempre, desde esta app) un producto sin existencia en ningún lado abre el
 *  conteo VACÍO (sin líneas) en vez de un 400: lo hallado donde el sistema no tenía nada se agrega con «Otra posición». */
export async function startProductCountOnline(warehousePublicId: string, productPublicId: string): Promise<StartedProductCount> {
  const detail = await unwrap(api.POST('/api/v1/cycle-counts', { params: { query: { forCounting: true } }, body: { warehousePublicId, productPublicIds: [productPublicId], allowEmpty: true, assignToMe: true } }))
  return { countId: detail.count?.id ?? 0, isBlind: detail.isBlind ?? true, lines: mapProductLines(detail.lines) }
}

/** Lote 24 — abre un conteo VACÍO (origen PRODUCT, sin producto ni posición) al que se van agregando los productos escaneados:
 *  POST /cycle-counts con allowEmpty y sin filtros. Necesita señal. */
export async function startOpenCountOnline(warehousePublicId: string): Promise<{ countId: number; isBlind: boolean }> {
  const detail = await unwrap(api.POST('/api/v1/cycle-counts', { params: { query: { forCounting: true } }, body: { warehousePublicId, allowEmpty: true, assignToMe: true } }))
  return { countId: detail.count?.id ?? 0, isBlind: detail.isBlind ?? true }
}

/** Lote 24 — dónde dice el sistema que está el producto (posición y lote con existencia, SIN cantidades): GET
 *  /cycle-counts/{id}/product-bins. Una opción = la posición por defecto; varias = se elige; ninguna = se pide escanear. */
export async function fetchProductBins(countId: number, productPublicId: string): Promise<BinOption[]> {
  const dto = await unwrap(api.GET('/api/v1/cycle-counts/{id}/product-bins', { params: { path: { id: countId }, query: { productPublicId } } }))
  return (dto.bins ?? []).map((b) => ({ binId: b.binId ?? 0, binCode: b.binCode ?? '', zoneCode: b.zoneCode ?? '', lotId: b.lotId ?? null, lotNumber: b.lotNumber ?? null }))
}

/** Recupera las líneas esperadas de un conteo ya abierto (se cerró y reabrió la app: local_count guarda el id pero
 *  no la lista, así que se vuelve a pedir; de todas formas esta pantalla ya necesita señal). Solo conteo por posición: el
 *  de producto guarda sus líneas localmente al abrirse. */
export async function fetchExpectedLines(countId: number): Promise<ExpectedLine[]> {
  const detail = await unwrap(api.GET('/api/v1/cycle-counts/{id}', { params: { path: { id: countId }, query: { forCounting: true } } }))
  return mapExpectedLines(detail.lines)
}

/** Cancela el conteo en el servidor (libera la posición para otro). Necesita señal, igual que abrirlo. */
export async function cancelCountOnline(countId: number): Promise<void> {
  await unwrap(api.DELETE('/api/v1/cycle-counts/{id}', { params: { path: { id: countId } } }))
}

/** Encola el lote capturado y el cierre del conteo (kernel/sync/outbox.ts): dos filas en orden, el lote primero. Cada
 *  línea lleva su propia posición (conteo por producto: varias posiciones en el mismo lote). */
export function enqueueFinishCount(countId: number, entries: CapturedEntry[]): void {
  enqueueSaveCount(countId, entries)
  enqueue({ kind: 'countFinish', path: `/api/v1/cycle-counts/${countId}/finish`, body: {} })
}

/** «Guardar y seguir después»: manda lo contado SIN cerrar el conteo (queda abierto en el servidor para retomarlo). */
export function enqueueSaveCount(countId: number, entries: CapturedEntry[]): void {
  enqueue({
    kind: 'countBatch',
    path: `/api/v1/cycle-counts/${countId}/lines/batch`,
    body: { lines: buildBatchItems(entries) },
  })
}

// ------------------------------------------------------------------ "Otra posición"

export interface CountZone {
  id: number
  code: string
  name: string
}

/** Zonas del almacén de las que se llena la posición sincronizada (tabla `bin`, GET /sync/bins): respaldo sin señal o si el
 *  usuario no tiene `inventory.view` (lo que pide GET /warehouses/{id}/zones). Solo trae zonas que ya tienen posiciones. */
export function localZones(warehousePublicId: string): CountZone[] {
  return getDb()
    .getAllSync<{ zone_id: number; zone_code: string | null; zone_name: string | null }>(
      `SELECT zone_id, MAX(zone_code) AS zone_code, MAX(zone_name) AS zone_name FROM bin
       WHERE warehouse_public_id = ? AND zone_id IS NOT NULL AND is_active = 1
       GROUP BY zone_id ORDER BY MAX(zone_code)`,
      [warehousePublicId],
    )
    .map((z) => ({ id: z.zone_id, code: z.zone_code ?? String(z.zone_id), name: z.zone_name ?? '' }))
}

export interface ZonesResult {
  zones: CountZone[]
  /** true si vinieron de la copia sincronizada (no se pudo preguntar al servidor). */
  fromLocal: boolean
}

/** Zonas activas del almacén: GET /warehouses/{publicId}/zones; si falla (sin señal, 403…), las de las posiciones sincronizadas. */
export async function fetchZones(warehousePublicId: string): Promise<ZonesResult> {
  try {
    const zones = await unwrap(api.GET('/api/v1/warehouses/{publicId}/zones', { params: { path: { publicId: warehousePublicId } } }))
    const active = zones
      .filter((z) => z.isActive !== false && z.id != null)
      .map((z) => ({ id: z.id ?? 0, code: z.code ?? '', name: z.name ?? '' }))
    return { zones: active, fromLocal: false }
  } catch (err) {
    if (!(err instanceof ApiError)) throw err
    return { zones: localZones(warehousePublicId), fromLocal: true }
  }
}

export interface CreatedBin {
  id: number
  code: string
  isProvisional: boolean
}

export interface ProvisionalBinInput {
  zoneId: number
  code?: string
  aisle?: string
  rack?: string
  level?: string
  position?: string
}

/** POST /cycle-counts/{id}/bins: crea la posición provisional ("pendiente de revisión"). Necesita señal. Errores del servidor
 *  (400/404/409/422) llegan como ApiError con su mensaje exacto. */
export async function createProvisionalBin(countId: number, input: ProvisionalBinInput): Promise<CreatedBin> {
  const body: ProvisionalBinInput = { zoneId: input.zoneId }
  for (const key of ['code', 'aisle', 'rack', 'level', 'position'] as const) {
    const v = input[key]?.trim()
    if (v) body[key] = v
  }
  const bin = await unwrap(api.POST('/api/v1/cycle-counts/{id}/bins', { params: { path: { id: countId } }, body }))
  return { id: bin.id ?? 0, code: bin.code ?? '', isProvisional: bin.isProvisional ?? true }
}

/** Posición ya existente con ese código en la copia sincronizada del almacén (activa), con su marca de "pendiente de
 *  revisión" (provisional) tal como la trajo la sincronización. */
export function findLocalBin(warehousePublicId: string, code: string): CreatedBin | null {
  const row = getDb().getFirstSync<{ id: number; code: string; is_provisional: number }>(
    'SELECT id, code, is_provisional FROM bin WHERE warehouse_public_id = ? AND code = ? COLLATE NOCASE AND is_active = 1 LIMIT 1',
    [warehousePublicId, code.trim()],
  )
  return row ? { id: row.id, code: row.code, isProvisional: row.is_provisional === 1 } : null
}

export { ApiError }
