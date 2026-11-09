// Lote 230 — reglas puras de medidas y peso del producto. Los vectores numéricos son los de ProductDimensionRulesTests (xunit): el cálculo local
// que se muestra antes de guardar redondea igual que el servidor.
import { describe, expect, it } from 'vitest'
import type { components } from '../../kernel/api/schema'
import {
  cubicUnit,
  fromCubicMeters,
  fromKg,
  hasAllThree,
  measuresCreateBody,
  measuresDefaults,
  measuresIssues,
  measuresPatchBody,
  roundAway,
  toCubicMeters,
  toKg,
  volumeInUnit,
  volumeM3Of,
  volumeView,
  type MeasuresValues,
} from './productMeasures'

type ProductDetailDto = components['schemas']['ProductDetailDto']
const t = (k: string) => k
const base: MeasuresValues = { length: null, width: null, height: null, dimensionUnit: 'IN', volume: null, weight: null, weightUnit: 'LB' }

describe('cálculo (mismos casos que el servidor)', () => {
  it.each([
    ['IN', 10, 10, 10, 0.0164], // 1000 in³ = 0.016387 m³
    ['CM', 100, 50, 20, 0.1],
    ['FT', 2, 3, 4, 0.6796], // 24 ft³
    ['M', 1.2, 1, 0.5, 0.6],
    ['FT', 1, 10, 10, 2.8317], // 100 ft³
  ])('%s %s × %s × %s = %s m³', (unit, l, w, h, m3) => {
    expect(volumeM3Of(l, w, h, unit)).toBe(m3)
  })

  it('sin las tres medidas (o con un 0) no hay cálculo', () => {
    expect(hasAllThree(10, 10, null)).toBe(false)
    expect(hasAllThree(10, 0, 10)).toBe(false)
    expect(volumeM3Of(10, null, 10, 'IN')).toBeNull()
    expect(volumeInUnit(10, 10, null)).toBeNull()
  })

  it('volumen en la unidad cúbica de las medidas', () => {
    expect(volumeInUnit(4.2, 3.6, 2)).toBe(30.24)
    expect(cubicUnit('IN')).toBe('IN3')
    expect(cubicUnit(null)).toBe('M3')
    expect(roundAway(fromCubicMeters(0.6796, 'FT'), 1)).toBe(24)
    expect(toCubicMeters(24, 'FT')).toBe(0.6796)
    expect(toCubicMeters(0.3, 'M')).toBe(0.3)
  })

  it.each([
    [2.5, 'LB', 1.134],
    [1, 'LB', 0.454],
    [1.25, 'KG', 1.25],
  ])('peso %s %s = %s kg', (w, unit, kg) => {
    expect(toKg(w, unit)).toBe(kg)
  })

  it.each([2.5, 0.1, 12.34, 1500.99])('la vuelta kg → lb recupera %s lb', (lb) => {
    expect(fromKg(toKg(lb, 'LB'), 'LB')).toBe(lb)
  })

  it('redondeo con la mitad lejos de cero', () => {
    expect(roundAway(0.00005, 4)).toBe(0.0001)
    expect(roundAway(-0.00005, 4)).toBe(-0.0001)
    expect(roundAway(1.0049999999999999, 3)).toBe(1.005)
  })
})

describe('volumeView', () => {
  it('con las tres medidas: calculado (no editable) en la unidad cúbica y su equivalente en m³', () => {
    expect(volumeView({ ...base, length: 10, width: 10, height: 10, volume: 5 })).toEqual({ calculated: true, volume: 1000, unit: 'IN3', m3: 0.0164 })
  })
  it('sin las tres: el manual en la unidad cúbica, con sus m³', () => {
    expect(volumeView({ ...base, dimensionUnit: 'FT', length: 2, volume: 24 })).toEqual({ calculated: false, volume: 24, unit: 'FT3', m3: 0.6796 })
    expect(volumeView(base)).toEqual({ calculated: false, volume: null, unit: 'IN3', m3: null })
  })
})

describe('measuresDefaults', () => {
  const detail = (over: Partial<ProductDetailDto>): ProductDetailDto => ({ product: { publicId: 'p' }, ...over })

  it('producto nuevo: las unidades sugeridas (o las de Puerto Rico si no llegan)', () => {
    expect(measuresDefaults(null, { dimensionUnit: 'CM', weightUnit: 'KG' })).toEqual({ ...base, dimensionUnit: 'CM', weightUnit: 'KG' })
    expect(measuresDefaults(null, undefined)).toEqual(base)
  })

  it('con unidad propia: esa, y el volumen y el peso que manda el servidor', () => {
    const d = detail({
      volumeM3: 0.0164,
      weightKg: 1.134,
      measures: { length: 10, width: 10, height: 10, dimensionUnit: 'IN', volume: 1000, volumeUnit: 'IN3', volumeCalculated: true, weight: 2.5, weightUnit: 'LB' },
    })
    expect(measuresDefaults(d, { dimensionUnit: 'CM', weightUnit: 'KG' })).toEqual({ length: 10, width: 10, height: 10, dimensionUnit: 'IN', volume: 1000, weight: 2.5, weightUnit: 'LB' })
  })

  it('volumen viejo en m³ sin unidad: convertido a la unidad sugerida; peso viejo sin unidad: en kg', () => {
    const d = detail({ volumeM3: 0.6796, weightKg: 3, measures: { volume: 0.6796, volumeUnit: 'M3', volumeCalculated: false, weight: 3, weightUnit: null } })
    const v = measuresDefaults(d, { dimensionUnit: 'FT', weightUnit: 'LB' })
    expect(v.dimensionUnit).toBe('FT')
    expect(roundAway(v.volume!, 1)).toBe(24)
    expect([v.weight, v.weightUnit]).toEqual([3, 'KG'])
  })
})

describe('cuerpo del alta y de la edición', () => {
  it('alta con las tres medidas: no manda volumen; peso con su unidad', () => {
    expect(measuresCreateBody({ ...base, length: 10, width: 10, height: 10, volume: 7, weight: 2.5 })).toEqual({
      length: 10, width: 10, height: 10, dimensionUnit: 'IN', volumeM3: null, weight: 2.5, weightUnit: 'LB',
    })
  })
  it('alta sin las tres: el volumen manual en m³; sin nada, sin unidad', () => {
    expect(measuresCreateBody({ ...base, dimensionUnit: 'FT', volume: 24 })).toMatchObject({ dimensionUnit: 'FT', volumeM3: 0.6796 })
    expect(measuresCreateBody(base)).toEqual({ length: null, width: null, height: null, dimensionUnit: null, volumeM3: null, weight: null, weightUnit: null })
  })

  it('edición sin tocar nada: todo null (sin cambio)', () => {
    expect(measuresPatchBody({ ...base, length: 10, volume: 5, weight: 2 }, {})).toEqual({
      length: null, width: null, height: null, dimensionUnit: null, volumeM3: null, weight: null, weightUnit: null,
    })
  })
  it('edición: medida vaciada = 0 (quitarla) con la unidad; el volumen guardado no se manda si no se tocó', () => {
    expect(measuresPatchBody({ ...base, length: null, width: 4, volume: 5 }, { length: true })).toEqual({
      length: 0, width: null, height: null, dimensionUnit: 'IN', volumeM3: null, weight: null, weightUnit: null,
    })
  })
  it('edición: volumen manual tocado → m³; con las tres medidas nunca se manda', () => {
    expect(measuresPatchBody({ ...base, dimensionUnit: 'M', volume: 0.3 }, { volume: true })).toMatchObject({ volumeM3: 0.3, dimensionUnit: 'M' })
    expect(measuresPatchBody({ ...base, length: 1, width: 1, height: 1, volume: 0.3 }, { volume: true, height: true })).toMatchObject({ volumeM3: null, height: 1 })
  })
  it('edición: cambiar la unidad manda la unidad y el volumen manual tal como se ve', () => {
    expect(measuresPatchBody({ ...base, dimensionUnit: 'FT', volume: 24 }, { dimensionUnit: true })).toMatchObject({ dimensionUnit: 'FT', volumeM3: 0.6796 })
  })
  it('edición: peso vaciado = 0; cambiar la unidad manda peso y unidad juntos', () => {
    expect(measuresPatchBody({ ...base, weight: null }, { weight: true })).toMatchObject({ weight: 0, weightUnit: 'LB' })
    expect(measuresPatchBody({ ...base, weight: 2, weightUnit: 'KG' }, { weightUnit: true })).toMatchObject({ weight: 2, weightUnit: 'KG' })
  })
})

describe('measuresIssues', () => {
  it('volumen calculado demasiado grande', () => {
    expect(measuresIssues({ ...base, dimensionUnit: 'M', length: 99_999, width: 99_999, height: 99_999 }, t)).toEqual([
      { path: 'volume', message: 'warehouse.products.errors.volumeTooLarge' },
    ])
  })
  it('volumen manual que en m³ no cabe; peso que en kg no cabe', () => {
    expect(measuresIssues({ ...base, dimensionUnit: 'M', volume: 100_000_000 }, t)).toEqual([{ path: 'volume', message: 'warehouse.products.errors.volumeInvalid' }])
    expect(measuresIssues({ ...base, weight: 1_000_000_000, weightUnit: 'KG' }, t)).toEqual([{ path: 'weight', message: 'warehouse.products.errors.weightTooLarge' }])
    expect(measuresIssues({ ...base, length: 10, width: 10, height: 10 }, t)).toEqual([])
  })
})
