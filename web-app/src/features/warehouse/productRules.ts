// Reglas de captura del producto (réplica de ProductRules del dominio) que usa el modal único de alta y edición
// (ProductEditorModal). Los mensajes son los exactos del manual 06 (claves `warehouse.products.errors.*`).
// También la cantidad de un ajuste manual (AdjustmentRules.ToPosting), compartida por InventoryAdjustModal y el bloque
// "Ajustar inventario" del modal de producto.
import { z } from 'zod'
import type { TParams } from '../../kernel/i18n'

type Translate = (key: string, params?: TParams) => string

/** Decimales escritos de un número (1.25 → 2). */
export function decimals(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

/** Peso en kg: no negativo; a lo sumo 3 decimales y menor que 10⁹ (DECIMAL(12,3)). Vacío = null. */
export function weightKgSchema(t: Translate) {
  return z
    .number(t('warehouse.products.errors.numberInvalid'))
    .min(0, t('warehouse.products.errors.measuresNegative'))
    .refine((v) => decimals(v) <= 3 && v < 1_000_000_000, t('warehouse.products.errors.weightInvalid'))
    .nullable()
}

/** Volumen en m³: no negativo; a lo sumo 4 decimales y menor que 10⁸ (DECIMAL(12,4)). Vacío = null. */
export function volumeM3Schema(t: Translate) {
  return z
    .number(t('warehouse.products.errors.numberInvalid'))
    .min(0, t('warehouse.products.errors.measuresNegative'))
    .refine((v) => decimals(v) <= 4 && v < 100_000_000, t('warehouse.products.errors.volumeInvalid'))
    .nullable()
}

/** Tope de costo/precio (DECIMAL(18,4)): ProductRules.Money rechaza `v >= 10¹⁴` con MoneyTooLarge. */
export const MONEY_MAX_EXCLUSIVE = 100_000_000_000_000

/**
 * Costo (`field = 'cost'`) o precio (`'price'`): no negativo, a lo sumo 4 decimales (mensaje por campo, como
 * `ProductRules.MoneyDecimals('costo' | 'precio')`) y menor que 10¹⁴ (MoneyTooLarge). Vacío = null.
 */
export function moneySchema(t: Translate, field: 'cost' | 'price') {
  return z
    .number(t('warehouse.products.errors.numberInvalid'))
    .min(0, t('warehouse.products.errors.negative'))
    .refine((v) => decimals(v) <= 4, t(field === 'cost' ? 'warehouse.products.errors.costDecimals' : 'warehouse.products.errors.priceDecimals'))
    .refine((v) => v < MONEY_MAX_EXCLUSIVE, t('warehouse.products.errors.moneyTooLarge'))
    .nullable()
}

/**
 * Cantidad de un ajuste manual (+ entra, − sale): obligatoria, distinta de cero y a lo sumo 3 decimales, con los
 * mensajes de `warehouse.inventory.adjustModal.errors.*`. Vacío (null) = "Se esperaba un número.".
 */
export function adjustQuantitySchema(t: Translate) {
  return z
    .number(t('warehouse.inventory.adjustModal.errors.quantityInvalid'))
    .nullable()
    .superRefine((v, ctx) => {
      if (v === null) {
        ctx.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityInvalid') })
        return
      }
      if (v === 0) ctx.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityZero') })
      else if (decimals(v) > 3) ctx.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityDecimals') })
    })
}
