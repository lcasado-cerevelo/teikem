// Lote F-E3 — reglas puras del Monitoreo: edad del ping y «hace N s», insignia EN VIVO, progreso y próxima llegada, rumbo, selección, consulta y
// el interruptor guardado; más la paridad es/en de las claves monitor.* y el mapeo de estados de parada.
import { describe, expect, it, vi } from 'vitest'
import { translate } from '../../kernel/i18n/i18n'
import es from '../../kernel/i18n/es.json'
import en from '../../kernel/i18n/en.json'
import { stateOf } from '../../kernel/ui'
import {
  agoParts,
  agoText,
  buildMonitorQuery,
  compassOf,
  FOLLOW_STORAGE_KEY,
  hasPing,
  isRecentPing,
  liveSummary,
  LIVE_WINDOW_SECONDS,
  nextArrival,
  overallProgress,
  percentOf,
  pickSelected,
  pingAgeSeconds,
  readFollow,
  speedOf,
  tripProgress,
  writeFollow,
  type DriverPing,
  type MonitorTrip,
} from './monitorRules'

const NOW = Date.parse('2026-10-12T15:00:00Z')
const pingAgo = (seconds: number): DriverPing => ({
  point: { lat: 18.4, lng: -66.1 },
  speedKmh: 30,
  headingDeg: 90,
  capturedAtUtc: new Date(NOW - seconds * 1000).toISOString().replace('Z', ''), // como lo manda el API: UTC sin zona
  receivedAtUtc: new Date(NOW - seconds * 1000 + 800).toISOString().replace('Z', ''),
  linkedToTrip: true,
})
const trip = (over: Partial<MonitorTrip> = {}): MonitorTrip => ({ publicId: 't', code: '2026-0001', totalStops: 4, completedStops: 0, failedStops: 0, pendingStops: 4, ...over })
const tES = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
const tEN = (key: string, params?: Record<string, string | number>) => translate('en', key, params)

describe('edad del ping', () => {
  it('lee la marca del API sin zona como UTC y cuenta segundos enteros', () => {
    expect(pingAgeSeconds(pingAgo(12), NOW)).toBe(12)
    expect(pingAgeSeconds(pingAgo(0), NOW)).toBe(0)
  })
  it('un reloj atrasado nunca da una edad negativa', () => {
    expect(pingAgeSeconds(pingAgo(-30), NOW)).toBe(0)
  })
  it('sin ping, sin hora o con una hora ilegible no hay edad', () => {
    expect(pingAgeSeconds(undefined, NOW)).toBeNull()
    expect(pingAgeSeconds(null, NOW)).toBeNull()
    expect(pingAgeSeconds({ speedKmh: 3 }, NOW)).toBeNull()
    expect(pingAgeSeconds({ capturedAtUtc: 'no-es-fecha' }, NOW)).toBeNull()
    expect(hasPing({ capturedAtUtc: 'no-es-fecha' })).toBe(false)
    expect(hasPing(pingAgo(5))).toBe(true)
  })
  it('agoParts pasa a minutos, horas y días en los umbrales', () => {
    expect(agoParts(0)).toEqual({ n: 0, unit: 's' })
    expect(agoParts(59)).toEqual({ n: 59, unit: 's' })
    expect(agoParts(60)).toEqual({ n: 1, unit: 'min' })
    expect(agoParts(3599)).toEqual({ n: 59, unit: 'min' })
    expect(agoParts(3600)).toEqual({ n: 1, unit: 'h' })
    expect(agoParts(86_399)).toEqual({ n: 23, unit: 'h' })
    expect(agoParts(86_400)).toEqual({ n: 1, unit: 'd' })
    expect(agoParts(-5)).toEqual({ n: 0, unit: 's' })
  })
  it('agoText: «hace N s / min / h / d» en español, «N s ago» en inglés y null sin dato', () => {
    expect(agoText(tES, 12)).toBe('hace 12 s')
    expect(agoText(tES, 185)).toBe('hace 3 min')
    expect(agoText(tES, 7300)).toBe('hace 2 h')
    expect(agoText(tES, 200_000)).toBe('hace 2 d')
    expect(agoText(tEN, 12)).toBe('12 s ago')
    expect(agoText(tEN, 185)).toBe('3 min ago')
    expect(agoText(tES, null)).toBeNull()
  })
})

describe('insignia EN VIVO', () => {
  it('reciente = menos de 2 minutos', () => {
    expect(LIVE_WINDOW_SECONDS).toBe(120)
    expect(isRecentPing(pingAgo(119), NOW)).toBe(true)
    expect(isRecentPing(pingAgo(120), NOW)).toBe(false)
    expect(isRecentPing(undefined, NOW)).toBe(false)
  })
  it('hay señal solo si alguna ruta tiene un ping reciente', () => {
    expect(liveSummary([], NOW)).toEqual({ live: false, recent: 0, withPing: 0 })
    expect(liveSummary([trip(), trip({ lastPing: undefined })], NOW).live).toBe(false)
    expect(liveSummary([trip({ lastPing: pingAgo(600) })], NOW)).toEqual({ live: false, recent: 0, withPing: 1 })
    expect(liveSummary([trip({ lastPing: pingAgo(600) }), trip({ lastPing: pingAgo(30) })], NOW)).toEqual({ live: true, recent: 1, withPing: 2 })
  })
})

describe('progreso y próxima llegada', () => {
  it('porcentaje: atendidas (entregadas + fallidas) sobre el total, sin pasarse de 0–100 ni dividir por cero', () => {
    expect(percentOf(1, 4)).toBe(25)
    expect(percentOf(2, 3)).toBe(67)
    expect(percentOf(0, 0)).toBe(0)
    expect(percentOf(9, 4)).toBe(100)
    expect(percentOf(-1, 4)).toBe(0)
    expect(tripProgress({ totalStops: 4, completedStops: 1, failedStops: 1 })).toBe(50)
    expect(tripProgress({})).toBe(0)
  })
  it('el progreso del día usa los totales del servidor, ponderado por paradas', () => {
    expect(overallProgress({ totalStops: 12, completedStops: 4, failedStops: 2 })).toBe(50)
    expect(overallProgress({ totalStops: 0 })).toBe(0)
    expect(overallProgress(undefined)).toBe(0)
  })
  it('la próxima llegada es la nextEtaUtc más temprana y dice de qué ruta', () => {
    const a = trip({ publicId: 'a', code: 'A', nextEtaUtc: '2026-10-12T16:30:00' })
    const b = trip({ publicId: 'b', code: 'B', nextEtaUtc: '2026-10-12T15:40:00' })
    const c = trip({ publicId: 'c', code: 'C', nextEtaUtc: null })
    expect(nextArrival([a, b, c])?.trip.code).toBe('B')
    expect(nextArrival([a, b, c])?.etaUtc).toBe('2026-10-12T15:40:00')
    expect(nextArrival([c])).toBeNull()
    expect(nextArrival([])).toBeNull()
    expect(nextArrival([trip({ nextEtaUtc: 'basura' })])).toBeNull()
  })
})

describe('telemetría', () => {
  it('rumbo en grados → punto cardinal de 8 vientos, con vuelta y negativos', () => {
    expect([0, 22, 23, 45, 90, 135, 180, 225, 270, 315, 337, 338, 359].map(compassOf)).toEqual(['N', 'N', 'NE', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW', 'NW', 'N', 'N'])
    expect(compassOf(360)).toBe('N')
    expect(compassOf(450)).toBe('E')
    expect(compassOf(-90)).toBe('W')
    expect(compassOf(null)).toBeNull()
    expect(compassOf(undefined)).toBeNull()
    expect(compassOf(Number.NaN)).toBeNull()
  })
  it('los puntos cardinales llevan sus nombres en español (SO, O, NO) y en inglés (SW, W, NW)', () => {
    expect(tES('monitor.compass.SW')).toBe('SO')
    expect(tES('monitor.compass.W')).toBe('O')
    expect(tES('monitor.compass.NW')).toBe('NO')
    expect(tEN('monitor.compass.SW')).toBe('SW')
  })
  it('velocidad redondeada a km/h entero; negativa o sin dato no es velocidad', () => {
    expect(speedOf(42.4)).toBe(42)
    expect(speedOf(42.5)).toBe(43)
    expect(speedOf(0)).toBe(0)
    expect(speedOf(-1)).toBeNull()
    expect(speedOf(null)).toBeNull()
    expect(speedOf(Number.POSITIVE_INFINITY)).toBeNull()
  })
})

describe('mapeo de estados de parada (los de la ruta en el plano)', () => {
  it('pendiente, en curso, entregada y fallida', () => {
    expect(stateOf('PENDING')).toBe('pending')
    expect(stateOf('ON_THE_WAY')).toBe('current')
    expect(stateOf('ARRIVED')).toBe('current')
    expect(stateOf('COMPLETED')).toBe('done')
    expect(stateOf('FAILED')).toBe('failed')
    expect(stateOf(null)).toBe('pending')
  })
})

describe('selección y consulta', () => {
  const list = [trip({ publicId: 'a' }), trip({ publicId: 'b' })]
  it('la ruta de ?trip si está en la lista; si no, la primera; cargando, la del enlace', () => {
    expect(pickSelected(list, 'b', false)).toBe('b')
    expect(pickSelected(list, 'zzz', false)).toBe('a')
    expect(pickSelected(list, null, false)).toBe('a')
    expect(pickSelected([], 'zzz', true)).toBe('zzz')
    expect(pickSelected([], 'zzz', false)).toBeNull()
    expect(pickSelected([], null, false)).toBeNull()
  })
  it('solo manda lo que tiene valor, y «incluir completadas» siempre explícito', () => {
    expect(buildMonitorQuery({ date: '2026-10-12', zoneId: '', search: '  ', includeCompleted: false })).toEqual({ date: '2026-10-12', includeCompleted: false })
    expect(buildMonitorQuery({ date: '2026-10-12', zoneId: '7', search: ' ana ', includeCompleted: true })).toEqual({ date: '2026-10-12', dispatchZoneId: 7, search: 'ana', includeCompleted: true })
    expect(buildMonitorQuery({ date: '2026-10-12', zoneId: 'x', search: '', includeCompleted: false })).not.toHaveProperty('dispatchZoneId')
  })
})

describe('«Siguiendo en vivo» recordado', () => {
  it('por omisión sí; solo «off» lo apaga; el almacenamiento roto no rompe nada', () => {
    const store = (v: string | null) => ({ getItem: () => v })
    expect(readFollow(store(null))).toBe(true)
    expect(readFollow(store('on'))).toBe(true)
    expect(readFollow(store('off'))).toBe(false)
    expect(readFollow(null)).toBe(true)
    expect(readFollow({ getItem: () => { throw new Error('bloqueado') } })).toBe(true)
  })
  it('guarda on/off bajo su clave y tolera un almacenamiento que falla', () => {
    const setItem = vi.fn()
    writeFollow({ setItem }, false)
    writeFollow({ setItem }, true)
    expect(setItem).toHaveBeenNthCalledWith(1, FOLLOW_STORAGE_KEY, 'off')
    expect(setItem).toHaveBeenNthCalledWith(2, FOLLOW_STORAGE_KEY, 'on')
    expect(() => writeFollow({ setItem: () => { throw new Error('cuota') } }, true)).not.toThrow()
    expect(() => writeFollow(null, true)).not.toThrow()
  })
})

describe('textos', () => {
  const keys = (o: unknown, prefix = ''): string[] =>
    Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => (v && typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]))
  it('es y en tienen exactamente las mismas claves monitor.*', () => {
    expect(keys(en.monitor).sort()).toEqual(keys(es.monitor).sort())
  })
})
