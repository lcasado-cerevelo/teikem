import { cacheKey, minutesAgo } from './lookupLogic'

describe('cacheKey', () => {
  it('normaliza mayúsculas y espacios', () => {
    expect(cacheKey('wh-1', ' abc-1 ')).toBe('wh-1:ABC-1')
  })

  it('el mismo almacén y código dan la misma clave', () => {
    expect(cacheKey('wh-1', 'abc')).toBe(cacheKey('wh-1', 'ABC'))
  })

  it('almacenes distintos dan claves distintas', () => {
    expect(cacheKey('wh-1', 'abc')).not.toBe(cacheKey('wh-2', 'abc'))
  })
})

describe('minutesAgo', () => {
  it('calcula los minutos transcurridos', () => {
    const now = new Date('2026-01-01T12:10:00Z')
    expect(minutesAgo('2026-01-01T12:00:00Z', now)).toBe(10)
  })

  it('nunca da negativo (reloj adelantado)', () => {
    const now = new Date('2026-01-01T12:00:00Z')
    expect(minutesAgo('2026-01-01T12:05:00Z', now)).toBe(0)
  })
})
