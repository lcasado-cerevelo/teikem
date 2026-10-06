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

/** Cuántas posiciones caben: solo las llenas (185 de 20 → 9); si la tarea es menor que la cantidad por posición, una sola. */
export function maxBins(pending: number, perBin: number): number {
  if (pending <= 0 || perBin <= 0) return 0
  return perBin >= pending ? 1 : Math.floor(pending / perBin)
}

/** Lo que recibe cada posición: la cantidad por posición, o todo lo pendiente si es menor. */
export function chunkQty(pending: number, perBin: number): number {
  return perBin >= pending ? pending : perBin
}

export interface DistBin {
  id: number
  code: string
}

export type AddBinResult = { ok: true; bins: DistBin[] } | { ok: false; reason: 'duplicate' | 'full' | 'perBin' }

/** Suma una posición al reparto: rechaza la repetida y la que ya no cabe (los sueltos se acomodan aparte). */
export function addDistBin(bins: readonly DistBin[], bin: DistBin, pending: number, perBin: number): AddBinResult {
  if (perBin <= 0) return { ok: false, reason: 'perBin' }
  if (bins.some((b) => b.id === bin.id)) return { ok: false, reason: 'duplicate' }
  if (bins.length >= maxBins(pending, perBin)) return { ok: false, reason: 'full' }
  return { ok: true, bins: [...bins, bin] }
}

/** Total repartido y lo que queda pendiente tras confirmar. */
export function distSummary(pending: number, perBin: number, count: number): { total: number; left: number } {
  const total = Math.round(chunkQty(pending, perBin) * count * 1000) / 1000
  return { total, left: Math.round((pending - total) * 1000) / 1000 }
}
