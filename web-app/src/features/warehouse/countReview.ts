// Lote F12 — lógica pura de la revisión rápida del conteo cíclico por producto (diseño `docs/conteo-por-producto-diseno.md`,
// contrato del Lote 21 en `docs/lote21-decisiones.md`):
// - lista "Por revisar" (`GET /cycle-counts/review`): estado calculado de cada conteo (Cuadra, Con diferencia, Con errores,
//   Faltan líneas), texto del producto ("SKU · Nombre y N más"), consulta al API y qué conteos cierra "Cerrar los que cuadran";
// - resultado del cierre en bloque (`POST /cycle-counts/reconcile-matching`): motivo de cada omitido en español;
// - detalle para revisar: qué líneas "fallan" (ajuste ≠ 0 o con error contra la existencia ACTUAL de la vista previa), la
//   evidencia de cada línea (*Contó X (quién, cuándo) · Corregido a Y (quién, cuándo)*);
// - vista previa (`GET /cycle-counts/{id}/reconcile-preview`): qué impide confirmar y el color del ajuste.
// Nada de esto decide por el servidor: "cuadra", los errores y los ajustes los calcula el API con el mismo plan que reconcilia.
import type { TParams } from '../../kernel/i18n'
import type { ChipTone } from '../../kernel/ui/Chip'
import type {
  CycleCountLineDto,
  CycleCountReconcileMatchingResultDto,
  CycleCountReviewItemDto,
  CycleCountSkippedItemDto,
  GetQuery,
  ReconcilePreviewDto,
  ReconcilePreviewLineDto,
} from './api'

/** `t` de `useT()` (o el de fuera de React). */
export type Translate = (key: string, params?: TParams) => string

// ---------------------------------------------------------------------------------------------------------------------
// Lista "Por revisar"
// ---------------------------------------------------------------------------------------------------------------------

/** Pestañas de 'Conteo cíclico': la lista de siempre y "Por revisar" (`?tab=review`). */
export type CountTabKey = 'counts' | 'review'

/** `?tab=review` solo con warehouse.count (la lista "Por revisar" lo exige en el API); cualquier otro valor = la lista. */
export function countTabFromParam(raw: string | null, canReview: boolean): CountTabKey {
  return raw === 'review' && canReview ? 'review' : 'counts'
}

/** Estado calculado de un conteo por revisar (chip de la lista). */
export type ReviewState = 'matches' | 'difference' | 'errors' | 'pending'

/**
 * Estado del chip: con errores (bloquea confirmar) → faltan líneas (no se puede confirmar) → con diferencia (asentaría algo) →
 * cuadra (`matches` del servidor). Un conteo sin líneas, o que el servidor no da por cuadrado sin otra causa, queda en
 * "Faltan líneas" (nunca se pinta "Cuadra" si el servidor no lo dijo).
 */
export function reviewState(item: Pick<CycleCountReviewItemDto, 'matches' | 'errorLines' | 'pendingLines' | 'differingLines' | 'movements' | 'lines'>): ReviewState {
  if ((item.errorLines ?? 0) > 0) return 'errors'
  if ((item.pendingLines ?? 0) > 0) return 'pending'
  if ((item.differingLines ?? 0) > 0 || (item.movements ?? 0) > 0) return 'difference'
  if (item.matches === true) return 'matches'
  return 'pending'
}

/** Tono del chip de cada estado (semáforo del kit: verde cuadra, naranja diferencia, rojo errores, neutro faltan). */
export const REVIEW_STATE_TONE: Record<ReviewState, ChipTone> = {
  matches: 'deliv',
  difference: 'warn',
  errors: 'fail',
  pending: 'neutral',
}

/** Orden del estado para ordenar la columna (primero lo que bloquea). */
export const REVIEW_STATE_ORDER: Record<ReviewState, number> = { errors: 0, pending: 1, difference: 2, matches: 3 }

/** Producto de un conteo: el primero por SKU y cuántos más (`otherProducts`). */
export function reviewProduct(item: Pick<CycleCountReviewItemDto, 'firstProductSku' | 'firstProductName' | 'otherProducts'>): {
  label: string
  more: number
} {
  return {
    label: [item.firstProductSku, item.firstProductName].filter(Boolean).join(' · '),
    more: Math.max(0, item.otherProducts ?? 0),
  }
}

export interface ReviewFilters {
  /** Un almacén (el API filtra por uno) o '' = todos. */
  warehousePublicId: string
  /** Id de quien contó como texto ('' = cualquiera). */
  countedByUserId: string
  /** Buscador libre (número, SKU o nombre; con pausa). */
  search: string
}

export const EMPTY_REVIEW_FILTERS: ReviewFilters = { warehousePublicId: '', countedByUserId: '', search: '' }

/** Consulta de `GET /cycle-counts/review` sin página (la usa también Exportar). Solo Contados (decisión del Lote 21). */
export function reviewFilterQuery(f: ReviewFilters): GetQuery<'/api/v1/cycle-counts/review'> {
  const by = Number(f.countedByUserId)
  return {
    warehousePublicId: f.warehousePublicId || undefined,
    countedByUserId: f.countedByUserId && Number.isInteger(by) && by > 0 ? by : undefined,
    search: f.search.trim() || undefined,
  }
}

export function reviewListQuery(f: ReviewFilters, page: number, pageSize: number): GetQuery<'/api/v1/cycle-counts/review'> {
  return { ...reviewFilterQuery(f), skip: (Math.max(1, page) - 1) * pageSize, take: pageSize }
}

export interface CounterOption {
  value: string
  label: string
}

/**
 * Opciones del filtro "Contó": quienes aparecen en las páginas ya vistas (el API no tiene un catálogo de quién contó) más los
 * usuarios de la compañía si el usuario puede leerlos (`admin.users`). Sin repetir, por nombre.
 */
export function counterOptions(
  seen: ReadonlyMap<number, string>,
  users: readonly { id?: number; fullName?: string | null; email?: string | null; isActive?: boolean }[] = [],
): CounterOption[] {
  const map = new Map<number, string>(seen)
  for (const u of users) {
    if (u.id == null || u.isActive === false || map.has(u.id)) continue
    map.set(u.id, u.fullName || u.email || String(u.id))
  }
  return [...map.entries()].map(([id, label]) => ({ value: String(id), label })).sort((a, b) => a.label.localeCompare(b.label, 'es'))
}

/** Agrega a `seen` los que contaron en estas filas (devuelve el mismo mapa si no hay nadie nuevo). */
export function rememberCounters(seen: ReadonlyMap<number, string>, items: readonly CycleCountReviewItemDto[]): ReadonlyMap<number, string> {
  let next: Map<number, string> | null = null
  for (const it of items) {
    const id = it.countedByUserId
    if (id == null || !it.countedByName || seen.get(id) === it.countedByName) continue
    next ??= new Map(seen)
    next.set(id, it.countedByName)
  }
  return next ?? seen
}

/**
 * Conteos que cierra "Cerrar los que cuadran": los elegidos que cuadran o, sin elegir ninguno, todos los que cuadran de la
 * página que se ve. Se mandan por `ids` (así el cierre respeta exactamente los filtros y la página, y el aviso dice cuántos).
 */
export function idsToClose(items: readonly CycleCountReviewItemDto[], selected: ReadonlySet<number>): { id: number; number: string }[] {
  const matching = items.filter((it) => reviewState(it) === 'matches' && it.count?.id != null)
  const chosen = selected.size > 0 ? matching.filter((it) => selected.has(it.count?.id ?? 0)) : matching
  return chosen.map((it) => ({ id: it.count?.id ?? 0, number: it.count?.number ?? '' }))
}

/** Elegidos que siguen siendo elegibles tras recargar la página (los que ya no cuadran o no están se quitan). */
export function keepSelection(selected: ReadonlySet<number>, items: readonly CycleCountReviewItemDto[]): ReadonlySet<number> {
  const ok = new Set(items.filter((it) => reviewState(it) === 'matches').map((it) => it.count?.id ?? 0))
  const next = new Set([...selected].filter((id) => ok.has(id)))
  return next.size === selected.size ? selected : next
}

// ---------------------------------------------------------------------------------------------------------------------
// Resultado del cierre en bloque
// ---------------------------------------------------------------------------------------------------------------------

/** Motivos que el API puede devolver (`reasonCode`); cualquier otro se muestra con el texto del servidor. */
export const SKIP_REASONS = ['WouldPost', 'Errors', 'Pending', 'Stale', 'NotCounted', 'AlreadyReconciled', 'NotFound', 'NoLines', 'Failed'] as const
export type SkipReason = (typeof SKIP_REASONS)[number]

export function isSkipReason(code: string | null | undefined): code is SkipReason {
  return (SKIP_REASONS as readonly string[]).includes(code ?? '')
}

/**
 * Texto de un omitido: la clave `warehouse.cycleCounts.review.skip.<código>` con `{n}` = `count` y, en Errors y Failed (cuyo
 * motivo es un mensaje concreto del servidor, p. ej. lo reservado), el detalle tal cual. Código desconocido → el `reason`.
 */
export function skipReasonText(s: Pick<CycleCountSkippedItemDto, 'reasonCode' | 'reason' | 'count'>, t: Translate): string {
  if (!isSkipReason(s.reasonCode)) return s.reason ?? s.reasonCode ?? ''
  const base = t(`warehouse.cycleCounts.review.skip.${s.reasonCode}`, { n: s.count ?? 0 })
  if ((s.reasonCode === 'Errors' || s.reasonCode === 'Failed') && s.reason) return `${base} ${s.reason}`
  return base
}

export interface BulkSummary {
  closed: { id: number; number: string; lines: number }[]
  skipped: { id: number; number: string; code: string; text: string }[]
  examined: number
  truncated: boolean
}

/** Resumen legible del cierre en bloque (cerrados por número; omitidos con su motivo en español). */
export function bulkSummary(
  result: CycleCountReconcileMatchingResultDto,
  t: Translate,
): BulkSummary {
  return {
    closed: (result.closed ?? []).map((c) => ({ id: c.id ?? 0, number: c.number ?? `#${c.id ?? 0}`, lines: c.lines ?? 0 })),
    skipped: (result.skipped ?? []).map((s) => ({ id: s.id ?? 0, number: s.number ?? `#${s.id ?? 0}`, code: s.reasonCode ?? '', text: skipReasonText(s, t) })),
    examined: result.examined ?? 0,
    truncated: result.truncated === true,
  }
}

/** Largo máximo del comentario del cierre (= historial de estatus, NVARCHAR(500); el API responde 400 si se pasa). */
export const BULK_COMMENT_MAX = 500

// ---------------------------------------------------------------------------------------------------------------------
// Detalle para revisar: líneas que fallan y evidencia
// ---------------------------------------------------------------------------------------------------------------------

/** Una línea de la vista previa "falla": asentaría algo (ajuste ≠ 0) o tiene error. Una pendiente no falla (es un dato). */
export function previewLineFails(l: Pick<ReconcilePreviewLineDto, 'adjustmentQty' | 'error' | 'movements'>): boolean {
  return Boolean(l.error) || (l.adjustmentQty ?? 0) !== 0 || (l.movements ?? 0) > 0
}

/** Vista previa indexada por línea. */
export function previewByLine(preview: ReconcilePreviewDto | null | undefined): ReadonlyMap<number, ReconcilePreviewLineDto> {
  return new Map((preview?.lines ?? []).map((l) => [l.lineId ?? 0, l]))
}

/**
 * Líneas que se ven en "solo las que fallan": con la vista previa, las que fallan contra la existencia ACTUAL; sin ella (aún no
 * llega), las que difieren de la foto (`lineVariance` ≠ 0). Las fijadas (`pinned`: corregidas en esta sesión) se quedan aunque
 * dejen de fallar, para no perder de vista la fila que se acaba de editar.
 */
export function failingLines<L extends Pick<CycleCountLineDto, 'id'>>(
  lines: readonly L[],
  preview: ReadonlyMap<number, ReconcilePreviewLineDto> | null,
  variance: (line: L) => number | null,
  pinned: ReadonlySet<number> = new Set(),
): L[] {
  return lines.filter((l) => {
    const id = l.id ?? 0
    if (pinned.has(id)) return true
    const p = preview?.get(id)
    if (p) return previewLineFails(p)
    const d = variance(l)
    return d !== null && d !== 0
  })
}

export interface EvidenceStep {
  qty: number | null
  by: string | null
  at: string | null
}

export interface LineEvidence {
  /** Lo que se capturó primero (Contó X). */
  counted: EvidenceStep | null
  /** La corrección vigente (Corregido a Y), si la hay. */
  corrected: EvidenceStep | null
}

/**
 * Evidencia de una línea: *Contó X (quién, cuándo)* y, si se corrigió, *Corregido a Y (quién, cuándo)*, con Y = lo vigente
 * (`countedQty`). Sin captura → null en ambos. Las series no traen `capturedQty` (se cuentan por conjunto): X = lo contado.
 */
export function lineEvidence(
  l: Pick<CycleCountLineDto, 'countedQty' | 'capturedQty' | 'capturedByName' | 'capturedAtUtc' | 'correctedByName' | 'correctedAtUtc' | 'wasCorrected'>,
): LineEvidence {
  const hasCapture = l.capturedAtUtc != null || l.capturedByName != null || l.capturedQty != null
  const corrected = l.wasCorrected === true || l.correctedAtUtc != null
  return {
    counted: hasCapture ? { qty: l.capturedQty ?? (corrected ? null : (l.countedQty ?? null)), by: l.capturedByName ?? null, at: l.capturedAtUtc ?? null } : null,
    corrected: corrected ? { qty: l.countedQty ?? null, by: l.correctedByName ?? null, at: l.correctedAtUtc ?? null } : null,
  }
}

/** Texto plano de la evidencia (exportación y lectores): "Contó 3 (Ana, 10/02/2026 9:30 AM) · Corregido a 5 (Beto, …)". */
export function evidenceText(
  ev: LineEvidence,
  t: Translate,
  num: (n: number | null | undefined) => string,
  when: (iso: string | null | undefined) => string,
): string {
  const who = (s: EvidenceStep) => [s.by, s.at ? when(s.at) : null].filter(Boolean).join(', ')
  const parts: string[] = []
  if (ev.counted) {
    const w = who(ev.counted)
    parts.push(t(w ? 'warehouse.cycleCounts.evidence.countedBy' : 'warehouse.cycleCounts.evidence.counted', { qty: ev.counted.qty == null ? '—' : num(ev.counted.qty), who: w }))
  }
  if (ev.corrected) {
    const w = who(ev.corrected)
    parts.push(t(w ? 'warehouse.cycleCounts.evidence.correctedBy' : 'warehouse.cycleCounts.evidence.corrected', { qty: ev.corrected.qty == null ? '—' : num(ev.corrected.qty), who: w }))
  }
  return parts.join(' · ')
}

// ---------------------------------------------------------------------------------------------------------------------
// Vista previa: qué impide confirmar y color del ajuste
// ---------------------------------------------------------------------------------------------------------------------

export type PreviewBlocker =
  | { key: 'blocking'; message: string }
  | { key: 'errors'; params: { n: number } }
  | { key: 'pending'; params: { n: number } }
  | { key: 'noLines' }

/**
 * Lo que impide confirmar según la vista previa (en el orden en que respondería el servidor): error del conteo entero
 * (`blockingError`, el 400 de reconciliar), líneas con error (el 409 de reservado), líneas sin contar (el 422) o sin líneas.
 * null = se puede confirmar (`resultStatusCode` dice en qué terminaría).
 */
export function previewBlocker(preview: Pick<ReconcilePreviewDto, 'blockingError' | 'totals'> | null | undefined): PreviewBlocker | null {
  if (!preview) return null
  if (preview.blockingError) return { key: 'blocking', message: preview.blockingError }
  const t = preview.totals ?? {}
  if ((t.errorLines ?? 0) > 0) return { key: 'errors', params: { n: t.errorLines ?? 0 } }
  if ((t.pendingLines ?? 0) > 0) return { key: 'pending', params: { n: t.pendingLines ?? 0 } }
  if ((t.lines ?? 0) === 0) return { key: 'noLines' }
  return null
}

/** Clase del ajuste: entra (+, color de flujo), sale (−, peligro) o nada. */
export function adjustmentClass(n: number | null | undefined): 'qty-in' | 'qty-out' | 'qty-zero' {
  if (n == null || n === 0) return 'qty-zero'
  return n > 0 ? 'qty-in' : 'qty-out'
}

/** "+2" / "-1" / "0" con el formateador de números de la compañía. */
export function signedQty(n: number | null | undefined, num: (n: number) => string): string {
  if (n == null) return '—'
  if (n === 0) return num(0) || '0'
  return n > 0 ? `+${num(n)}` : `-${num(-n)}`
}
