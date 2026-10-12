// Lote F-E2b — Reglas puras del plano de ruta esquemático: proyección y ajuste al rectángulo, inversa, pines encimados, escala y claves de estado.
import { describe, expect, it } from 'vitest'
import { accuracyOf, fitProjection, isGeoPoint, niceScale, scaleParts, spreadOverlaps, stateOf } from './routeMapRules'

describe('claves de precisión y de estado', () => {
  it('MANUAL y EXACT se reconocen; todo lo demás (centroides, sin dato) es aproximado', () => {
    expect(accuracyOf('MANUAL')).toBe('manual')
    expect(accuracyOf('manual')).toBe('manual')
    expect(accuracyOf('EXACT')).toBe('exact')
    expect(accuracyOf('ZIP_CENTROID')).toBe('approximate')
    expect(accuracyOf('CITY_CENTROID')).toBe('approximate')
    expect(accuracyOf(null)).toBe('approximate')
    expect(accuracyOf(undefined)).toBe('approximate')
  })

  it('el estatus de la parada del servidor → estado del pin; PENDING y lo desconocido son pendiente', () => {
    expect(stateOf('PENDING')).toBe('pending')
    expect(stateOf('COMPLETED')).toBe('done')
    expect(stateOf('DELIVERED')).toBe('done')
    expect(stateOf('FAILED')).toBe('failed')
    expect(stateOf('ON_THE_WAY')).toBe('current')
    expect(stateOf('ARRIVED')).toBe('current')
    expect(stateOf('algo-nuevo')).toBe('pending')
    expect(stateOf(null)).toBe('pending')
  })

  it('un punto es dibujable solo con números finitos dentro de los rangos', () => {
    expect(isGeoPoint({ lat: 18.4, lng: -66.1 })).toBe(true)
    expect(isGeoPoint({ lat: 0, lng: 0 })).toBe(true)
    expect(isGeoPoint({ lat: 91, lng: 0 })).toBe(false)
    expect(isGeoPoint({ lat: 0, lng: -181 })).toBe(false)
    expect(isGeoPoint({ lat: Number.NaN, lng: 0 })).toBe(false)
    expect(isGeoPoint({ lat: 1 })).toBe(false)
    expect(isGeoPoint(null)).toBe(false)
    expect(isGeoPoint(undefined)).toBe(false)
  })
})

describe('proyección ajustada al rectángulo', () => {
  const pts = [
    { lat: 18.5, lng: -66.2 },
    { lat: 18.3, lng: -66.0 },
    { lat: 18.4, lng: -66.1 },
  ]

  it('sin puntos o sin tamaño no hay proyección', () => {
    expect(fitProjection([], 600, 300)).toBeNull()
    expect(fitProjection(pts, 0, 300)).toBeNull()
  })

  it('todos los puntos caen dentro del plano con el margen y el norte queda arriba y el oeste a la izquierda', () => {
    const p = fitProjection(pts, 600, 300, 28)!
    for (const pt of pts) {
      const { x, y } = p.project(pt)
      expect(x).toBeGreaterThanOrEqual(28 - 1e-6)
      expect(x).toBeLessThanOrEqual(600 - 28 + 1e-6)
      expect(y).toBeGreaterThanOrEqual(28 - 1e-6)
      expect(y).toBeLessThanOrEqual(300 - 28 + 1e-6)
    }
    expect(p.project(pts[0]).y).toBeLessThan(p.project(pts[1]).y) // más al norte = más arriba
    expect(p.project(pts[0]).x).toBeLessThan(p.project(pts[1]).x) // más al oeste = más a la izquierda
  })

  it('toca el margen por el lado que limita y centra el otro (el rectángulo se ajusta sin deformar)', () => {
    // 0.2° de latitud por 0.2° de longitud: en un plano ancho limita el alto
    const p = fitProjection(pts, 600, 300, 28)!
    const ys = pts.map((q) => p.project(q).y)
    expect(Math.min(...ys)).toBeCloseTo(28, 5)
    expect(Math.max(...ys)).toBeCloseTo(300 - 28, 5)
    const xs = pts.map((q) => p.project(q).x)
    expect((Math.min(...xs) + Math.max(...xs)) / 2).toBeCloseTo(300, 5) // centrado en horizontal
  })

  it('corrige la distorsión en longitud con cos(latitud media): un grado de longitud mide menos que uno de latitud', () => {
    const north = [
      { lat: 60, lng: 0 },
      { lat: 61, lng: 1 },
    ]
    const p = fitProjection(north, 400, 400, 0)!
    const a = p.project(north[0])
    const b = p.project(north[1])
    // a 60.5° de latitud cos ≈ 0.4924: el ancho es ≈ la mitad del alto
    expect((b.x - a.x) / (a.y - b.y)).toBeCloseTo(Math.cos((60.5 * Math.PI) / 180), 3)
  })

  it('un solo punto queda en el centro y no se amplía sin fin', () => {
    const p = fitProjection([{ lat: 18.4, lng: -66.1 }], 600, 300)!
    const { x, y } = p.project({ lat: 18.4, lng: -66.1 })
    expect(x).toBeCloseTo(300, 5)
    expect(y).toBeCloseTo(150, 5)
    // alcance mínimo de 0.01° (≈ 1.1 km) en el lado limitante
    expect(p.metersPerPx).toBeGreaterThan(1)
  })

  it('unproject es la inversa exacta de project', () => {
    const p = fitProjection(pts, 520, 300)!
    for (const pt of pts) {
      const { x, y } = p.project(pt)
      const back = p.unproject(x, y)
      expect(back.lat).toBeCloseTo(pt.lat, 9)
      expect(back.lng).toBeCloseTo(pt.lng, 9)
    }
    // un píxel cualquiera vuelve a su píxel
    const g = p.unproject(123, 77)
    const again = p.project(g)
    expect(again.x).toBeCloseTo(123, 6)
    expect(again.y).toBeCloseTo(77, 6)
  })

  it('un plano muy angosto (360 px) sigue conteniendo todos los puntos', () => {
    const p = fitProjection(pts, 300, 300, 28)!
    for (const pt of pts) {
      const { x } = p.project(pt)
      expect(x).toBeGreaterThanOrEqual(28 - 1e-6)
      expect(x).toBeLessThanOrEqual(300 - 28 + 1e-6)
    }
  })
})

describe('pines encimados', () => {
  it('un pin solo no se mueve', () => {
    const out = spreadOverlaps([{ id: 'a', x: 50, y: 60 }])
    expect(out.get('a')).toEqual({ x: 50, y: 60 })
  })

  it('pines en el mismo lugar se abren en abanico alrededor del centro: todos distintos y a la distancia mínima', () => {
    const out = spreadOverlaps([
      { id: 'a', x: 100, y: 100 },
      { id: 'b', x: 100, y: 100 },
      { id: 'c', x: 102, y: 101 },
    ])
    const pts = ['a', 'b', 'c'].map((k) => out.get(k)!)
    for (let i = 0; i < pts.length; i++) {
      for (let j = i + 1; j < pts.length; j++) expect(Math.hypot(pts[i].x - pts[j].x, pts[i].y - pts[j].y)).toBeGreaterThan(12)
    }
  })

  it('los pines lejanos no se tocan', () => {
    const out = spreadOverlaps([
      { id: 'a', x: 10, y: 10 },
      { id: 'b', x: 200, y: 100 },
    ])
    expect(out.get('a')).toEqual({ x: 10, y: 10 })
    expect(out.get('b')).toEqual({ x: 200, y: 100 })
  })
})

describe('escala', () => {
  it('toma el mayor valor redondo que cabe', () => {
    const s = niceScale(10, 120) // 10 m por píxel: caben 1200 m → 1000 m (100 px)
    expect(s.meters).toBe(1000)
    expect(s.px).toBeCloseTo(100, 6)
    expect(niceScale(0.2, 100).meters).toBe(20) // caben 20 m
  })

  it('metros o kilómetros', () => {
    expect(scaleParts(500)).toEqual({ value: 500, unit: 'm' })
    expect(scaleParts(2000)).toEqual({ value: 2, unit: 'km' })
  })
})
