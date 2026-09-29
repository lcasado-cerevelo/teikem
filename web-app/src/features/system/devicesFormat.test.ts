import { describe, expect, it } from 'vitest'
import { formatRelative } from './devicesFormat'

describe('formatRelative', () => {
  const now = new Date('2026-09-28T12:00:00Z')

  it('sin fecha devuelve vacío', () => {
    expect(formatRelative(null, 'es', now)).toBe('')
    expect(formatRelative(undefined, 'es', now)).toBe('')
  })

  it('fecha inválida devuelve vacío', () => {
    expect(formatRelative('no-es-fecha', 'es', now)).toBe('')
  })

  it('hace unos minutos', () => {
    const iso = '2026-09-28T11:57:00'
    expect(formatRelative(iso, 'es', now)).toMatch(/hace 3 min/)
  })

  it('hace unas horas', () => {
    const iso = '2026-09-28T10:00:00'
    expect(formatRelative(iso, 'es', now)).toMatch(/hace 2 h/)
  })

  it('hace varios días', () => {
    const iso = '2026-09-22T12:00:00'
    expect(formatRelative(iso, 'es', now)).toMatch(/hace 6 días/)
  })

  it('en inglés', () => {
    const iso = '2026-09-28T11:57:00'
    expect(formatRelative(iso, 'en', now)).toMatch(/3 min\.? ago/)
  })
})
