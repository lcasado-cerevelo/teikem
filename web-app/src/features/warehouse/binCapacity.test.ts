// Lote 11 — lógica pura del cupo en bloque ("Asignar cupo"): consulta de la vista previa, cuerpo del POST (exactamente una
// acción, allBins solo sin filtros), conteo por zonas, validación del cupo y cuándo se confirma.
import { describe, expect, it } from 'vitest'
import type { WarehouseZoneDto } from './api'
import {
  BIN_CAPACITY_CONFIRM_THRESHOLD,
  EMPTY_BIN_CAPACITY_SCOPE,
  capacityPreviewQuery,
  capacityRequestBody,
  hasScopeFilter,
  needsCapacityConfirmation,
  parseCapacityQty,
  sanitizeZoneIds,
  scopeReady,
  sendsAllBins,
  zonesWithoutCapacity,
  type BinCapacityScope,
} from './binCapacity'

const scope = (s: Partial<BinCapacityScope>): BinCapacityScope => ({ ...EMPTY_BIN_CAPACITY_SCOPE, ...s })
const ZONES: WarehouseZoneDto[] = [
  { id: 1, code: 'A', binsWithoutCapacity: 4 },
  { id: 2, code: 'B', binsWithoutCapacity: 0 },
  { id: 3, code: 'C', binsWithoutCapacity: 7 },
]

describe('alcance', () => {
  it('los textos en blanco no cuentan como filtro; zona o texto sí', () => {
    expect(hasScopeFilter(scope({ aisle: '   ' }))).toBe(false)
    expect(hasScopeFilter(scope({ zoneIds: ['1'] }))).toBe(true)
    expect(hasScopeFilter(scope({ level: ' 2 ' }))).toBe(true)
  })

  it('sin filtros solo es aplicable con "Todo el almacén"; con filtros, allBins se ignora', () => {
    expect(scopeReady(scope({}))).toBe(false)
    expect(scopeReady(scope({ allBins: true }))).toBe(true)
    expect(sendsAllBins(scope({ allBins: true }))).toBe(true)
    expect(sendsAllBins(scope({ allBins: true, rack: 'R1' }))).toBe(false)
  })
})

describe('vista previa y cuerpo del POST', () => {
  it('la vista previa usa los mismos filtros recortados, solo activas y take=1', () => {
    expect(capacityPreviewQuery(scope({ zoneIds: ['1', '3'], aisle: ' a ', position: '' }))).toEqual({
      includeInactive: false,
      zoneIds: [1, 3],
      aisle: 'a',
      take: 1,
    })
    expect(capacityPreviewQuery(scope({ allBins: true }))).toEqual({ includeInactive: false, take: 1 })
  })

  it('fijar cupo manda maxCapacityQty y no clear; quitar manda clear y no maxCapacityQty', () => {
    const s = scope({ zoneIds: ['2'], rack: 'R1', onlyWithoutCapacity: true })
    const setBody = capacityRequestBody(s, { kind: 'set', qty: 40 })
    expect(setBody).toEqual({ zoneIds: [2], rack: 'R1', includeInactive: false, onlyWithoutCapacity: true, allBins: false, maxCapacityQty: 40 })
    expect(setBody).not.toHaveProperty('clear')
    const clearBody = capacityRequestBody(s, { kind: 'clear' })
    expect(clearBody.clear).toBe(true)
    expect(clearBody).not.toHaveProperty('maxCapacityQty')
  })

  it('allBins: true solo sin filtros', () => {
    expect(capacityRequestBody(scope({ allBins: true }), { kind: 'set', qty: 5 })).toMatchObject({ allBins: true, maxCapacityQty: 5 })
    expect(capacityRequestBody(scope({ allBins: true, zoneIds: ['1'] }), { kind: 'set', qty: 5 }).allBins).toBe(false)
  })
})

describe('conteo por zonas y zonas válidas', () => {
  it('Σ binsWithoutCapacity de las elegidas; sin zonas elegidas, de todas', () => {
    expect(zonesWithoutCapacity(ZONES, ['1', '2'])).toBe(4)
    expect(zonesWithoutCapacity(ZONES, [])).toBe(11)
  })

  it('descarta ids que no son zonas de la lista', () => {
    expect(sanitizeZoneIds(['1', '9', '3'], ZONES)).toEqual(['1', '3'])
  })
})

describe('cupo máximo escrito', () => {
  it('obligatorio, entero, mayor que cero y dentro de INT', () => {
    expect(parseCapacityQty('')).toEqual({ ok: false, code: 'required' })
    expect(parseCapacityQty('2.5')).toEqual({ ok: false, code: 'integer' })
    expect(parseCapacityQty('abc')).toEqual({ ok: false, code: 'integer' })
    expect(parseCapacityQty('0')).toEqual({ ok: false, code: 'positive' })
    expect(parseCapacityQty('-3')).toEqual({ ok: false, code: 'positive' })
    expect(parseCapacityQty('3000000000')).toEqual({ ok: false, code: 'tooLarge' })
    expect(parseCapacityQty(' 40 ')).toEqual({ ok: true, value: 40 })
  })
})

describe('confirmación', () => {
  it('más de 100 posiciones o "Todo el almacén"', () => {
    const filtered = scope({ zoneIds: ['1'] })
    expect(needsCapacityConfirmation(BIN_CAPACITY_CONFIRM_THRESHOLD, filtered)).toBe(false)
    expect(needsCapacityConfirmation(BIN_CAPACITY_CONFIRM_THRESHOLD + 1, filtered)).toBe(true)
    expect(needsCapacityConfirmation(3, scope({ allBins: true }))).toBe(true)
  })
})
