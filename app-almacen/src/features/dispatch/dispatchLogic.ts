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
