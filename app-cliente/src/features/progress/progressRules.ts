// Barra de progreso de una orden (Lote 103): tantas etapas como estatus tiene el camino normal de la compañía. Reglas puras: qué pinta cada tramo según la etapa actual y el estado.

export type SegmentKind = 'done' | 'current' | 'alert' | 'todo' | 'cancelled'

/** Estados que manda el servidor: en marcha, completada (entregada), detenida (en espera, parcial, fallida…) o cancelada. */
export type ProgressState = 'IN_PROGRESS' | 'COMPLETED' | 'ALERT' | 'CANCELLED' | string

/** Qué es cada uno de los `total` tramos. En marcha: lo anterior hecho y el actual resaltado; completada: todo hecho; detenida: el actual en alerta; cancelada: todo apagado. */
export function segmentKinds(step: number, total: number, state: ProgressState): SegmentKind[] {
  const n = Math.max(0, Math.floor(total))
  return Array.from({ length: n }, (_, i) => {
    const pos = i + 1
    if (state === 'CANCELLED') return 'cancelled'
    if (state === 'COMPLETED') return 'done'
    if (pos < step) return 'done'
    if (pos === step) return state === 'ALERT' ? 'alert' : 'current'
    return 'todo'
  })
}

/** Porcentaje avanzado (para leerlo en voz alta o mostrarlo): etapa actual sobre el total; 0 si está cancelada. */
export function progressPercent(step: number, total: number, state: ProgressState): number {
  if (state === 'CANCELLED' || total <= 0) return 0
  if (state === 'COMPLETED') return 100
  return Math.round((Math.min(Math.max(step, 0), total) / total) * 100)
}

/** Cada etapa de la lista vertical: hecha, actual o por venir (la actual en alerta si está detenida). */
export function stepKind(index: number, step: number, state: ProgressState): SegmentKind {
  return segmentKinds(step, index + 1, state)[index]
}
