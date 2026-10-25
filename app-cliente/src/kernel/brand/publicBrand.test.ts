// La marca antes de entrar (Lote 219): qué ranura de logo según el tema.
import { pickPublicSlot } from './publicBrand'

describe('pickPublicSlot', () => {
  it('sobre fondo claro el logo normal; sobre oscuro el invertido', () => {
    expect(pickPublicSlot(['lockup', 'lockup-inverted'], false)).toBe('lockup')
    expect(pickPublicSlot(['lockup', 'lockup-inverted'], true)).toBe('lockup-inverted')
  })
  it('si falta el ideal usa el otro y al final la marca cuadrada; sin logos, ninguno', () => {
    expect(pickPublicSlot(['lockup-inverted'], false)).toBe('lockup-inverted')
    expect(pickPublicSlot(['mark'], true)).toBe('mark')
    expect(pickPublicSlot([], false)).toBeNull()
  })
})
