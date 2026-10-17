// Pruebas de las reglas puras del Radar (Lote 89).
import { describe, expect, it } from 'vitest'
import { filtersFromParams } from '../orders/orderRules'
import { radarGroupHref, radarOpenHref } from './radarRules'

const OPEN = ['CONFIRMED', 'IN_TRANSIT']

describe('enlaces del Radar', () => {
  it('un pueblo abre Órdenes con el pueblo y solo lo abierto', () => {
    expect(radarGroupHref('TOWN', 'Ponce', OPEN)).toBe('/orders?city=Ponce&statuses=CONFIRMED%2CIN_TRANSIT')
  })
  it('un cliente abre Órdenes con su publicId', () => {
    expect(radarGroupHref('CLIENT', 'abc-1', OPEN)).toBe('/orders?client=abc-1&statuses=CONFIRMED%2CIN_TRANSIT')
  })
  it('el nombre con espacios y tildes se codifica y «Ver todas» lleva los estatus', () => {
    expect(radarGroupHref('TOWN', 'San Germán', [])).toBe('/orders?city=San+Germ%C3%A1n')
    expect(radarOpenHref(OPEN)).toBe('/orders?statuses=CONFIRMED%2CIN_TRANSIT')
    expect(radarOpenHref([])).toBe('/orders')
  })
})

describe('Órdenes recibe los filtros del enlace', () => {
  it('pueblo, cliente y estatus', () => {
    const f = filtersFromParams(new URLSearchParams('city=San+Germ%C3%A1n&client=abc-1&statuses=CONFIRMED%2CIN_TRANSIT'))
    expect(f.cities).toEqual(['San Germán'])
    expect(f.clientIds).toEqual(['abc-1'])
    expect(f.statuses).toEqual(['CONFIRMED', 'IN_TRANSIT'])
  })
  it('sin parámetros, sin filtros', () => {
    const f = filtersFromParams(new URLSearchParams(''))
    expect([f.cities, f.clientIds, f.statuses]).toEqual([[], [], []])
  })
})
