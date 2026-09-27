import { describe, expect, it } from 'vitest'
import { parseApiDate } from './dates'

describe('parseApiDate', () => {
  it('una fecha-hora sin zona del API se lee como UTC', () => {
    expect(parseApiDate('2026-09-27T14:00:00.123').toISOString()).toBe('2026-09-27T14:00:00.123Z')
    expect(parseApiDate('2026-09-27T14:00:00').toISOString()).toBe('2026-09-27T14:00:00.000Z')
  })

  it('respeta la zona si la trae', () => {
    expect(parseApiDate('2026-09-27T14:00:00Z').toISOString()).toBe('2026-09-27T14:00:00.000Z')
    expect(parseApiDate('2026-09-27T10:00:00-04:00').toISOString()).toBe('2026-09-27T14:00:00.000Z')
    expect(parseApiDate('2026-09-27T10:00:00+0100').toISOString()).toBe('2026-09-27T09:00:00.000Z')
  })

  it('una fecha sola queda igual (medianoche UTC) y lo inválido da Invalid Date', () => {
    expect(parseApiDate('2026-09-27').toISOString()).toBe('2026-09-27T00:00:00.000Z')
    expect(Number.isNaN(parseApiDate('no-es-fecha').getTime())).toBe(true)
  })
})
