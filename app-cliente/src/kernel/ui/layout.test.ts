// Vista por aparato (Luis, 2026-10-24): la app instalada y las tabletas y celulares ven la forma móvil; la web en computadora, la ancha.
import { isTouchDevice, isWideWidth } from './layout'

describe('isWideWidth', () => {
  it('web en computadora (puntero fino) con 900 px o más: vista ancha', () => {
    expect(isWideWidth(1440, 'web', false)).toBe(true)
    expect(isWideWidth(900, 'web', false)).toBe(true)
  })
  it('web en computadora con ventana angosta: vista móvil', () => {
    expect(isWideWidth(899, 'web', false)).toBe(false)
  })
  it('una tableta en el navegador (táctil), aunque mida 1024 o 1366 px, ve la vista móvil', () => {
    expect(isWideWidth(1024, 'web', true)).toBe(false)
    expect(isWideWidth(1366, 'web', true)).toBe(false)
  })
  it('la app instalada (Android o iOS) siempre ve la vista móvil, también en tabletas', () => {
    expect(isWideWidth(1280, 'android', true)).toBe(false)
    expect(isWideWidth(1280, 'ios', true)).toBe(false)
  })
})

describe('isTouchDevice', () => {
  const original = window.matchMedia
  afterEach(() => {
    window.matchMedia = original
  })
  it('fuera de la web siempre es un aparato móvil', () => {
    expect(isTouchDevice('android')).toBe(true)
  })
  it('en la web mira el puntero principal: dedo = táctil, ratón = computadora', () => {
    window.matchMedia = ((q: string) => ({ matches: q === '(pointer: coarse)' })) as unknown as typeof window.matchMedia
    expect(isTouchDevice('web')).toBe(true)
    window.matchMedia = (() => ({ matches: false })) as unknown as typeof window.matchMedia
    expect(isTouchDevice('web')).toBe(false)
  })
})
