// Lógica pura de los selectores con buscador del almacén (WarehousePicker, BinPicker): comparación de texto y orden de
// las opciones. Pensada para escribir a mano o con un lector de código de barras (teclea el código y manda Enter).

/** Texto para comparar: sin mayúsculas ni acentos. */
export function foldText(s: string | null | undefined): string {
  return (s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}

/** Primera opción cuyo código es exactamente `text` (sin mayúsculas ni acentos, sin espacios a los lados). */
export function exactCodeMatch<T extends { code?: string | null }>(items: readonly T[], text: string): T | undefined {
  const q = foldText(text.trim())
  if (!q) return undefined
  return items.find((i) => foldText(i.code) === q)
}

/**
 * Almacenes cuyo código o nombre contiene `text` (subcadena, sin mayúsculas ni acentos), en el orden de la lista; la
 * coincidencia exacta por código va primero (es la que elige Enter). Texto vacío = todos.
 */
export function filterWarehouses<T extends { code?: string | null; name?: string | null }>(items: readonly T[], text: string): T[] {
  const q = foldText(text.trim())
  if (!q) return [...items]
  const hits = items.filter((w) => foldText(w.code).includes(q) || foldText(w.name).includes(q))
  return exactFirst(hits, text)
}

/**
 * Orden de las posiciones que devolvió el API: primero la coincidencia exacta por código con `text`, luego las sugeridas
 * (en el orden de `suggestedIds`) y después el resto en el orden del servidor.
 */
export function orderBins<T extends { id?: number; code?: string | null }>(items: readonly T[], text: string, suggestedIds: readonly number[] = []): T[] {
  const rank = new Map(suggestedIds.map((id, i) => [id, i]))
  const suggested = items.filter((b) => b.id != null && rank.has(b.id)).sort((a, b) => (rank.get(a.id ?? 0) ?? 0) - (rank.get(b.id ?? 0) ?? 0))
  const rest = items.filter((b) => b.id == null || !rank.has(b.id))
  return exactFirst([...suggested, ...rest], text)
}

/**
 * Posiciones de una lista dada (BinPicker con `options`) cuyo código o zona contiene `text` (subcadena, sin mayúsculas ni
 * acentos), en el orden de la lista. Texto vacío = todas.
 */
export function filterBinOptions<T extends { code?: string | null; zoneCode?: string | null }>(items: readonly T[], text: string): T[] {
  const q = foldText(text.trim())
  if (!q) return [...items]
  return items.filter((b) => foldText(b.code).includes(q) || foldText(b.zoneCode).includes(q))
}

function exactFirst<T extends { code?: string | null }>(items: T[], text: string): T[] {
  const exact = exactCodeMatch(items, text)
  if (!exact) return items
  return [exact, ...items.filter((i) => i !== exact)]
}
