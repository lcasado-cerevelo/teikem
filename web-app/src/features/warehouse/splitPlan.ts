// Reparto por posición (pedido del dueño 2026-10-06 en la app; 2026-10-07 también en la web): la misma cantidad en cada posición, en el orden en que se
// eligen; caben las posiciones llenas y UNA más que recibe lo que quedaba (185 de 20 → 9 de 20 y una décima con 5). Lógica pura; es la misma regla del
// servidor (`WarehouseTaskRules.DistributionPlan`) y de la app (`putawayLogic.ts`).

/** Cantidad por posición escrita a mano (coma o punto); 0 si está vacía o no es válida (>0, hasta 3 decimales). */
export function parsePerBin(text: string): number {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 && Math.round(n * 1000) / 1000 === n ? n : 0
}

/** Posiciones llenas que caben (185 de 20 → 9) y lo que sobra (5): la posición siguiente recibe ese resto. */
export function fullAndRest(pending: number, perBin: number): { full: number; rest: number } {
  if (pending <= 0 || perBin <= 0) return { full: 0, rest: 0 }
  const full = Math.floor(Math.round((pending / perBin) * 1e6) / 1e6)
  return { full, rest: Math.round((pending - full * perBin) * 1000) / 1000 }
}

/** Cuántas posiciones caben: las llenas y UNA más con el resto, si lo hay (185 de 20 → 10; 180 de 20 → 9; 5 de 20 → 1). */
export function maxBins(pending: number, perBin: number): number {
  const { full, rest } = fullAndRest(pending, perBin)
  return full + (rest > 0 ? 1 : 0)
}

/** Lo que recibe la posición número `index` (0 = la primera): la cantidad por posición, o el resto si es la que ya no se llena. */
export function chunkAt(pending: number, perBin: number, index: number): number {
  const { full, rest } = fullAndRest(pending, perBin)
  return index < full ? perBin : rest
}

/** ¿Esa posición recibe menos que la cantidad por posición (la del resto)? Es la que lleva la alerta fija. */
export function isRestBin(pending: number, perBin: number, index: number): boolean {
  return chunkAt(pending, perBin, index) < perBin
}

export interface SplitBin {
  id: number
  code: string
  /** Espacio libre según el cupo de la posición (cupo − existencia); null/ausente = sin cupo configurado. */
  free?: number | null
}

export type AddSplitResult = { ok: true; bins: SplitBin[] } | { ok: false; reason: 'duplicate' | 'full' | 'perBin' }

/** Suma una posición al reparto: rechaza la repetida y la que ya no cabe (no quedan unidades por repartir). */
export function addSplitBin(bins: readonly SplitBin[], bin: SplitBin, pending: number, perBin: number): AddSplitResult {
  if (perBin <= 0) return { ok: false, reason: 'perBin' }
  if (bins.some((b) => b.id === bin.id)) return { ok: false, reason: 'duplicate' }
  if (bins.length >= maxBins(pending, perBin)) return { ok: false, reason: 'full' }
  return { ok: true, bins: [...bins, bin] }
}

/** Total repartido con `count` posiciones y lo que queda pendiente. */
export function splitSummary(pending: number, perBin: number, count: number): { total: number; left: number } {
  let total = 0
  for (let i = 0; i < count; i++) total += chunkAt(pending, perBin, i)
  total = Math.round(total * 1000) / 1000
  return { total, left: Math.round((pending - total) * 1000) / 1000 }
}

/** Espacio libre de una posición: cupo − existencia (nunca negativo); sin cupo = null. */
export function freeQtyOf(maxCapacityQty: number | null | undefined, qtyOnHand: number | null | undefined): number | null {
  return maxCapacityQty == null ? null : Math.max(0, maxCapacityQty - (qtyOnHand ?? 0))
}

/** ¿Lo que recibirá la posición excede su espacio libre? Sin cupo o sin dato nunca avisa (el cupo solo avisa, no bloquea). */
export function exceedsCapacity(free: number | null | undefined, qty: number): boolean {
  return free != null && qty > free
}
