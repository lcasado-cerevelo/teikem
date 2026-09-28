import { validatePin } from './pinRules'

describe('validatePin', () => {
  it('acepta 4 a 6 dígitos no triviales', () => {
    expect(validatePin('4821')).toBeNull()
    expect(validatePin('482137')).toBeNull()
    expect(validatePin(' 4821 ')).toBeNull()
  })

  it('rechaza longitud fuera de 4-6 o no numérico', () => {
    expect(validatePin('123')).toBe('format')
    expect(validatePin('1234567')).toBe('format')
    expect(validatePin('12a4')).toBe('format')
  })

  it('rechaza dígitos repetidos y secuencias consecutivas', () => {
    expect(validatePin('1111')).toBe('trivial')
    expect(validatePin('1234')).toBe('trivial')
    expect(validatePin('98765')).toBe('trivial')
  })

  it('no confunde un paso irregular con secuencia', () => {
    expect(validatePin('1235')).toBeNull()
  })
})
