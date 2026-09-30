// Reglas de captura del producto (réplica de ProductRules del dominio) que usa el modal único de alta y edición
// (ProductEditorModal). Los mensajes son los exactos del manual 06 (claves `warehouse.products.errors.*`).
// También la cantidad de un ajuste manual (AdjustmentRules.ToPosting), compartida por InventoryAdjustModal y el bloque
// "Ajustar inventario" del modal de producto. Lote 12: Marca y Modelo (máx. 100) y la nota obligatoria de todo ajuste manual
// (`adjustNotesSchema`, la misma en el modal de producto, el del Kárdex y el ajuste manual de un faltante de compra).
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

/** Largo máximo de Marca y Modelo (Lote 12, `ProductRules.BrandMaxLength`/`ModelMaxLength`, NVARCHAR(100)). */
export const BRAND_MAX = 100
export const MODEL_MAX = 100

/** Marca o modelo: texto libre opcional (recortado), máximo 100, con el mensaje exacto del API (400 `errors.brand|model`). */
export function brandModelSchema(t: Translate, field: 'brand' | 'model') {
  return z
    .string()
    .trim()
    .max(field === 'brand' ? BRAND_MAX : MODEL_MAX, t(field === 'brand' ? 'warehouse.products.errors.brandMax' : 'warehouse.products.errors.modelMax'))
}

/** Máximo de la nota de un ajuste (`AdjustmentRules.MaxNotesLength`, NVARCHAR(300)). */
export const ADJUST_NOTES_MAX = 300

/**
 * Nota de todo ajuste manual de inventario: obligatoria (recortada) y de hasta 300 caracteres, con los mensajes exactos del
 * API (`AdjustmentRules.NotesRequired` 'Escriba una nota que explique el ajuste.' y 'Las notas admiten como máximo 300
 * caracteres.', 400 en `errors.notes`). La usan el bloque de ajuste de ProductEditorModal, InventoryAdjustModal (Kárdex) y,
 * solo para la acción MANUAL_ADJUSTMENT, ResolveShortageModal.
 */
export function adjustNotesSchema(t: Translate) {
  return z
    .string()
    .trim()
    .min(1, t('warehouse.products.errors.adjustNotesRequired'))
    .max(ADJUST_NOTES_MAX, t('warehouse.products.errors.adjustNotesMax'))
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

/**
 * Lote 14 (D11) — dirección del ajuste ("Subir"/"Bajar"): obligatoria, con el mensaje de pantalla 'Elija si el ajuste sube
 * o baja el inventario.'. Valor del formulario: 'up' | 'down' | '' (sin elegir).
 */
export function adjustDirectionSchema(t: Translate) {
  return z.string().refine((v) => v === 'up' || v === 'down', t('warehouse.inventory.adjustModal.errors.directionRequired'))
}

/**
 * Lote 14 (D11) — cantidad del ajuste en positivo (la pantalla pone el signo según Subir/Bajar): obligatoria, mayor que
 * cero ('La cantidad debe ser mayor que cero.') y a lo sumo 3 decimales. Vacío (null) = "Se esperaba un número.".
 */
export function adjustMagnitudeSchema(t: Translate) {
  return z
    .number(t('warehouse.inventory.adjustModal.errors.quantityRequired'))
    .nullable()
    .superRefine((v, ctx) => {
      if (v === null) {
        ctx.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityRequired') })
        return
      }
      if (v <= 0) ctx.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityPositive') })
      else if (decimals(v) > 3) ctx.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityDecimals') })
    })
}
