import { describe, expect, it } from 'vitest'
import { clampSplitRatio, ratioFromPointer, readSplitRatio, splitBounds, splitStorageKey } from './splitRatio'

describe('splitRatio (lógica pura de SplitPane)', () => {
  it('readSplitRatio: un número entre 0 y 1; vacío, texto o fuera de rango → el por defecto', () => {
    expect(readSplitRatio('0.45', 0.6)).toBe(0.45)
    expect(readSplitRatio(' 0.7 ', 0.6)).toBe(0.7)
    expect(readSplitRatio(null, 0.6)).toBe(0.6)
    expect(readSplitRatio(undefined)).toBe(0.6)
    expect(readSplitRatio('', 0.5)).toBe(0.5)
    expect(readSplitRatio('abc', 0.6)).toBe(0.6)
    expect(readSplitRatio('NaN', 0.6)).toBe(0.6)
    expect(readSplitRatio('Infinity', 0.6)).toBe(0.6)
    expect(readSplitRatio('0', 0.6)).toBe(0.6)
    expect(readSplitRatio('1', 0.6)).toBe(0.6)
    expect(readSplitRatio('-0.2', 0.6)).toBe(0.6)
    expect(readSplitRatio('{"r":0.5}', 0.6)).toBe(0.6)
  })

  it('clampSplitRatio: sin ancho medido solo cuentan minRatio/maxRatio (0.35 / 0.75)', () => {
    expect(clampSplitRatio(0.6, 0)).toBe(0.6)
    expect(clampSplitRatio(0.1, 0)).toBe(0.35)
    expect(clampSplitRatio(0.95, 0)).toBe(0.75)
    expect(clampSplitRatio(Number.NaN, 0)).toBe(0.35)
    expect(clampSplitRatio(0.5, 0, [420, 320], 0.4, 0.5)).toBe(0.5)
  })

  it('clampSplitRatio: con ancho, cada panel conserva su mínimo en px (A 420, B 320)', () => {
    // 1000 px útiles: A ≥ 0.42, B ≥ 320 → A ≤ 0.68
    const [lo, hi] = splitBounds(1000)
    expect(lo).toBeCloseTo(0.42)
    expect(hi).toBeCloseTo(0.68)
    expect(clampSplitRatio(0.35, 1000)).toBeCloseTo(0.42)
    expect(clampSplitRatio(0.75, 1000)).toBeCloseTo(0.68)
    expect(clampSplitRatio(0.6, 1000)).toBe(0.6)
    // muy ancho: mandan minRatio/maxRatio
    expect(splitBounds(3000)).toEqual([0.35, 0.75])
  })

  it('clampSplitRatio: si el ancho no alcanza para los dos mínimos, la barra queda fija en el reparto de los mínimos', () => {
    const [lo, hi] = splitBounds(600)
    expect(lo).toBe(hi)
    expect(lo).toBeCloseTo(420 / 740)
    expect(clampSplitRatio(0.7, 600)).toBeCloseTo(420 / 740)
  })

  it('ratioFromPointer: el centro de la barra (10 px) bajo el puntero', () => {
    expect(ratioFromPointer(605, 1010)).toBeCloseTo(0.6)
    expect(ratioFromPointer(5, 1010)).toBe(0)
    expect(ratioFromPointer(100, 0)).toBe(0.6)
  })

  it('splitStorageKey: teikem.split.<clave>', () => {
    expect(splitStorageKey('pickBatches')).toBe('teikem.split.pickBatches')
  })
})
