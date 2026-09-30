// Lote 14 — lógica pura de los modales de ajuste y transferencia (InventoryAdjustModal, InventoryTransferModal y el bloque
// de ajuste de ProductEditorModal): lo disponible en una posición (pista "Disponible en la posición: N" y tope al bajar),
// los lotes con saldo en la posición (al bajar un producto LOT) y los "ítems" de una posición de origen (producto + lote)
// para transferir en el orden origen → ítem → destino.
import type { components } from '../../kernel/api/schema'
import { signedAdjustQuantity, type AdjustDirection } from './adjustmentReasons'
import type { BalanceDto, SerialDto } from './api'
import { parseSerials } from './lineRules'

/** Suma de lo disponible (`qtyAvailable`) de los saldos de un producto en una posición; con `lotId`, solo ese lote. */
export function availableAt(balances: readonly BalanceDto[], lotId?: number | null): number {
  return balances.filter((b) => lotId == null || b.lotId === lotId).reduce((acc, b) => acc + (b.qtyAvailable ?? 0), 0)
}

/** Lotes con disponible > 0 en la posición (opciones del lote al bajar): valor = lotId, pista = disponible y vencimiento. */
export function lotOptions(balances: readonly BalanceDto[], availableText: (qty: number, expiry: string | null | undefined) => string) {
  return balances
    .filter((b) => b.lotId != null && (b.qtyAvailable ?? 0) > 0)
    .map((b) => ({ value: String(b.lotId), label: b.lotNumber ?? `#${b.lotId}`, hint: availableText(b.qtyAvailable ?? 0, b.expiryDate) }))
}

/** Clave de un ítem transferible: producto + lote ("<publicId>|<lotId>" o "<publicId>|" sin lote). */
export function transferItemKey(b: Pick<BalanceDto, 'productPublicId' | 'lotId'>): string {
  return `${b.productPublicId ?? ''}|${b.lotId ?? ''}`
}

/** Ítem de la clave (`transferItemKey`) o null. */
export function parseTransferItemKey(key: string): { productPublicId: string; lotId: number | null } | null {
  const [productPublicId, lot] = key.split('|')
  if (!productPublicId) return null
  const lotId = lot ? Number(lot) : null
  return { productPublicId, lotId: lotId != null && Number.isFinite(lotId) ? lotId : null }
}

/**
 * Ítems disponibles en la posición de origen (un saldo por producto y lote con disponible > 0) como opciones de
 * `ComboSelect`: "SKU · Nombre" (+ "Lote L-1"), con lo disponible como pista; se busca también por lote.
 */
export function transferItemOptions(
  balances: readonly BalanceDto[],
  text: { lot: (lot: string) => string; available: (qty: number) => string },
) {
  return balances
    .filter((b) => (b.qtyAvailable ?? 0) > 0 && b.productPublicId)
    .map((b) => ({
      value: transferItemKey(b),
      label: [b.sku, b.productName, b.lotNumber ? text.lot(b.lotNumber) : ''].filter(Boolean).join(' · '),
      hint: text.available(b.qtyAvailable ?? 0),
    }))
}

/** Series disponibles del producto en la posición (y en el lote, si se indica): opciones de la salida o la transferencia. */
export function serialsAt(serials: readonly SerialDto[], binId: number | null, lotId?: number | null): string[] {
  if (binId == null) return []
  return serials
    .filter((s) => s.binId === binId && (lotId == null || s.lotId === lotId) && s.serialNumber)
    .map((s) => s.serialNumber as string)
}

// ---------------------------------------------------------------------------------------------------------------------
// Ajuste (D11): valores del formulario → cuerpo del API
// ---------------------------------------------------------------------------------------------------------------------

/** Valores del formulario de ajuste (InventoryAdjustModal y bloque de ajuste de la ficha del producto). */
export interface AdjustFormValues {
  /** 'up' | 'down' | '' (sin elegir). */
  direction: string
  productPublicId: string | null
  warehousePublicId: string | null
  /** id de la posición como texto ('' = ninguna). */
  binId: string
  /** Cantidad en POSITIVO (la pantalla pone el signo). En productos SERIAL se ignora: cuenta el número de series. */
  quantity: number | null
  reason: string
  notes: string
  /** Subir un producto LOT: número de lote (nuevo o existente). */
  lotNumber: string
  /** Bajar un producto LOT: lote con saldo en la posición (id como texto). */
  lotId: string
  /** Subir un producto SERIAL: series nuevas (una por renglón o separadas por coma). */
  serialText: string
  /** Bajar un producto SERIAL: series disponibles en la posición. */
  serials: string[]
}

/** Series del ajuste según la dirección (subir = las escritas; bajar = las elegidas). */
export function adjustSerials(v: Pick<AdjustFormValues, 'direction' | 'serialText' | 'serials'>): string[] {
  return v.direction === 'down' ? [...v.serials] : parseSerials(v.serialText)
}

/** Magnitud del ajuste: el número de series en productos SERIAL; si no, la cantidad escrita. */
export function adjustMagnitude(v: AdjustFormValues, tracking: string): number {
  return tracking === 'SERIAL' ? adjustSerials(v).length : Math.abs(v.quantity ?? 0)
}

/**
 * Cuerpo de `POST /inventory/adjustments`: cantidad CON SIGNO (subir +, bajar −; el API no cambió), nota recortada; lote
 * nuevo por número al subir un LOT (`lot.number`) o el lote elegido al bajar (`lotId`); series en SERIAL.
 */
export function adjustmentBody(v: AdjustFormValues, tracking: string): components['schemas']['AdjustmentRequest'] {
  const direction = v.direction as AdjustDirection
  const serials = tracking === 'SERIAL' ? adjustSerials(v) : []
  const lotNumber = v.lotNumber.trim()
  return {
    productPublicId: v.productPublicId,
    warehousePublicId: v.warehousePublicId,
    binId: Number(v.binId),
    quantity: signedAdjustQuantity(direction, adjustMagnitude(v, tracking)),
    reason: v.reason,
    notes: v.notes.trim(),
    lot: tracking === 'LOT' && direction === 'up' && lotNumber ? { number: lotNumber } : undefined,
    lotId: tracking === 'LOT' && direction === 'down' && v.lotId ? Number(v.lotId) : undefined,
    serialNumbers: serials.length > 0 ? serials : undefined,
  }
}
