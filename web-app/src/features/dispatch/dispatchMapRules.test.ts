// Lote F-E2b — reglas puras del plano, el arrastre y el pin manual de la Sala de despacho.
import { describe, expect, it } from 'vitest'
import { translate } from '../../kernel/i18n'
import enDict from '../../kernel/i18n/en.json'
import esDict from '../../kernel/i18n/es.json'
import {
  buildPinRequest,
  coordText,
  firstApproximateStop,
  mapStops,
  parseCoordinate,
  pinSchema,
  pinValuesOf,
  reorderIds,
  rowIndexAt,
  stopHasPoint,
} from './dispatchRules'

describe('arrastrar para reordenar', () => {
  it('reorderIds mueve el elemento a la nueva posición y corre los demás (hacia arriba, hacia abajo, al extremo)', () => {
    expect(reorderIds([1, 2, 3, 4], 3, 0)).toEqual([4, 1, 2, 3])
    expect(reorderIds([1, 2, 3, 4], 0, 2)).toEqual([2, 3, 1, 4])
    expect(reorderIds([1, 2, 3, 4], 1, 3)).toEqual([1, 3, 4, 2])
  })

  it('no hay cambio si se suelta donde estaba o fuera de rango, y el arreglo original no se toca', () => {
    const ids = [1, 2, 3]
    expect(reorderIds(ids, 1, 1)).toBeNull()
    expect(reorderIds(ids, -1, 0)).toBeNull()
    expect(reorderIds(ids, 0, 3)).toBeNull()
    reorderIds(ids, 0, 2)
    expect(ids).toEqual([1, 2, 3])
  })

  it('rowIndexAt elige la fila bajo el puntero: sobre la primera, entre filas, y pasado el final la última', () => {
    const bottoms = [40, 80, 120] // tres filas de 40 px
    expect(rowIndexAt(bottoms, -50)).toBe(0)
    expect(rowIndexAt(bottoms, 10)).toBe(0)
    expect(rowIndexAt(bottoms, 40)).toBe(0)
    expect(rowIndexAt(bottoms, 41)).toBe(1)
    expect(rowIndexAt(bottoms, 119)).toBe(2)
    expect(rowIndexAt(bottoms, 900)).toBe(2)
    expect(rowIndexAt([], 10)).toBe(-1)
  })
})

describe('pin manual: validación con los textos del servidor', () => {
  const es = (key: string) => translate('es', key)
  const issues = (lat: string, lng: string) => {
    const r = pinSchema(es).safeParse({ lat, lng })
    return r.success ? [] : r.error.issues.map((i) => [i.path.join('.'), i.message])
  }

  it('parseCoordinate acepta punto o coma decimal, signo y espacios; rechaza lo que no es un número escrito con cifras', () => {
    expect(parseCoordinate('18.4655')).toBe(18.4655)
    expect(parseCoordinate(' 18,4655 ')).toBe(18.4655)
    expect(parseCoordinate('-66.1057')).toBe(-66.1057)
    expect(parseCoordinate('+5')).toBe(5)
    expect(parseCoordinate('.5')).toBe(0.5)
    expect(parseCoordinate('0')).toBe(0)
    for (const bad of ['', 'abc', '18.4.5', '18,4,5', '1e3', '18°', '--5', 'Infinity', 'NaN']) expect(parseCoordinate(bad)).toBeNull()
  })

  it('valores válidos, con coma o punto y en los extremos, no dan error', () => {
    expect(issues('18.4655', '-66.1057')).toEqual([])
    expect(issues('18,4655', '-66,1057')).toEqual([])
    expect(issues('-90', '180')).toEqual([])
    expect(issues('0', '0')).toEqual([])
  })

  it('vacío: «Indique la latitud y la longitud.» en cada campo que falta', () => {
    expect(issues('', '')).toEqual([['lat', 'Indique la latitud y la longitud.'], ['lng', 'Indique la latitud y la longitud.']])
    expect(issues('18.4', '  ')).toEqual([['lng', 'Indique la latitud y la longitud.']])
  })

  it('fuera de rango: los mismos textos del servidor', () => {
    expect(issues('90.0001', '0')).toEqual([['lat', 'La latitud debe estar entre -90 y 90.']])
    expect(issues('-91', '0')).toEqual([['lat', 'La latitud debe estar entre -90 y 90.']])
    expect(issues('0', '180.5')).toEqual([['lng', 'La longitud debe estar entre -180 y 180.']])
    expect(issues('95', '-200')).toEqual([['lat', 'La latitud debe estar entre -90 y 90.'], ['lng', 'La longitud debe estar entre -180 y 180.']])
  })

  it('texto que no es número: pide un número', () => {
    expect(issues('abc', '-66.1')).toEqual([['lat', 'La latitud debe ser un número, por ejemplo 18.4655.']])
    expect(issues('18.4', 'x')).toEqual([['lng', 'La longitud debe ser un número, por ejemplo -66.1057.']])
  })

  it('el request lleva números (coma a punto) y la versión vigente; las coordenadas tomadas del plano se redondean a 5 decimales', () => {
    expect(buildPinRequest({ lat: '18,4655', lng: '-66.1057' }, 'rv-9')).toEqual({ lat: 18.4655, lng: -66.1057, rowVersion: 'rv-9' })
    expect(buildPinRequest({ lat: '1', lng: '2' }, undefined)).toEqual({ lat: 1, lng: 2, rowVersion: null })
    expect(coordText(18.4655123456)).toBe('18.46551')
    expect(coordText(-66.1)).toBe('-66.1')
  })

  it('los valores de partida salen del pin actual de la parada (vacíos si no tiene)', () => {
    expect(pinValuesOf({ point: { lat: 18.4, lng: -66.1 } })).toEqual({ lat: '18.4', lng: '-66.1' })
    expect(pinValuesOf({ point: null })).toEqual({ lat: '', lng: '' })
    expect(pinValuesOf(null)).toEqual({ lat: '', lng: '' })
  })
})

describe('paradas para el plano', () => {
  const t2 = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
  const base = { orderNumber: 'OT-1', consigneeName: 'Farmacia Sol', city: 'Bayamón', statusCode: 'PENDING', isApproximate: true }

  it('numera con la secuencia mostrada, arma «Parada 3 · Consignatario · pueblo» y traduce precisión y estado', () => {
    const out = mapStops(
      [
        { ...base, id: 7, point: { lat: 18.4, lng: -66.1 }, geocodeAccuracyCode: 'EXACT', isApproximate: false },
        { ...base, id: 8, orderNumber: 'OT-2', consigneeName: null, city: 'Caguas', point: { lat: 18.2, lng: -66 }, geocodeAccuracyCode: 'ZIP_CENTROID' },
        { ...base, id: 9, orderNumber: 'OT-3', point: { lat: 18.3, lng: -66.2 }, geocodeAccuracyCode: 'MANUAL', statusCode: 'COMPLETED', isApproximate: false },
      ],
      t2,
    )
    expect(out.map((s) => s.seq)).toEqual([1, 2, 3])
    expect(out[0].label).toBe('Parada 1 · Farmacia Sol · Bayamón')
    expect(out[1].label).toBe('Parada 2 · OT-2 · Caguas') // sin consignatario: el número de la orden
    expect(out.map((s) => s.accuracy)).toEqual(['exact', 'approximate', 'manual'])
    expect(out.map((s) => s.state)).toEqual(['pending', 'pending', 'done'])
    expect(out[0].short).toBe('OT-1')
  })

  it('una parada sin punto sigue en la lista (el plano la pone aparte)', () => {
    const [s] = mapStops([{ ...base, id: 1 }], t2)
    expect(s.point).toBeNull()
    expect(stopHasPoint({ point: undefined })).toBe(false)
    expect(stopHasPoint({ point: { lat: 1, lng: 2 } })).toBe(true)
  })

  it('la primera parada aproximada es la primera que el servidor marca isApproximate (sin pin o centroide)', () => {
    const stops = [
      { id: 1, isApproximate: false },
      { id: 2, isApproximate: true },
      { id: 3, isApproximate: true },
    ]
    expect(firstApproximateStop(stops)?.id).toBe(2)
    expect(firstApproximateStop([{ id: 1, isApproximate: false }])).toBeUndefined()
  })
})

describe('textos del plano y del pin', () => {
  function keysOf(obj: unknown, prefix = ''): string[] {
    if (obj === null || typeof obj !== 'object') return [prefix]
    return Object.entries(obj as Record<string, unknown>).flatMap(([k, v]) => keysOf(v, prefix ? `${prefix}.${k}` : k))
  }

  it('routeMap.* y dispatch.pin.* / dispatch.map.* tienen las mismas claves en español e inglés', () => {
    expect(keysOf(enDict.routeMap).sort()).toEqual(keysOf(esDict.routeMap).sort())
    expect(keysOf(enDict.dispatch.pin).sort()).toEqual(keysOf(esDict.dispatch.pin).sort())
    expect(keysOf(enDict.dispatch.map).sort()).toEqual(keysOf(esDict.dispatch.map).sort())
  })

  it('los mensajes de rango del pin son literalmente los del servidor (manual 05 §4.3)', () => {
    expect(esDict.dispatch.pin.required).toBe('Indique la latitud y la longitud.')
    expect(esDict.dispatch.pin.latRange).toBe('La latitud debe estar entre -90 y 90.')
    expect(esDict.dispatch.pin.lngRange).toBe('La longitud debe estar entre -180 y 180.')
  })
})
