// Lote 8A-app — reglas puras de Despacho (docs/mobile/app-almacen-plan.md §2, pantalla 5). Sin API ni base: solo
// construye y valida lo que se ve en pantalla.
// Recolectar (producto, cantidad, posición) funciona sin señal: la posición se guarda tal como se escaneó, sin
// verificarla contra el servidor todavía (no hay una tabla local de posiciones, docs/lote8A-app-decisiones.md). Se
// resuelve a su id real recién al empacar (dispatchApi.ts), que de todas formas ya necesita señal para elegir el
// consignatario.
/**
 * docs/mobile/mejoras-ux-zebra.md §2 ("escanear = aceptar"): escanear la posición de donde sale el producto AGREGA la línea.
 * Lote A5 — decisión del dueño 5 (docs/decisiones-del-dueno-2026-10-03.md): **la cantidad va primero**. La cantidad arranca
 * vacía (no hay "1" por omisión); escanear la posición sin una cantidad mayor que 0 no agrega nada ni guarda la posición:
 * la pantalla avisa y se vuelve a escanear después de escribirla (binScanOutcome).
 */

export interface PickLine {
  productPublicId: string
  sku: string
  productName: string
  quantity: number
  fromBinCode: string
}

export interface PickLineDraft {
  productPublicId: string
  sku: string
  productName: string
  qtyText: string
  fromBinCode: string
}

export function newPickLineDraft(product: { publicId: string; sku: string; name: string }): PickLineDraft {
  return { productPublicId: product.publicId, sku: product.sku, productName: product.name, qtyText: '', fromBinCode: '' }
}

function parseQty(text: string): number {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : 0
}

/** Estado de la cantidad escrita: `missing` = en blanco; `invalid` = 0, negativa o no es un número; `ok` = mayor que 0. */
export type PickQtyState = 'ok' | 'missing' | 'invalid'

export function pickQtyState(qtyText: string): PickQtyState {
  if (qtyText.trim() === '') return 'missing'
  return parseQty(qtyText) > 0 ? 'ok' : 'invalid'
}

/** Qué hace la lectura de la posición (decisión del dueño 5): con cantidad > 0 agrega la línea al instante; sin cantidad o con
 *  una cantidad inválida no agrega nada (y la posición no se guarda: se vuelve a escanear tras escribir la cantidad). */
export type BinScanOutcome = { kind: 'add'; line: PickLine } | { kind: 'needQty' } | { kind: 'invalidQty' } | { kind: 'noBin' }

export function binScanOutcome(draft: PickLineDraft, binCode: string): BinScanOutcome {
  if (binCode.trim() === '') return { kind: 'noBin' }
  const qty = pickQtyState(draft.qtyText)
  if (qty === 'missing') return { kind: 'needQty' }
  if (qty === 'invalid') return { kind: 'invalidQty' }
  return { kind: 'add', line: buildPickLine({ ...draft, fromBinCode: binCode }) }
}

export function buildPickLine(draft: PickLineDraft): PickLine {
  return {
    productPublicId: draft.productPublicId,
    sku: draft.sku,
    productName: draft.productName,
    quantity: parseQty(draft.qtyText),
    fromBinCode: draft.fromBinCode.trim(),
  }
}

/** Un despacho lleva productos de UN solo dueño (regla del servidor): el cliente 3PL del despacho abierto (null = inventario
 *  propio) debe ser el dueño del producto escaneado (null = propio). */
export function sameOwner(openClientPublicId: string | null | undefined, productOwnerPublicId: string | null | undefined): boolean {
  return (openClientPublicId ?? null) === (productOwnerPublicId ?? null)
}

export interface ClientChoice {
  publicId: string
  label: string
}

export interface ConsigneeChoice {
  publicId: string
  label: string
}

export interface ResolvedPickLine extends PickLine {
  fromBinId: number
}

/** Códigos de posición distintos entre las líneas, en el orden en que aparecen (para resolverlos a binId al empacar). */
export function uniqueBinCodes(lines: PickLine[]): string[] {
  const seen = new Set<string>()
  const codes: string[] = []
  for (const l of lines) {
    if (!seen.has(l.fromBinCode)) {
      seen.add(l.fromBinCode)
      codes.push(l.fromBinCode)
    }
  }
  return codes
}

/** Cuerpo de POST /api/v1/pick-batches/collect-and-pack en una sola llamada (decisión 2 de docs/lote8A-decisiones.md). */
export function buildCollectAndPackBody(
  warehousePublicId: string,
  clientPublicId: string,
  consigneeLocationPublicId: string,
  pieces: number,
  lines: ResolvedPickLine[],
) {
  return {
    warehousePublicId,
    lines: lines.map((l) => ({ productPublicId: l.productPublicId, quantity: l.quantity, binId: l.fromBinId })),
    pack: {
      order: {
        clientPublicId,
        consigneeLocationPublicId,
        // Sin selector de servicio en el aparato (decisión implícita, igual que confirmNow): el catálogo ServiceType
        // trae STANDARD sembrado (Diseño/logistica-db-seed.sql); OrderService.CreateAsync exige uno si el tenant no
        // tiene default configurado.
        serviceType: 'STANDARD',
        // Mismo motivo que serviceType arriba: el catálogo PackageType trae BOX sembrado; cada paquete lo exige si
        // el tenant no tiene un tipo por defecto (OrderService.CreateAsync).
        packages: [{ packageType: 'BOX', pieces }],
        confirmNow: true,
      },
    },
  }
}

// ------------------------------------------------------------------ posición sugerida (FEFO) — pedido del dueño 2026-10-05

/** Existencia DISPONIBLE de un producto en una posición (y lote), con su lugar en el orden de salida. El ORDEN lo calcula el servidor
 *  (GET /inventory/exit-options, PickBatchRules.Eligible: vence primero, zona, código; sin cuarentena ni cruce ni posiciones inactivas): la app
 *  no lo recalcula, solo lo sigue por `rank` (1 = sale primero). */
export interface StockOption {
  binCode: string
  zoneTypeCode: string | null
  lotNumber: string | null
  /** AAAA-MM-DD; null = sin vencimiento. */
  expiryDate: string | null
  available: number
  rank: number
}

/** Las existencias en el orden de salida del servidor (por rank), solo con disponible. */
export function inExitOrder(options: readonly StockOption[]): StockOption[] {
  return options.filter((o) => o.available > 0).sort((a, b) => a.rank - b.rank)
}

/** De dónde debe salir lo que sigue: recorre el orden de salida del servidor descontando lo que este despacho ya sacó (`alreadyPicked`, en la
 *  unidad del producto) y devuelve la primera existencia con algo disponible (con lo que queda de ella). null = no hay de dónde. */
export function nextStockOption(options: readonly StockOption[], alreadyPicked: number): StockOption | null {
  let skip = Math.max(0, alreadyPicked)
  for (const o of inExitOrder(options)) {
    if (skip >= o.available) {
      skip -= o.available
      continue
    }
    return { ...o, available: o.available - skip }
  }
  return null
}

/** Producto con lote: la posición NO es opcional, es la del próximo lote en salir (FEFO). `ok` = la posición escaneada es esa y alcanza
 *  la cantidad; `otherBin` = es otra; `tooMuch` = es esa pero no alcanza (hay que sacar lo que hay y escanear la siguiente). */
export type LotBinCheck = { kind: 'ok' } | { kind: 'otherBin'; expected: StockOption } | { kind: 'tooMuch'; expected: StockOption }

export function checkLotBin(expected: StockOption | null, scannedBinCode: string, qty: number): LotBinCheck {
  if (!expected) return { kind: 'ok' }
  if (expected.binCode.trim().toUpperCase() !== scannedBinCode.trim().toUpperCase()) return { kind: 'otherBin', expected }
  if (qty > expected.available) return { kind: 'tooMuch', expected }
  return { kind: 'ok' }
}

/** Cantidad ya sacada de un producto en las líneas de este despacho. */
export function pickedQty(lines: readonly PickLine[], productPublicId: string): number {
  return lines.filter((l) => l.productPublicId === productPublicId).reduce((sum, l) => sum + l.quantity, 0)
}

// ------------------------------------------------------------------ plan de salida con varias posiciones (tarea 24d)

/** Una parte del plan: cuánto sacar de una posición (y lote). */
export interface PlanRow {
  binCode: string
  lotNumber: string | null
  expiryDate: string | null
  qty: number
}

/**
 * Reparte la cantidad a despachar siguiendo el orden de salida del servidor, descontando lo que este despacho ya sacó del producto
 * (`alreadyPicked`): 50 con 20 en A-01 y 40 en B-03 → 20 de A-01 y 30 de B-03. `short` = lo que no alcanza (0 si hay suficiente).
 */
export function planExit(options: readonly StockOption[], qty: number, alreadyPicked: number): { rows: PlanRow[]; short: number } {
  const rows: PlanRow[] = []
  let left = Math.max(0, qty)
  let skip = Math.max(0, alreadyPicked)
  for (const o of inExitOrder(options)) {
    if (left <= 0) break
    let avail = o.available
    if (skip > 0) {
      const used = Math.min(skip, avail)
      skip -= used
      avail -= used
    }
    if (avail <= 0) continue
    const take = Math.min(avail, left)
    rows.push({ binCode: o.binCode, lotNumber: o.lotNumber, expiryDate: o.expiryDate, qty: take })
    left = Math.round((left - take) * 1000) / 1000
  }
  return { rows, short: left }
}

export type ReplaceBinResult = { ok: true; rows: PlanRow[] } | { ok: false; reason: 'noStock'; available: number }

/** Cambia la posición de una parte del plan por otra escaneada: debe tener existencia disponible del producto para lo que ya
 *  se sacará de ella en el plan (las demás partes que usan esa posición cuentan). Sin existencia suficiente → rechazo con lo que hay. */
export function replacePlanBin(rows: readonly PlanRow[], index: number, binCode: string, options: readonly StockOption[]): ReplaceBinResult {
  const wanted = binCode.trim().toUpperCase()
  const here = options.filter((o) => o.binCode.trim().toUpperCase() === wanted && o.available > 0)
  const available = here.reduce((sum, o) => sum + o.available, 0)
  const usedElsewhere = rows.reduce((sum, r, i) => (i !== index && r.binCode.trim().toUpperCase() === wanted ? sum + r.qty : sum), 0)
  if (here.length === 0 || available - usedElsewhere < rows[index].qty) return { ok: false, reason: 'noStock', available: Math.max(0, available - usedElsewhere) }
  const first = here.sort((a, b) => a.rank - b.rank)[0]
  const next = rows.map((r, i) => (i === index ? { ...r, binCode: first.binCode, lotNumber: first.lotNumber, expiryDate: first.expiryDate } : r))
  return { ok: true, rows: next }
}
