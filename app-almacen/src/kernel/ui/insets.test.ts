import { bottomPadding, MIN_BOTTOM_INSET } from './insets'
import { spacing, touchTarget } from './theme'

// docs/mobile/mejoras-ux-zebra.md §1: la barra de navegación del aparato no debe tapar el último botón.
describe('margen inferior de la app', () => {
  it('el mínimo es más de la mitad del alto de un botón', () => {
    expect(MIN_BOTTOM_INSET).toBeGreaterThan(touchTarget / 2)
  })

  it('max(inset del sistema, 32) + spacing.sm', () => {
    expect(bottomPadding(0)).toBe(32 + spacing.sm) // el aparato reporta 0: se usa el mínimo
    expect(bottomPadding(24)).toBe(32 + spacing.sm) // barra de gestos fina
    expect(bottomPadding(48)).toBe(48 + spacing.sm) // barra de 3 botones bien reportada: la real
    expect(bottomPadding(Number.NaN)).toBe(32 + spacing.sm)
  })
})
