// 2026-10-11: nombre legible de cada tipo de operación de la cola de salida (kernel/sync/outbox.ts) para la pantalla de
// Sincronización, que antes mostraba el `kind` técnico tal cual («manualIssue», «countBatch»…). El Record obliga a dar
// una etiqueta a todo tipo nuevo de OutboxKind (tsc falla si falta); un `kind` desconocido (una fila guardada por otra
// versión de la app) se muestra tal cual, para que no quede en blanco.
import type { OutboxKind } from './outbox'

const KIND_KEYS: Record<OutboxKind, string> = {
  receipt: 'sync.kindReceipt',
  pack: 'sync.kindPack',
  collect: 'sync.kindCollect',
  countBatch: 'sync.kindCountBatch',
  countFinish: 'sync.kindCountFinish',
  transfer: 'sync.kindTransfer',
  adjust: 'sync.kindAdjust',
  damage: 'sync.kindDamage',
  manualIssue: 'sync.kindManualIssue',
}

/** Clave i18n del tipo, o null si el tipo no es uno de los conocidos. */
export function outboxKindKey(kind: string): string | null {
  return Object.prototype.hasOwnProperty.call(KIND_KEYS, kind) ? KIND_KEYS[kind as OutboxKind] : null
}

/** Etiqueta traducida del tipo de operación; el `kind` tal cual si no es uno conocido. */
export function outboxKindLabel(kind: string, t: (key: string) => string): string {
  const key = outboxKindKey(kind)
  return key ? t(key) : kind
}
