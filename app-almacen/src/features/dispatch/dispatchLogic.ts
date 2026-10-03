// Lote 8A-app — reglas puras de Despacho (docs/mobile/app-almacen-plan.md §2, pantalla 5). Sin API ni base: solo
// construye y valida lo que se ve en pantalla.
// Recolectar (producto, cantidad, posición) funciona sin señal: la posición se guarda tal como se escaneó, sin
// verificarla contra el servidor todavía (no hay una tabla local de posiciones, docs/lote8A-app-decisiones.md). Se
// resuelve a su id real recién al empacar (dispatchApi.ts), que de todas formas ya necesita señal para elegir el
// consignatario.
/**
 * docs/mobile/mejoras-ux-zebra.md §2 ("escanear = aceptar", sin un "Continuar" tras la lectura): escanear la posición de
 * donde sale el producto AGREGA la línea si la cantidad ya es válida (la cantidad se escribe antes, arriba; es el paso
 * manual). Con la cantidad vacía o inválida solo guarda la posición y se agrega con el botón, como antes.
 * Decisión para el dueño (docs/mobile/loteA3-decisiones.md): con `false` vuelve al flujo anterior (siempre "Agregar").
 */
export const DISPATCH_ADD_ON_BIN_SCAN = true

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
  return { productPublicId: product.publicId, sku: product.sku, productName: product.name, qtyText: '1', fromBinCode: '' }
}

function parseQty(text: string): number {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : 0
}

export function canAddPickLine(draft: PickLineDraft): boolean {
  return parseQty(draft.qtyText) > 0 && draft.fromBinCode.trim().length > 0
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
