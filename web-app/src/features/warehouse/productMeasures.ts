// Lote 230 — reglas puras de las MEDIDAS Y EL PESO del producto en el editor (espejo de `ProductDimensionRules` del dominio, sin React):
// - Largo, ancho y alto se escriben en una unidad (IN, CM, FT, M) que la pantalla pide y sugiere (la del producto o la de la región de la compañía,
//   GET /products/units-suggestion). Son de la UNIDAD BASE del producto (lo que se vende), no de la caja ni de la paleta.
// - Con las TRES medidas el volumen se CALCULA y no se edita: se muestra en la unidad cúbica de las medidas (in³, cm³, ft³, m³) y el servidor guarda
//   los m³ (4 decimales); un volumen mandado a mano se ignora, así que no se manda.
// - Si falta cualquiera, el volumen se escribe a mano EN LA UNIDAD CÚBICA de las medidas y viaja convertido a m³ (`volumeM3`, 4 decimales). Un volumen
//   ya guardado no se pierde por poner o quitar una medida suelta: solo se manda si se tocó (o si cambió la unidad en que está escrito).
// - 0 o vacío en una medida = quitarla (en el PATCH una medida sin tocar va null = sin cambio).
// - El peso se escribe en LB o KG (sugerida igual) y viaja como `weight` + `weightUnit` (el servidor lo guarda en kg); `weightKg` ya no se manda.
// El cálculo local es solo para mostrar antes de guardar: redondea igual que el servidor (4 decimales, mitad lejos de cero).
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import type { TParams } from '../../kernel/i18n'
import { decimals } from './productRules'

type Translate = (key: string, params?: TParams) => string
type ProductDetailDto = components['schemas']['ProductDetailDto']
type ProductCreateRequest = components['schemas']['ProductCreateRequest']
type ProductPatchRequest = components['schemas']['ProductPatchRequest']

export const DIMENSION_UNITS = ['IN', 'CM', 'FT', 'M'] as const
export const WEIGHT_UNITS = ['LB', 'KG'] as const
export type DimensionUnit = (typeof DIMENSION_UNITS)[number]
export type WeightUnit = (typeof WEIGHT_UNITS)[number]

export const METERS_PER: Record<DimensionUnit, number> = { IN: 0.0254, CM: 0.01, FT: 0.3048, M: 1 }
export const KG_PER_POUND = 0.45359237
/** Tope de una medida y del volumen en m³ (DECIMAL(12,4)) y del peso en kg (DECIMAL(12,3)). */
export const MAX_DIMENSION = 100_000_000
export const MAX_VOLUME_M3 = 100_000_000
export const MAX_WEIGHT = 1_000_000_000

/** Región por omisión (Puerto Rico, sistema inglés) si la sugerencia no llega. */
export const FALLBACK_SUGGESTION = { dimensionUnit: 'IN' as DimensionUnit, weightUnit: 'LB' as WeightUnit }

export function isDimensionUnit(v: unknown): v is DimensionUnit {
  return typeof v === 'string' && (DIMENSION_UNITS as readonly string[]).includes(v)
}
export function isWeightUnit(v: unknown): v is WeightUnit {
  return typeof v === 'string' && (WEIGHT_UNITS as readonly string[]).includes(v)
}

/** Redondeo a `d` decimales con la mitad lejos de cero (MidpointRounding.AwayFromZero), limpiando el ruido binario. */
export function roundAway(n: number, d: number): number {
  const f = 10 ** d
  const x = Number((Math.abs(n) * f).toPrecision(15))
  return (Math.sign(n) * Math.round(x)) / f
}

/** ¿Están las tres medidas (mayores que cero)? */
export function hasAllThree(length: number | null | undefined, width: number | null | undefined, height: number | null | undefined): boolean {
  return (length ?? 0) > 0 && (width ?? 0) > 0 && (height ?? 0) > 0
}

/** Unidad cúbica de las medidas (IN → IN3…); sin unidad, M3. */
export function cubicUnit(unit: string | null | undefined): string {
  return `${unit || 'M'}3`
}

/** Volumen exacto en la unidad cúbica de las medidas (4 decimales); null si falta alguna. */
export function volumeInUnit(length: number | null, width: number | null, height: number | null): number | null {
  return hasAllThree(length, width, height) ? roundAway(length! * width! * height!, 4) : null
}

/** Volumen en m³ (4 decimales, como Product.VolumeM3) de las tres medidas en su unidad; null si falta alguna. */
export function volumeM3Of(length: number | null, width: number | null, height: number | null, unit: string): number | null {
  if (!hasAllThree(length, width, height) || !isDimensionUnit(unit)) return null
  const m = METERS_PER[unit]
  return roundAway(length! * m * (width! * m) * (height! * m), 4)
}

/** Un volumen escrito en la unidad cúbica de `unit` pasado a m³ (4 decimales: lo que guarda el servidor). */
export function toCubicMeters(volume: number, unit: string): number {
  const m = isDimensionUnit(unit) ? METERS_PER[unit] : 1
  return roundAway(volume * m * m * m, 4)
}

/** Un volumen en m³ mostrado en la unidad cúbica de `unit` (4 decimales). */
export function fromCubicMeters(volumeM3: number, unit: string): number {
  const m = isDimensionUnit(unit) ? METERS_PER[unit] : 1
  return roundAway(volumeM3 / (m * m * m), 4)
}

/** Peso en kg (3 decimales, como Product.WeightKg) de un peso escrito en `unit`. */
export function toKg(weight: number, unit: string): number {
  return roundAway(unit === 'LB' ? weight * KG_PER_POUND : weight, 3)
}

/** El peso guardado en kg mostrado en `unit` (LB con 2 decimales; KG tal cual). */
export function fromKg(weightKg: number, unit: string): number {
  return unit === 'LB' ? roundAway(weightKg / KG_PER_POUND, 2) : weightKg
}

// ---------------------------------------------------------------- formulario

/** Los campos de medidas y peso del formulario del producto. */
export interface MeasuresValues {
  length?: number | null
  width?: number | null
  height?: number | null
  dimensionUnit: string
  /** Volumen escrito a mano, en la unidad cúbica de `dimensionUnit` (solo cuenta si falta alguna medida). */
  volume?: number | null
  weight?: number | null
  weightUnit: string
}

export interface UnitsSuggestionLike {
  dimensionUnit?: string | null
  weightUnit?: string | null
}

/**
 * Valores iniciales: la unidad del producto o la sugerida; el volumen manual = el guardado, en la unidad cúbica de la unidad del formulario (el que
 * mandó el servidor si ya está en esa unidad, si no los m³ convertidos); el peso en su unidad (un peso viejo sin unidad está en kg).
 */
export function measuresDefaults(detail: ProductDetailDto | null, suggestion: UnitsSuggestionLike | null | undefined): Required<MeasuresValues> {
  const m = detail?.measures
  const sDim = suggestion?.dimensionUnit
  const sWeight = suggestion?.weightUnit
  const pDim = m?.dimensionUnit
  const pWeight = m?.weightUnit
  const suggestedDim = isDimensionUnit(sDim) ? sDim : FALLBACK_SUGGESTION.dimensionUnit
  const suggestedWeight = isWeightUnit(sWeight) ? sWeight : FALLBACK_SUGGESTION.weightUnit
  const dimensionUnit = isDimensionUnit(pDim) ? pDim : suggestedDim
  const storedM3 = detail?.volumeM3 ?? null
  let volume: number | null = null
  if (m?.volume != null && m.volumeUnit === cubicUnit(dimensionUnit)) volume = m.volume
  else if (storedM3 != null) volume = fromCubicMeters(storedM3, dimensionUnit)
  const weightUnit = isWeightUnit(pWeight) ? pWeight : detail?.weightKg != null ? 'KG' : suggestedWeight
  const weight = m?.weight ?? (detail?.weightKg != null ? fromKg(detail.weightKg, weightUnit) : null)
  return {
    length: m?.length ?? null,
    width: m?.width ?? null,
    height: m?.height ?? null,
    dimensionUnit,
    volume,
    weight,
    weightUnit,
  }
}

/** Lo que muestra el bloque del volumen: calculado (no editable) con las tres medidas, o el manual; con su unidad cúbica y su equivalente en m³. */
export function volumeView(v: Pick<MeasuresValues, 'length' | 'width' | 'height' | 'dimensionUnit' | 'volume'>): {
  calculated: boolean
  volume: number | null
  unit: string
  m3: number | null
} {
  const unit = cubicUnit(v.dimensionUnit)
  const [l, w, h] = [v.length ?? null, v.width ?? null, v.height ?? null]
  if (hasAllThree(l, w, h)) return { calculated: true, volume: volumeInUnit(l, w, h), unit, m3: volumeM3Of(l, w, h, v.dimensionUnit) }
  const volume = v.volume ?? null
  return { calculated: false, volume, unit, m3: volume != null ? toCubicMeters(volume, v.dimensionUnit) : null }
}

/** Alta: las medidas escritas, la unidad (si hay alguna medida o volumen), el volumen manual en m³ (solo sin las tres) y el peso con su unidad. */
export function measuresCreateBody(v: MeasuresValues): Pick<ProductCreateRequest, 'length' | 'width' | 'height' | 'dimensionUnit' | 'volumeM3' | 'weight' | 'weightUnit'> {
  const all = hasAllThree(v.length, v.width, v.height)
  const anyDim = v.length != null || v.width != null || v.height != null
  const volumeM3 = !all && v.volume != null ? toCubicMeters(v.volume, v.dimensionUnit) : null
  return {
    length: v.length ?? null,
    width: v.width ?? null,
    height: v.height ?? null,
    dimensionUnit: anyDim || volumeM3 != null ? v.dimensionUnit : null,
    volumeM3,
    weight: v.weight ?? null,
    weightUnit: v.weight != null ? v.weightUnit : null,
  }
}

type Dirty = Partial<Record<keyof MeasuresValues, unknown>>

/**
 * Edición (PATCH: null = sin cambio): solo lo que se tocó. Una medida vaciada va 0 (= quitarla); el volumen manual va en m³ solo si se tocó o cambió la
 * unidad en que está escrito, y nunca con las tres medidas; la unidad viaja si cambió algo de las medidas; el peso vaciado va 0, y peso y unidad viajan
 * juntos tal como se ven (cambiar solo la unidad no convierte el número escrito).
 */
export function measuresPatchBody(
  v: MeasuresValues,
  dirty: Dirty,
): Pick<ProductPatchRequest, 'length' | 'width' | 'height' | 'dimensionUnit' | 'volumeM3' | 'weight' | 'weightUnit'> {
  const all = hasAllThree(v.length, v.width, v.height)
  const dim = (k: 'length' | 'width' | 'height') => (dirty[k] ? (v[k] ?? 0) : null)
  const dimsDirty = Boolean(dirty.length || dirty.width || dirty.height)
  const volumeTouched = Boolean(dirty.volume) || (Boolean(dirty.dimensionUnit) && v.volume != null)
  const volumeM3 = !all && volumeTouched ? toCubicMeters(v.volume ?? 0, v.dimensionUnit) : null
  const weightTouched = Boolean(dirty.weight || dirty.weightUnit)
  return {
    length: dim('length'),
    width: dim('width'),
    height: dim('height'),
    dimensionUnit: dirty.dimensionUnit || dimsDirty || volumeM3 != null ? v.dimensionUnit : null,
    volumeM3,
    weight: weightTouched ? (v.weight ?? (dirty.weight ? 0 : null)) : null,
    weightUnit: weightTouched ? v.weightUnit : null,
  }
}

// ---------------------------------------------------------------- validación (mensajes exactos del servidor)

/** Largo, ancho o alto: no negativo, hasta 4 decimales y menor que 10⁸ («El largo no puede ser negativo.» / «… debe ser un número de hasta 4 decimales.»). */
export function dimensionSchema(t: Translate, field: 'length' | 'width' | 'height') {
  return z
    .number(t('warehouse.products.errors.numberInvalid'))
    .min(0, t(`warehouse.products.errors.${field}Negative`))
    .refine((n) => decimals(n) <= 4 && n < MAX_DIMENSION, t(`warehouse.products.errors.${field}Invalid`))
    .nullable()
}

/** Peso en su unidad: no negativo y que quepa («El peso no puede ser negativo.» / «El peso es demasiado grande.»). */
export function weightSchema(t: Translate) {
  return z
    .number(t('warehouse.products.errors.numberInvalid'))
    .min(0, t('warehouse.products.errors.weightNegative'))
    .refine((n) => n < MAX_WEIGHT, t('warehouse.products.errors.weightTooLarge'))
    .nullable()
}

/** Volumen manual en la unidad cúbica: no negativo y hasta 4 decimales (el tope en m³ se revisa con la unidad, en `measuresIssues`). */
export function manualVolumeSchema(t: Translate) {
  return z
    .number(t('warehouse.products.errors.numberInvalid'))
    .min(0, t('warehouse.products.errors.measuresNegative'))
    .refine((n) => decimals(n) <= 4, t('warehouse.products.errors.volumeInvalid'))
    .nullable()
}

/** Reglas que dependen de la unidad: el volumen calculado o el manual (en m³) deben caber en la columna. */
export function measuresIssues(v: MeasuresValues, t: Translate): { path: keyof MeasuresValues; message: string }[] {
  const out: { path: keyof MeasuresValues; message: string }[] = []
  if (hasAllThree(v.length, v.width, v.height)) {
    const m3 = volumeM3Of(v.length ?? null, v.width ?? null, v.height ?? null, v.dimensionUnit)
    if (m3 != null && m3 >= MAX_VOLUME_M3) out.push({ path: 'volume', message: t('warehouse.products.errors.volumeTooLarge') })
  } else if (v.volume != null && toCubicMeters(v.volume, v.dimensionUnit) >= MAX_VOLUME_M3) {
    out.push({ path: 'volume', message: t('warehouse.products.errors.volumeInvalid') })
  }
  if (v.weight != null && toKg(v.weight, v.weightUnit) >= MAX_WEIGHT) out.push({ path: 'weight', message: t('warehouse.products.errors.weightTooLarge') })
  return out
}
