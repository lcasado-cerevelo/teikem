// Lote 8A-app — reglas puras de la captura de Recibir (docs/mobile/app-almacen-plan.md §2, pantalla 3). Sin base de
// datos ni API: solo construye y valida lo que se ve en pantalla, para poder probarlo sin montar nada.
import type { TrackingType } from './localLookup'

export interface DraftLine {
  productPublicId: string
  sku: string
  productName: string
  trackingTypeCode: TrackingType
  receivedQty: number
  lotNumber: string | null
  expiryDate: string | null
  serialNumbers: string[] | null
}

export interface LineDraft {
  productPublicId: string
  sku: string
  productName: string
  trackingTypeCode: TrackingType
  qtyText: string
  lot: string
  expiry: string
  serials: string[]
}

export function newLineDraft(product: { publicId: string; sku: string; name: string; trackingTypeCode: TrackingType }): LineDraft {
  return {
    productPublicId: product.publicId,
    sku: product.sku,
    productName: product.name,
    trackingTypeCode: product.trackingTypeCode,
    qtyText: '1',
    lot: '',
    expiry: '',
    serials: [],
  }
}

function parseQty(text: string): number {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : 0
}

/** El producto necesita lote o series antes de poder agregar la línea. */
export function requiresLot(draft: Pick<LineDraft, 'trackingTypeCode'>): boolean {
  return draft.trackingTypeCode === 'LOT'
}

export function requiresSerials(draft: Pick<LineDraft, 'trackingTypeCode'>): boolean {
  return draft.trackingTypeCode === 'SERIAL'
}

/** ¿Se puede agregar la línea tal como está? */
export function canAddLine(draft: LineDraft): boolean {
  if (requiresSerials(draft)) return draft.serials.length > 0
  if (requiresLot(draft)) return draft.lot.trim().length > 0 && parseQty(draft.qtyText) > 0
  return parseQty(draft.qtyText) > 0
}

/** Agrega un número de serie a la lista (sin repetir vacíos ni el mismo dos veces). */
export function addSerial(draft: LineDraft, serial: string): LineDraft {
  const value = serial.trim()
  if (!value || draft.serials.includes(value)) return draft
  return { ...draft, serials: [...draft.serials, value] }
}

export function removeSerial(draft: LineDraft, serial: string): LineDraft {
  return { ...draft, serials: draft.serials.filter((s) => s !== serial) }
}

/** Construye la línea final a partir del borrador (asume canAddLine(draft) === true). */
export function buildLine(draft: LineDraft): DraftLine {
  if (requiresSerials(draft)) {
    return {
      productPublicId: draft.productPublicId,
      sku: draft.sku,
      productName: draft.productName,
      trackingTypeCode: draft.trackingTypeCode,
      receivedQty: draft.serials.length,
      lotNumber: null,
      expiryDate: null,
      serialNumbers: draft.serials,
    }
  }
  return {
    productPublicId: draft.productPublicId,
    sku: draft.sku,
    productName: draft.productName,
    trackingTypeCode: draft.trackingTypeCode,
    receivedQty: parseQty(draft.qtyText),
    lotNumber: requiresLot(draft) ? draft.lot.trim() : null,
    expiryDate: requiresLot(draft) && draft.expiry.trim() ? draft.expiry.trim() : null,
    serialNumbers: null,
  }
}

export interface ReceiptDoc {
  purchaseOrderPublicId?: string | null
  asnId?: number | null
}

/** Cuerpo de POST /api/v1/receipts en una sola llamada (confirm:true), decisión 2 de docs/lote8A-decisiones.md. */
export function buildReceiptBody(warehousePublicId: string, doc: ReceiptDoc | null, lines: DraftLine[]) {
  return {
    warehousePublicId,
    purchaseOrderPublicId: doc?.purchaseOrderPublicId ?? null,
    asnId: doc?.asnId ?? null,
    confirm: true,
    lines: lines.map((l) => ({
      productPublicId: l.productPublicId,
      receivedQty: l.receivedQty,
      lot: l.lotNumber ? { number: l.lotNumber, expiryDate: l.expiryDate || null } : null,
      serialNumbers: l.serialNumbers,
    })),
  }
}
