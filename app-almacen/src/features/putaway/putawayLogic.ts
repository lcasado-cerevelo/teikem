// Lote 8A-app — reglas puras de Acomodar (docs/mobile/app-almacen-plan.md §2, pantalla 4). Sin API ni base: solo
// ordena y decide qué mostrar, para poder probarlo sin montar nada.
export interface PutawayTask {
  id: number
  sku: string
  productName: string
  quantity: number | null
  toBinCode: string | null
  assignedToUserId: number | null
}

/** Las tareas de quien tiene el aparato en mano van primero; el resto, más viejas primero (orden de llegada). */
export function sortTasksMineFirst(tasks: PutawayTask[], myUserId: number): PutawayTask[] {
  const mine = tasks.filter((t) => t.assignedToUserId === myUserId)
  const others = tasks.filter((t) => t.assignedToUserId !== myUserId)
  return [...mine, ...others]
}

// ---------------------------------------------------------------- reparto por posición (misma regla que el servidor)

/** Cantidad por posición escrita a mano (coma o punto); 0 si está vacía o no es válida. */
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

/** ¿Esa posición recibe menos que la cantidad por posición (la del resto)? Es la que lleva la alerta. */
export function isRestBin(pending: number, perBin: number, index: number): boolean {
  return chunkAt(pending, perBin, index) < perBin
}

export interface DistBin {
  id: number
  code: string
}

export type AddBinResult = { ok: true; bins: DistBin[] } | { ok: false; reason: 'duplicate' | 'full' | 'perBin' }

/** Suma una posición al reparto: rechaza la repetida y la que ya no cabe (no quedan unidades por repartir). */
export function addDistBin(bins: readonly DistBin[], bin: DistBin, pending: number, perBin: number): AddBinResult {
  if (perBin <= 0) return { ok: false, reason: 'perBin' }
  if (bins.some((b) => b.id === bin.id)) return { ok: false, reason: 'duplicate' }
  if (bins.length >= maxBins(pending, perBin)) return { ok: false, reason: 'full' }
  return { ok: true, bins: [...bins, bin] }
}

/** Total repartido con `count` posiciones y lo que queda pendiente tras confirmar. */
export function distSummary(pending: number, perBin: number, count: number): { total: number; left: number } {
  let total = 0
  for (let i = 0; i < count; i++) total += chunkAt(pending, perBin, i)
  total = Math.round(total * 1000) / 1000
  return { total, left: Math.round((pending - total) * 1000) / 1000 }
}
