// Segundo bloque de decisiones del dueño (2026-10-03) — lote parcial. Con el cambio del servidor, un lote de captura (`countBatch`)
// con una línea que el supervisor ya corrigió YA NO se rechaza: responde 200, guarda las líneas libres y trae `skippedLines` (las
// omitidas: SKU, posición, lote, lo que se mandó y lo que dejó el supervisor; nunca la cantidad esperada). La fila de la cola queda
// `sent` como siempre; lo omitido se guarda aparte como AVISO persistente en el aparato (kv de la compañía) para que Sincronización lo
// muestre hasta que el operario lo descarte, aunque cierre la app. Funciones sin red; el almacenamiento usa el kv local.
import { getKv, KvKeys, setKv } from '../../kernel/db/kv'
import { countIdFromBatchPath } from './countRejection'

export interface SkippedLine {
  /** Id de la línea del conteo en el servidor. */
  lineId: number | null
  sku: string
  binCode: string
  lotNumber: string | null
  /** Lo que mandó este aparato (null si no se puede leer). */
  sentQty: number | null
  /** Valor vigente que dejó el supervisor (null si no vino). Nunca la cantidad esperada. */
  currentQty: number | null
}

export interface SkippedNotice {
  /** Id local del aviso (único). */
  id: string
  /** Fila de la cola que lo originó. */
  outboxId: number
  /** Id del conteo en el servidor (de la ruta enviada); null si no se reconoce. */
  countId: number | null
  createdAtUtc: string
  lines: SkippedLine[]
}

function num(v: unknown): number | null {
  return typeof v === 'number' && Number.isFinite(v) ? v : null
}

/** Lee `skippedLines` de la respuesta (200) de un lote. Cualquier forma inesperada → lista vacía (no hay aviso). */
export function parseSkippedLines(result: unknown): SkippedLine[] {
  if (typeof result !== 'object' || result === null) return []
  const raw = (result as { skippedLines?: unknown }).skippedLines
  if (!Array.isArray(raw)) return []
  const out: SkippedLine[] = []
  for (const item of raw) {
    if (typeof item !== 'object' || item === null) continue
    const o = item as Record<string, unknown>
    const sku = typeof o.sku === 'string' ? o.sku.trim() : ''
    if (sku === '') continue
    out.push({
      lineId: num(o.lineId),
      sku,
      binCode: typeof o.binCode === 'string' ? o.binCode : '',
      lotNumber: typeof o.lotNumber === 'string' && o.lotNumber !== '' ? o.lotNumber : null,
      sentQty: num(o.sentQty),
      currentQty: num(o.currentQty),
    })
  }
  return out
}

function readAll(): SkippedNotice[] {
  try {
    const raw = getKv(KvKeys.countSkippedNotices)
    if (!raw) return []
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? (parsed as SkippedNotice[]).filter((n) => n && typeof n.id === 'string' && Array.isArray(n.lines)) : []
  } catch {
    return []
  }
}

function writeAll(notices: SkippedNotice[]): void {
  setKv(KvKeys.countSkippedNotices, notices.length === 0 ? null : JSON.stringify(notices))
}

/** Avisos pendientes de descartar, del más viejo al más nuevo. */
export function listSkippedNotices(): SkippedNotice[] {
  return readAll()
}

/**
 * Guarda el aviso de un lote parcial: llamar tras un `countBatch` exitoso. Sin `skippedLines` no hace nada. Idempotente por fila de
 * la cola (si se llamara dos veces para la misma fila no duplica). Devuelve el aviso creado o null.
 */
export function recordSkippedFromResult(outboxId: number, path: string, result: unknown, now: Date = new Date()): SkippedNotice | null {
  const lines = parseSkippedLines(result)
  if (lines.length === 0) return null
  const all = readAll()
  const existing = all.find((n) => n.outboxId === outboxId)
  if (existing) return existing
  const notice: SkippedNotice = {
    id: `skip-${outboxId}-${now.getTime().toString(36)}`,
    outboxId,
    countId: countIdFromBatchPath(path),
    createdAtUtc: now.toISOString(),
    lines,
  }
  writeAll([...all, notice])
  return notice
}

/** Descarta un aviso (el operario ya lo vio). */
export function dismissSkippedNotice(id: string): void {
  writeAll(readAll().filter((n) => n.id !== id))
}

/** `• SKU · posición (mandaste x → el supervisor dejó y)`; con lote, `• SKU · lote L · posición …`. */
export function skippedLineText(l: SkippedLine, t: (key: string, params?: Record<string, string | number>) => string): string {
  const base = { sku: l.sku, bin: l.binCode, sent: l.sentQty ?? '—' }
  if (l.currentQty === null) {
    return l.lotNumber ? t('countSkipped.lineLotNoCurrent', { ...base, lot: l.lotNumber }) : t('countSkipped.lineNoCurrent', base)
  }
  return l.lotNumber
    ? t('countSkipped.lineLot', { ...base, lot: l.lotNumber, current: l.currentQty })
    : t('countSkipped.line', { ...base, current: l.currentQty })
}
