// 2026-10-07 — listado de posiciones marcables (Despacho y Recibo directo): quien captura marca de qué posiciones sale (o a cuáles entra) la
// cantidad y cada marca toma lo que falta, sin pasarse nunca del total ni de lo que cabe/hay en la posición. Reglas puras, sin pantalla.

/** Una posición del listado. `capacity` = lo máximo que se puede tomar de ella (existencia disponible al sacar, espacio libre al recibir);
 *  null = sin tope conocido. `recommended` = la que el sistema usaría para completar la cantidad (se pinta distinto y va primero). */
export interface MarkOption {
  key: string
  binCode: string
  capacity: number | null
  recommended: boolean
  /** Texto de apoyo: lote, vencimiento, disponible… */
  detail: string
}

/** Cantidad tomada por posición (clave de `MarkOption`). */
export type Marks = Record<string, number>

const round = (n: number) => Math.round(n * 1000) / 1000

export function sumMarks(marks: Marks): number {
  return round(Object.values(marks).reduce((s, q) => s + q, 0))
}

/** Lo que falta por tomar para llegar al total (nunca negativo). */
export function remainingQty(total: number, marks: Marks): number {
  return Math.max(0, round(total - sumMarks(marks)))
}

/** Las marcas ajustadas a un total que pudo bajar: se recorre el listado en su orden y se recorta lo que pase del total (los últimos pierden). */
export function capMarks(options: readonly MarkOption[], marks: Marks, total: number): Marks {
  const next: Marks = {}
  let left = Math.max(0, total)
  for (const o of options) {
    const q = marks[o.key]
    if (!q || q <= 0 || left <= 0) continue
    const take = round(Math.min(q, left, o.capacity ?? q))
    if (take <= 0) continue
    next[o.key] = take
    left = round(left - take)
  }
  return next
}

/** Marca o desmarca una posición. Al marcar toma lo que falte hasta el tope de la posición; si ya no falta nada, no marca. */
export function toggleMark(options: readonly MarkOption[], marks: Marks, key: string, total: number): Marks {
  const capped = capMarks(options, marks, total)
  if (capped[key] !== undefined) {
    const { [key]: _removed, ...rest } = capped
    return rest
  }
  const option = options.find((o) => o.key === key)
  const missing = remainingQty(total, capped)
  if (!option || missing <= 0) return capped
  const take = round(Math.min(missing, option.capacity ?? missing))
  return take > 0 ? { ...capped, [key]: take } : capped
}

/** Marca de una vez las recomendadas (el plan sugerido), sin tocar lo ya marcado. */
export function markRecommended(options: readonly MarkOption[], marks: Marks, total: number): Marks {
  let next = capMarks(options, marks, total)
  for (const o of options) {
    if (o.recommended && next[o.key] === undefined) next = toggleMark(options, next, o.key, total)
  }
  return next
}

/** El listado ordenado: primero las recomendadas, luego las demás, cada grupo en su orden original. */
export function sortRecommendedFirst<T extends { recommended: boolean }>(options: readonly T[]): T[] {
  return [...options.filter((o) => o.recommended), ...options.filter((o) => !o.recommended)]
}

/** ¿Las marcas completan exactamente el total? */
export function marksComplete(marks: Marks, total: number): boolean {
  return total > 0 && sumMarks(marks) === round(total)
}

/** Las marcas como renglones (posición, cantidad) en el orden del listado. */
export function marksToRows(options: readonly MarkOption[], marks: Marks): Array<{ key: string; binCode: string; qty: number }> {
  return options.filter((o) => (marks[o.key] ?? 0) > 0).map((o) => ({ key: o.key, binCode: o.binCode, qty: marks[o.key] }))
}
