import { biometricSupport, LOCK_AFTER_MINUTES, shouldLock } from './lockRules'

describe('bloqueo con huella: reglas', () => {
  const min = 60_000
  it('no bloquea si «Abrir con huella» está apagado o si nunca salió de primer plano', () => {
    expect(shouldLock(false, 0, 10 * min)).toBe(false)
    expect(shouldLock(true, null, 10 * min)).toBe(false)
  })
  it('bloquea al volver pasados los minutos y no antes', () => {
    expect(shouldLock(true, 0, LOCK_AFTER_MINUTES * min - 1)).toBe(false)
    expect(shouldLock(true, 0, LOCK_AFTER_MINUTES * min)).toBe(true)
    expect(shouldLock(true, 1000, 1000 + 5 * min, 5)).toBe(true)
    expect(shouldLock(true, 1000, 1000 + 4 * min, 5)).toBe(false)
  })
  it('sin lector, con lector sin huella registrada, o listo', () => {
    expect(biometricSupport(false, false)).toBe('unavailable')
    expect(biometricSupport(false, true)).toBe('unavailable')
    expect(biometricSupport(true, false)).toBe('not_enrolled')
    expect(biometricSupport(true, true)).toBe('ready')
  })
})
